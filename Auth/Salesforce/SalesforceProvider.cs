using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Collections;

using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Serialization;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;
using EastFive.Net;
using EastFive.Security.CredentialProvider;
using EastFive.Security.SessionServer;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Services;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Auth.CredentialProviders;

namespace EastFive.Azure.Auth.Salesforce
{
    [IntegrationName(SalesforceProvider.IntegrationName)]
    public class SalesforceProvider : IProvideLogin, IProvideSession, IProvideClaims
    {
        public const string IntegrationName = "Salesforce";

        #region Parameters

        #region Request pararmeters
        private const string requestParamClientId = "client_id";
        private const string requestParamRedirectUri = "redirect_uri";
        private const string requestParamResponseType = "response_type";
        private const string requestParamScope = "scope";
        private const string requestParamResponseMode = "response_mode";
        private const string requestParamState = "state";
        private const string requestParamNonce = "nonce";
        #endregion

        #region Parameter that come back on the redirect
        public const string responseParamCode = "code";
        public const string responseParamState = "state";
        public const string responseParamScope = "scope";
        public const string responseParamRedirectUri = "response_uri";
        #endregion

        #region Parameter that come back in the token exchange
        public const string tokenParamAccessToken = "access_token";
        public const string tokenParamExpiresIn = "expires_in";
        public const string tokenParamIdToken = "id_token";
        public const string tokenParamScope = "scope";
        public const string tokenParamTokenType = "token_type";
        public const string tokenParamRefreshToken = "refresh_token";
        #endregion

        #region Parameters from the Claim Token

        // https://developers.google.com/identity/protocols/oauth2/openid-connect

        /// <summary>
        /// An identifier for the user, unique among all Google accounts and never reused.
        /// A Google account can have multiple email addresses at different points in time,
        /// but the sub value is never changed. Use sub within your
        /// application as the unique-identifier key for the user.
        /// Maximum length of 255 case-sensitive ASCII characters.
        /// </summary>
        public const string claimParamSub = "sub";

        /// <summary>
        /// The user's email address.
        /// This value may not be unique to this user and is not suitable for use as a primary key.
        /// Provided only if your scope included the email scope value.
        /// </summary>
        public const string claimParamEmail = "email";

        /// <summary>
        /// True if the user's e-mail address has been verified; otherwise false.
        /// </summary>
        public const string claimParamEmailVerified = "email_verified";

        /// <summary>
        /// The user's surname(s) or last name(s). Might be provided when a name claim is present.
        /// </summary>
        public const string claimParamFamilyName = "family_name";

        /// <summary>
        /// The user's given name(s) or first name(s). Might be provided when a name claim is present.
        /// </summary>
        public const string claimParamGivenName = "given_name";

        /// <summary>
        /// The user's full name, in a displayable form. Might be provided when:
        /// The request scope included the string "profile"
        /// The ID token is returned from a token refresh
        /// When name claims are present, you can use them to update your app's user records.
        /// </summary>
        /// <remarks>Note that this claim is never guaranteed to be present.</remarks>
        public const string claimParamName = "name";

        /// <summary>
        /// The value of the nonce supplied by your app in the authentication request.
        /// You should enforce protection against replay attacks by ensuring it is presented only once.
        /// </summary>
        public const string claimParamNonce = "nonce";

        /// <summary>
        /// The URL of the user's profile picture. Might be provided when:
        /// The request scope included the string "profile"
        /// The ID token is returned from a token refresh
        /// When picture claims are present, you can use them to update your app's user records.
        /// Note that this claim is never guaranteed to be present.
        /// </summary>
        public const string claimParamPicture = "picture";

        /// <summary>
        /// The URL of the user's profile page. Might be provided when:
        /// The request scope included the string "profile"
        /// The ID token is returned from a token refresh
        /// When profile claims are present, you can use them to update your app's user records.
        /// Note that this claim is never guaranteed to be present.
        /// </summary>
        public const string claimParamProfile = "profile";

        #endregion

        #endregion

        #region Configured Settings
        private string clientId;
        private string clientSecret;
        private Uri authorizationApiBase;
        private Uri tokenEndpoint;
        private string issuer;
        private OAuth.Keys keys;
        #endregion

        public SalesforceProvider(string clientId, string clientSecret,
            Uri authorizationApiBase, Uri tokenEndpoint, string issuer, OAuth.Keys keys)
        {
            this.clientId = clientId;
            this.clientSecret = clientSecret;
            this.authorizationApiBase = authorizationApiBase;
            this.tokenEndpoint = tokenEndpoint;
            this.issuer = issuer;
            this.keys = keys;
        }

        private TResult Parse<TResult>(string jwtEncodedString,
                IDictionary<string, string> responseParams,
            Func<string, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<string, TResult> onInvalidToken)
        {
            return this.keys.Parse(jwtEncodedString,
                    issuer, clientId.AsArray(),
                (subject, jwtSecurityToken, principal) =>
                {
                    var state = responseParams.TryGetValue(responseParamState, out string stateStr) ?
                        Guid.TryParse(stateStr, out Guid stateParsedGuid) ?
                            stateParsedGuid
                            :
                            default(Guid?)
                        :
                        default(Guid?);

                    var sourceClaims = jwtSecurityToken.Claims
                        .Select(claim => claim.Type.PairWithValue(claim.Value))
                        .ToArray();

                    var updatedArgs = principal
                        .Claims
                        .Select(claim => claim.Type.PairWithValue(claim.Value))
                        .Concat(responseParams)
                        .Concat(sourceClaims)
                        .Distinct(kvp => kvp.Key)
                        .ToDictionary();

                    return onSuccess(subject, state, updatedArgs);
                },
                onInvalidToken: onInvalidToken);
        }

        #region IProvideLogin

        #region IProvideAuthorization

        public string Method => IntegrationName;

        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        public Type CallbackController => typeof(SalesforceRedirect);

        public virtual async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!responseParams.ContainsKey(SalesforceProvider.responseParamCode))
                return onInvalidCredentials($"`{SalesforceProvider.responseParamCode}` code was not provided");
            var code = responseParams[SalesforceProvider.responseParamCode];

            if (!responseParams.ContainsKey(SalesforceProvider.responseParamRedirectUri))
                return onInvalidCredentials($"`{SalesforceProvider.responseParamRedirectUri}` code was not provided");
            var redirectUri = responseParams[SalesforceProvider.responseParamRedirectUri];

            return await SalesforceTokenResponse.LoadTokenAsync(this.tokenEndpoint,
                    code, this.clientId, this.clientSecret, redirectUri,
                (tokenResponse) =>
                {
                    var extraParamsWithTokenValues = tokenResponse.AppendResponseParameters(responseParams);

                    if(tokenResponse.id.IsNotDefaultOrNull())
                    {
                        // https://help.salesforce.com/s/articleView?id=sf.remoteaccess_using_openid.htm&type=5
                        // tokenResponse.id.HttpClientGetResourceAsync()
                    }
                    return Parse(tokenResponse.id_token, extraParamsWithTokenValues,
                        (subject, authorizationId, extraParamsWithClaimValues) =>
                            onSuccess(subject, authorizationId, default(Guid?), extraParamsWithClaimValues),
                        (why) => onFailure(why));
                },
                onFailure);
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, Guid?, Guid?, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return GetSubject(
                subject =>
                {
                    var state = responseParams.ContainsKey(responseParamState) ?
                           Guid.TryParse(responseParams[responseParamState], out Guid stateParsedGuid) ?
                               stateParsedGuid
                               :
                               default(Guid?)
                           :
                           default(Guid?);

                    return onSuccess(subject, state, default(Guid?));
                });

            TResult GetSubject(Func<string, TResult> callback)
            {
                if (responseParams.ContainsKey(claimParamSub))
                {
                    var subject = responseParams[claimParamSub];
                    if (subject.HasBlackSpace())
                        return callback(subject);
                }

                if (responseParams.ContainsKey(SalesforceProvider.tokenParamIdToken))
                {
                    var jwtEncodedString = responseParams[SalesforceProvider.tokenParamIdToken];
                    return Parse(jwtEncodedString, responseParams,
                        (subject, state, updatedParamsDiscard) => callback(subject),
                        onFailure);
                }

                return onFailure($"Could not locate {claimParamSub} in params or claims.");
            }
        }

        #endregion

        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            var tokenUrl = this.authorizationApiBase
                .AddQueryParameter(requestParamClientId, this.clientId)
                //.AddQueryParameter(requestParamResponseMode, "form_post")
                .AddQueryParameter(requestParamResponseType, $"{responseParamCode}")
                .AddQueryParameter(requestParamScope, "full+refresh_token")
                .AddQueryParameter(requestParamState, state.ToString("N"))
                .AddQueryParameter(requestParamNonce, Guid.NewGuid().ToString("N"))
                .AddQueryParameter(requestParamRedirectUri, responseControllerLocation.AbsoluteUri);
            return tokenUrl;
        }

        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        #endregion

        #region IProvideSession

        public Task<bool> SupportsSessionAsync(EastFive.Azure.Auth.Session session)
        {
            return true.AsTask();
        }

        #endregion

        #region IProvideClaims

        public bool TryGetStandardClaimValue(string claimType,
            IDictionary<string, string> parameters, out string claimValue)
        {
            if (claimType.IsNullOrWhiteSpace())
            {
                claimValue = default;
                return false;
            }
            if (parameters.ContainsKey(claimType))
            {
                claimValue = parameters[claimType];
                return true;
            }
            if (System.Security.Claims.ClaimTypes.NameIdentifier.Equals(claimType, StringComparison.OrdinalIgnoreCase))
                if (parameters.ContainsKey(claimParamSub))
                {
                    claimValue = parameters[claimParamSub];
                    return true;
                }
            if (System.Security.Claims.ClaimTypes.Email.Equals(claimType, StringComparison.OrdinalIgnoreCase))
                if (parameters.ContainsKey(claimParamEmail))
                {
                    claimValue = parameters[claimParamEmail];
                    return true;
                }
            if (System.Security.Claims.ClaimTypes.GivenName.Equals(claimType, StringComparison.OrdinalIgnoreCase))
                if (parameters.ContainsKey(claimParamGivenName))
                {
                    claimValue = parameters[claimParamGivenName];
                    return true;
                }

            claimValue = default;
            return false;
        }

        #endregion

    }

    public class SalesforceProviderAttribute : Attribute, IProvideLoginProvider
    {
        public Task<TResult> ProvideLoginProviderAsync<TResult>(
            Func<IProvideLogin, TResult> onProvideAuthorization,
            Func<string, TResult> onNotAvailable)
        {
            return AppSettings.Auth.Salesforce.ConsumerKey.ConfigurationString(
                consumerKey =>
                {
                    return AppSettings.Auth.Salesforce.ConsumerSecret.ConfigurationString(
                        async (consumerSecret) =>
                        {
                            return await await new Uri(discoveryDocumentUrl).HttpClientGetResourceAsync(
                                (DiscoveryDocument discDoc) =>
                                {
                                    return OAuth.Keys.LoadTokenKeysAsync(discDoc.jwks_uri,
                                        keys =>
                                        {
                                            var provider = new SalesforceProvider(consumerKey, consumerSecret,
                                                discDoc.authorization_endpoint, discDoc.token_endpoint, discDoc.issuer, keys);
                                            return onProvideAuthorization(provider);
                                        },
                                        onNotAvailable);
                                },
                                onFailure: (why) => onNotAvailable(why).AsTask());
                        },
                        (why) => onNotAvailable(why).AsTask());
                },
                (why) => onNotAvailable(why).AsTask());
        }

        public const string discoveryDocumentUrl = "https://login.salesforce.com/.well-known/openid-configuration";

        public class DiscoveryDocument
        {
            public Uri authorization_endpoint;
            public Uri token_endpoint;
            public Uri userinfo_endpoint;
            public Uri jwks_uri;
            public string issuer; //https://accounts.google.com
        }
    }

    public static class SalesforceProviderExtensions
    {
        public static bool IsSalesforce(this Method authMethod)
        {
            return authMethod.name == SalesforceProvider.IntegrationName;
        }
    }

}

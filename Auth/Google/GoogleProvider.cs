using EastFive.Security.CredentialProvider;
using Newtonsoft.Json;
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
using EastFive.Security.SessionServer.Persistence;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using EastFive.Api.Services;
using System.Security.Claims;
using EastFive.Security.SessionServer;
using System.Collections;
using EastFive.Serialization;
using EastFive.Web.Configuration;
using EastFive.Extensions;
using EastFive.Api.Controllers;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Azure.Auth;
using EastFive;
using EastFive.Api;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using EastFive.Api.Azure.Credentials;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Auth.CredentialProviders;
using EastFive.Net;

namespace EastFive.Azure.Auth
{
    [IntegrationName(GoogleProvider.IntegrationName)]
    public class GoogleProvider : IProvideLogin, IProvideSession
    {
        public const string IntegrationName = "Google";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        private const string appleAuthServerUrl = "https://appleid.apple.com/auth/authorize";
        private const string appleKeyServerUrl = "https://appleid.apple.com/auth/keys";

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

        #region Claim Tokens

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

        #region Configured Settings
        private string clientId;
        private string clientSecret;
        private Uri authorizationApiBase;
        private Uri tokenEndpoint;
        #endregion

        public GoogleProvider(string clientId, string clientSecret,
            Uri authorizationApiBase, Uri tokenEndpoint)
        {
            this.clientId = clientId;
            this.clientSecret = clientSecret;
            this.authorizationApiBase = authorizationApiBase;
            this.tokenEndpoint = tokenEndpoint;
        }

        //[IntegrationName(AppleProvider.IntegrationName)]
        //public static Task<TResult> InitializeAsync<TResult>(
        //    Func<IProvideAuthorization, TResult> onProvideAuthorization,
        //    Func<TResult> onProvideNothing,
        //    Func<string, TResult> onFailure)
        //{
        //    return AppSettings.Auth.Google.ClientId.ConfigurationString(
        //        applicationId =>
        //        {
        //            return AppSettings.Auth.Google.ClientSecret.ConfigurationString(
        //                (clientSecret) =>
        //                {
        //                    return new Uri(discoveryDocumentUrl).HttpClientGetResource(
        //                        (DiscoveryDocument discDoc) =>
        //                        {
        //                            var provider = new GoogleProvider(applicationId, clientSecret,
        //                                discDoc.authorization_endpoint, discDoc.token_endpoint);
        //                            return onProvideAuthorization(provider);
        //                        },
        //                        onFailure: (why) => onFailure(why));
                            
        //                },
        //                (why) => onProvideNothing().AsTask());
        //        },
        //        (why) => onProvideNothing().AsTask());
        //}

        public Type CallbackController => typeof(GoogleRedirect);

        public class Keys
        {
            public Key[] keys { get; set; }
        }

        public class Key
        {
            public string kty { get; set; }
            public string kid { get; set; }
            public string use { get; set; }
            public string alg { get; set; }
            public string n { get; set; }
            public string e { get; set; }
        }

        public virtual async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!responseParams.ContainsKey(GoogleProvider.responseParamCode))
                return onInvalidCredentials($"`{GoogleProvider.responseParamCode}` code was not provided");
            var code = responseParams[GoogleProvider.responseParamCode];

            if (!responseParams.ContainsKey(GoogleProvider.responseParamRedirectUri))
                return onInvalidCredentials($"`{GoogleProvider.responseParamRedirectUri}` code was not provided");
            var redirectUri = responseParams[GoogleProvider.responseParamRedirectUri];

            using (var httpClient = new HttpClient())
            {
                var postValues = new Dictionary<string, string>()
                    {
                        { "code", code },
                        { "client_id", this.clientId },
                        { "client_secret", this.clientSecret },
                        { "redirect_uri", "http://localhost:54610/auth/GoogleRedirect" }, //redirectUri },
                        { "grant_type", "authorization_code" },
                    };
                using (var body = new FormUrlEncodedContent(postValues))
                {
                        try
                        {
                            var response = await httpClient.PostAsync(this.tokenEndpoint, body);
                            var content = await response.Content.ReadAsStringAsync();
                            if (!response.IsSuccessStatusCode)
                                return onFailure(content);

                            var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(content);
                            return Parse(tokenResponse.id_token, responseParams,
                                (subject, authorizationId, extraParamsWithTokenValues) =>
                                    onSuccess(subject, authorizationId, default(Guid?), extraParamsWithTokenValues),
                                (why) => onFailure(why));
                        }
                        catch (System.Net.Http.HttpRequestException ex)
                        {
                            return onCouldNotConnect($"{ex.GetType().FullName}:{ex.Message}");
                        }
                        catch (Exception exGeneral)
                        {
                            return onCouldNotConnect(exGeneral.Message);
                        }
                }
            }
        }

        public class TokenResponse
        {
            public string access_token;
            public int expires_in;
            public string scope;
            /// <summary>
            /// Bearer
            /// </summary>
            public string token_type;
            /// <summary>
            /// JWT
            /// </summary>
            public string id_token;
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
                if (responseParams.ContainsKey("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"))
                {
                    var subject = responseParams["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"];
                    if (subject.HasBlackSpace())
                        return callback(subject);
                }
                if (responseParams.ContainsKey(AppleProvider.responseParamIdToken))
                {
                    var jwtEncodedString = responseParams[AppleProvider.responseParamIdToken];
                    var handler = new JwtSecurityTokenHandler();
                    var token = handler.ReadJwtToken(jwtEncodedString);
                    return token.Claims
                        .Where(claim => claim.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")
                        .First(
                            (claim, next) => callback(claim.Value),
                            () => onFailure("No claim http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"));
                }

                return onFailure($"Could not locate http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier in params or claims.");
            }
        }

        private static TResult Parse<TResult>(string jwtEncodedString,
                IDictionary<string, string> responseParams,
            Func<string, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<string, TResult> onInvalidToken)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtSecurityToken = handler.ReadJwtToken(jwtEncodedString);

                var claims = jwtSecurityToken.Claims.ToArray();
                return claims
                    .Where(claim => claim.Type == claimParamSub)
                    .First(
                        (claim, next) =>
                        {
                            var subject = claim.Value;
                            var state = responseParams.ContainsKey(responseParamState) ?
                                Guid.TryParse(responseParams[responseParamState], out Guid stateParsedGuid) ?
                                    stateParsedGuid
                                    :
                                    default(Guid?)
                                :
                                default(Guid?);

                            var updatedArgs = claims
                                .Select(claim => claim.Type.PairWithValue(claim.Value))
                                .Concat(responseParams)
                                .Distinct(kvp => kvp.Key)
                                .ToDictionary();

                            return onSuccess(subject, state, updatedArgs);
                        },
                        () => onInvalidToken("Token does not identify any specific user."));
            }
            catch (ArgumentException ex)
            {
                return onInvalidToken(ex.Message);
            }
            catch (Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException ex)
            {
                return onInvalidToken(ex.Message);
            }
            catch (Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException ex)
            {
                return onInvalidToken(ex.Message);
            }
            catch (Microsoft.IdentityModel.Tokens.SecurityTokenException ex)
            {
                return onInvalidToken(ex.Message);
            }
        }

        #region IProvideLogin

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
                .AddQueryParameter(requestParamScope, "openid email")
                .AddQueryParameter(requestParamState, state.ToString("N"))
                .AddQueryParameter(requestParamNonce, Guid.NewGuid().ToString("N"))
                .AddQueryParameter(requestParamRedirectUri, responseControllerLocation.AbsoluteUri);
            return tokenUrl;
        }

        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Task<bool> SupportsSessionAsync(EastFive.Azure.Auth.Session session)
        {
            return true.AsTask();
        }

        #endregion

        #region IProvideAccountInformation

        protected (string, string) ParseAccountInfo(IDictionary<string, string> extraParameters)
        {
            var name = "Google User";
            var email = string.Empty;
            if (extraParameters.ContainsKey(claimParamName))
                name = extraParameters[claimParamName];

            if (extraParameters.ContainsKey(claimParamEmail))
                email = extraParameters[claimParamName];

            return (name, email);
        }

        #endregion 
    }

    public class GoogleProviderAttribute : Attribute, IProvideLoginProvider
    {
        public Task<TResult> ProvideLoginProviderAsync<TResult>(
            Func<IProvideLogin, TResult> onProvideAuthorization,
            Func<string, TResult> onNotAvailable)
        {
            return AppSettings.Auth.Google.ClientId.ConfigurationString(
                applicationId =>
                {
                    return AppSettings.Auth.Google.ClientSecret.ConfigurationString(
                        (clientSecret) =>
                        {
                            return new Uri(discoveryDocumentUrl).HttpClientGetResource(
                                (DiscoveryDocument discDoc) =>
                                {
                                    var provider = new GoogleProvider(applicationId, clientSecret,
                                        discDoc.authorization_endpoint, discDoc.token_endpoint);
                                    return onProvideAuthorization(provider);
                                },
                                onFailure: (why) => onNotAvailable(why));
                        },
                        (why) => onNotAvailable(why).AsTask());
                },
                (why) => onNotAvailable(why).AsTask());
        }

        public const string discoveryDocumentUrl = "https://accounts.google.com/.well-known/openid-configuration";

        public class DiscoveryDocument
        {
            public Uri authorization_endpoint;
            public Uri token_endpoint;
            public Uri userinfo_endpoint;
            public Uri jwks_uri;
        }
    }
}

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

namespace EastFive.Azure.Auth
{
    [IntegrationName(AppleProvider.IntegrationName)]
    public class AppleProvider : IProvideLogin, IProvideSession
    {
        public const string IntegrationName = "Apple";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        private const string appleAuthServerUrl = "https://appleid.apple.com/auth/authorize";
        private const string appleSubjectClaimKey = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";

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
        public const string responseParamIdToken = "id_token";
        public const string responseParamUser = "user";
        #endregion

        #region Configured Settings
        private string applicationId;
        private string[] validAudiences;
        private OAuth.Keys keys;
        #endregion

        public AppleProvider(string applicationId, string [] validAudiences, OAuth.Keys keys)
        {
            this.applicationId = applicationId;
            this.validAudiences = validAudiences;
            this.keys = keys;
        }

        //[IntegrationName(AppleProvider.IntegrationName)]
        //public static Task<TResult> InitializeAsync<TResult>(
        //    Func<IProvideAuthorization, TResult> onProvideAuthorization,
        //    Func<TResult> onProvideNothing,
        //    Func<string, TResult> onFailure)
        //{
        //    return AppSettings.Auth.Apple.ClientId.ConfigurationString(
        //        applicationId =>
        //        {
        //            return AppSettings.Auth.Apple.ValidAudiences.ConfigurationString(
        //                (validAudiencesStr) =>
        //                {
        //                    return OAuth.Keys.LoadTokenKeysAsync(new Uri(appleKeyServerUrl),
        //                        keys =>
        //                        {
        //                            var validAudiences = validAudiencesStr.Split(','.AsArray());
        //                            var provider = new AppleProvider(applicationId, validAudiences, keys);
        //                            return onProvideAuthorization(provider);
        //                        },
        //                        onFailure: onFailure);
        //                },
        //                (why) => onProvideNothing().AsTask());
        //        },
        //        (why) => onProvideNothing().AsTask());
        //}

        public Type CallbackController => typeof(AppleRedirect);

        public virtual async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!responseParams.ContainsKey(AppleProvider.responseParamIdToken))
                return onInvalidCredentials($"`{AppleProvider.responseParamIdToken}` code was not provided");
            var idTokenJwt = responseParams[AppleProvider.responseParamIdToken];
            var issuer = "https://appleid.apple.com";

            return await keys.Parse(
                    idTokenJwt, issuer, validAudiences,
                (subject, jwtToken, principal) =>
                {
                    var state = responseParams.TryGetValue(responseParamState, out string stateStr) ?
                        Guid.TryParse(stateStr, out Guid stateParsedGuid) ?
                            stateParsedGuid
                            :
                            default(Guid?)
                        :
                        default(Guid?);

                    var extraParamsWithClaimValues = principal
                        .Claims
                        .Select(claim => claim.Type.PairWithValue(claim.Value))
                        .Concat(responseParams)
                        .Distinct(kvp => kvp.Key)
                        .ToDictionary();

                    return onSuccess(subject, state, default(Guid?), extraParamsWithClaimValues);
                },
                onFailure).AsTask();
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
                if(responseParams.ContainsKey(appleSubjectClaimKey))
                {
                    var subject = responseParams[appleSubjectClaimKey];
                    if(subject.HasBlackSpace())
                        return callback(subject);
                }
                if(responseParams.ContainsKey(AppleProvider.responseParamIdToken))
                {
                    var jwtEncodedString = responseParams[AppleProvider.responseParamIdToken];
                    var handler = new JwtSecurityTokenHandler();
                    var token = handler.ReadJwtToken(jwtEncodedString);
                    return token.Claims
                        .Where(claim => claim.Type == appleSubjectClaimKey)
                        .First(
                            (claim, next) => callback(claim.Value),
                            () => onFailure($"No claim {appleSubjectClaimKey}"));
                }

                return onFailure($"Could not locate {appleSubjectClaimKey} in params or claims.");
            }
        }

        #region IProvideLogin

        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            var tokenUrl = new Uri(appleAuthServerUrl)
                .AddQueryParameter(requestParamClientId, applicationId)
                .AddQueryParameter(requestParamResponseMode, "form_post")
                .AddQueryParameter(requestParamResponseType, $"{responseParamCode} {responseParamIdToken}")
                .AddQueryParameter(requestParamScope, "name email")
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
            var name = "Apple User";
            var email = string.Empty;
            if (extraParameters.ContainsKey(responseParamUser))
            {
                var userInfoJson = extraParameters[responseParamUser];
                var userInfo = JsonConvert.DeserializeObject<UserInfo>(userInfoJson);
                name = $"{userInfo.name.firstName} {userInfo.name.lastName}";
                email = userInfo.email;
            }
            if (extraParameters.ContainsKey(UserInfo.NamePropertyName))
            {
                name = extraParameters[UserInfo.NamePropertyName];
            }
            if (extraParameters.ContainsKey(UserInfo.EmailPropertyName))
            {
                email = extraParameters[UserInfo.EmailPropertyName];
            }
            return (name, email);
        }

        /// <summary>
        /// {"name":{"firstName":"Joshua","middleName":"","lastName":"Wingstrom"},"email":"NOTxdvaa1g6zj@privaterelay.appleid.com"}
        /// </summary>
        public class UserInfo
        {
            public const string NamePropertyName = "name";
            public Name name { get; set; }

            public const string EmailPropertyName = "email";
            public string email { get; set; }
        }

        public class Name
        {
            public string firstName { get; set; }
            public string middleName { get; set; }
            public string lastName { get; set; }
        }

        #endregion 
    }


    public class AppleProviderAttribute : Attribute, IProvideLoginProvider
    {
        private const string appleKeyServerUrl = "https://appleid.apple.com/auth/keys";

        public Task<TResult> ProvideLoginProviderAsync<TResult>(
            Func<IProvideLogin, TResult> onLoaded,
            Func<string, TResult> onNotAvailable)
        {
            return AppSettings.Auth.Apple.ClientId.ConfigurationString(
                applicationId =>
                {
                    return AppSettings.Auth.Apple.ValidAudiences.ConfigurationString(
                        (validAudiencesStr) =>
                        {
                            var validAudiences = validAudiencesStr.Split(','.AsArray());
                            return OAuth.Keys.LoadTokenKeysAsync(new Uri(appleKeyServerUrl),
                                keys =>
                                {
                                    var provider = new AppleProvider(applicationId, validAudiences, keys);
                                    return onLoaded(provider);
                                },
                                onFailure: onNotAvailable);
                        },
                        onNotAvailable.AsAsyncFunc());
                },
                onNotAvailable.AsAsyncFunc());
        }
    }
}

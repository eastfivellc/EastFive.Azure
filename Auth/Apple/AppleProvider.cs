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
using EastFive.Security.CredentialProvider;
using EastFive.Api.Services;
using EastFive.Serialization;
using EastFive.Web.Configuration;
using EastFive.Extensions;
using EastFive.Api.Controllers;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Azure.Auth;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Auth.CredentialProviders;

namespace EastFive.Azure.Auth
{
    [IntegrationName(AppleProvider.IntegrationName)]
    public class AppleProvider : IProvideLogin, IProvideSession, IProvideClaims
    {
        #region Properties

        public const string IntegrationName = "Apple";
        public string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        private const string appleAuthServerUrl = "https://appleid.apple.com/auth/authorize";
        private const string appleSubjectClaimKey = System.Security.Claims.ClaimTypes.NameIdentifier; // "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";

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

        #endregion

        public AppleProvider(string applicationId, string [] validAudiences, OAuth.Keys keys)
        {
            this.applicationId = applicationId;
            this.validAudiences = validAudiences;
            this.keys = keys;
        }

        #region IProvideLogin

        #region IProvideAuthorization

        public Type CallbackController => typeof(AppleRedirect);

        public virtual async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<IDictionary<string, string>, TResult> onSuccess,
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
                    var extraParamsWithClaimValues = principal
                        .Claims
                        .Select(claim => claim.Type.PairWithValue(claim.Value))
                        .Concat(responseParams)
                        .Distinct(kvp => kvp.Key)
                        .ToDictionary();

                    return onSuccess(extraParamsWithClaimValues);
                },
                onFailure).AsTask();
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, IRefOptional<Authorization>, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return GetSubject(
                subject =>
                {
                    var state = GetState();
                    return onSuccess(subject, state);
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

            IRefOptional<Authorization> GetState()
            {
                if (!responseParams.TryGetValue(responseParamState, out string stateValue))
                    return RefOptional<Authorization>.Empty();

                RefOptional<Authorization>.TryParse(stateValue, out IRefOptional<Authorization> stateId);
                return stateId;
            }
        }

        #endregion


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
                if(parameters.ContainsKey(responseParamIdToken))
                {
                    claimValue = parameters[responseParamIdToken];
                    return true;
                }

            if (System.Security.Claims.ClaimTypes.Email.Equals(claimType, StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.ContainsKey(UserInfo.EmailPropertyName))
                {
                    claimValue = parameters[UserInfo.EmailPropertyName];
                    return true;
                }

                if (TryGetUserInfo(out UserInfo userInfo))
                {
                    claimValue = userInfo.email;
                    return true;
                }

                claimValue = string.Empty;
                return true;
            }

            if (System.Security.Claims.ClaimTypes.Name.Equals(claimType, StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.ContainsKey(UserInfo.NamePropertyName))
                {
                    claimValue = parameters[UserInfo.NamePropertyName];
                    return true;
                }

                if (TryGetUserInfo(out UserInfo userInfo))
                {
                    claimValue = $"{userInfo.name.firstName} {userInfo.name.lastName}";
                    return true;
                }

                claimValue = "Apple User";
                return true;
            }

            if (System.Security.Claims.ClaimTypes.GivenName.Equals(claimType, StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetUserInfo(out UserInfo userInfo))
                {
                    claimValue = userInfo.name.firstName;
                    return true;
                }
            }

            if (System.Security.Claims.ClaimTypes.Surname.Equals(claimType, StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetUserInfo(out UserInfo userInfo))
                {
                    claimValue = userInfo.name.lastName;
                    return true;
                }
            }

            claimValue = default;
            return false;

            bool TryGetUserInfo(out UserInfo userInfo)
            {
                if (!parameters.ContainsKey(responseParamUser))
                {
                    userInfo = default;
                    return false;
                }
                var userInfoJson = parameters[responseParamUser];
                userInfo = JsonConvert.DeserializeObject<UserInfo>(userInfoJson);
                return true;
            }
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
}

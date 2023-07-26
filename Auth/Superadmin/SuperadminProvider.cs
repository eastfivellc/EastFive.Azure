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
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Services;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Auth.CredentialProviders;
using EastFive.Azure.Login;
using EastFive.Azure.Auth.OAuth;

namespace EastFive.Azure.Auth.Superadmin
{
    [IntegrationName(SuperadminProvider.IntegrationName)]
    public class SuperadminProvider : IProvideLogin, IProvideSession, IProvideClaims
    {
        public const string IntegrationName = "Superadmin";

        #region Parameters

        #region Request pararmeters
        private const string requestParamClientId = "client_id";
        #endregion

        #region Parameter that come back on the redirect
        public const string responseParamCode = "code";
        public const string responseParamState = "state";
        #endregion

        #region Parameters from the Claim Token

        // https://developers.Superadmin.com/identity/protocols/oauth2/openid-connect

        /// <summary>
        /// An identifier for the user, unique among all Superadmin accounts and never reused.
        /// A Superadmin account can have multiple email addresses at different points in time,
        /// but the sub value is never changed. Use sub within your
        /// application as the unique-identifier key for the user.
        /// Maximum length of 255 case-sensitive ASCII characters.
        /// </summary>
        public const string claimParamSub = "sub";

        #endregion

        #endregion

        #region Configured Settings
        private string clientId;
        private string clientSecret;
        #endregion

        public SuperadminProvider(string clientId, string clientSecret)
        {
            this.clientId = clientId;
            this.clientSecret = clientSecret;
        }

        #region IProvideLogin

        #region IProvideAuthorization

        public string Method => IntegrationName;

        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        public Type CallbackController => typeof(SuperadminRedirect);

        public virtual async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> responseParams,
            Func<IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!responseParams.ContainsKey(SuperadminProvider.responseParamCode))
                return onInvalidCredentials($"`{SuperadminProvider.responseParamCode}` code was not provided");
            var code = responseParams[SuperadminProvider.responseParamCode];

            return onFailure("Not implemented.");
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
                if (responseParams.ContainsKey(claimParamSub))
                {
                    var subject = responseParams[claimParamSub];
                    if (subject.HasBlackSpace())
                        return callback(subject);
                }

                return onFailure($"Could not locate {claimParamSub} in params or claims.");
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

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            
            throw new NotImplementedException();
        }

        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            throw new NotImplementedException();
        }

        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            throw new NotImplementedException();
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

            claimValue = default;
            return false;
        }

        #endregion

    }

    public class SuperadminProviderAttribute : Attribute, IProvideLoginProvider
    {
        public Task<TResult> ProvideLoginProviderAsync<TResult>(
            Func<IProvideLogin, TResult> onProvideAuthorization,
            Func<string, TResult> onNotAvailable)
        {
            var applicationId = EastFive.Security.SecureGuid.Generate().ToString("N");
            var clientSecret = EastFive.Security.SecureGuid.Generate().ToString("N");
            var provider = new SuperadminProvider(applicationId, clientSecret);
            return onProvideAuthorization(provider).AsTask();
        }

        public const string discoveryDocumentUrl = "https://accounts.Superadmin.com/.well-known/openid-configuration";

        public class DiscoveryDocument
        {
            public Uri authorization_endpoint;
            public Uri token_endpoint;
            public Uri userinfo_endpoint;
            public Uri jwks_uri;
            public string issuer; //https://accounts.Superadmin.com
        }
    }

    public static class SuperadminProviderExtensions
    {
        public static bool IsSuperadmin(this Method authMethod)
        {
            return authMethod.name == SuperadminProvider.IntegrationName;
        }
    }

}

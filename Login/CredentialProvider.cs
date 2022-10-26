using EastFive.Api.Azure;
using EastFive.Azure.Auth;
using EastFive.Azure.Auth.CredentialProviders;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Serialization;
using EastFive.Web.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Login
{
    public class CredentialProvider : IProvideLogin, Auth.IProvideSession, IProvideLoginManagement
    {
        public const string IntegrationName = "Login";
        public virtual string Method => IntegrationName;
        public Guid Id => System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        #region Initialization

        // For easy environment management
        public const string referrerKey = "referrer";

        // Authorization
        private const string tokenKey = "token";
        private const string stateKey = "state";
        public const string accountIdKey = "account_id";

        // API Access
        public const string accessTokenKey = "access_token";
        public const string refreshTokenKey = "refresh_token";

        private readonly Guid clientId;
        private readonly string clientSecret;

        public CredentialProvider(Guid clientKey, string clientSecret)
        {
            this.clientId = clientKey;
            this.clientSecret = clientSecret;
        }

        //[IntegrationName(IntegrationName)]
        //public static async Task<TResult> InitializeAsync<TResult>(
        //    Func<IProvideAuthorization, TResult> onProvideAuthorization,
        //    Func<TResult> onProvideNothing,
        //    Func<string, TResult> onFailure)
        //{
        //    return await LoadFromConfig(
        //        (provider) => onProvideAuthorization(provider),
        //        (why) => onFailure(why)).AsTask();
        //}

        #endregion

        #region IProvideAuthorization

        public async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> tokenParameters,
            Func<IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!tokenParameters.ContainsKey(CredentialProvider.tokenKey))
                return onInvalidCredentials($"Parameter with name [{CredentialProvider.tokenKey}] was not provided");
            var accessToken = tokenParameters[CredentialProvider.tokenKey];

            if (!tokenParameters.ContainsKey(CredentialProvider.stateKey))
                return onInvalidCredentials($"Parameter with name [{CredentialProvider.stateKey}] was not provided");
            var stateLookup = tokenParameters[CredentialProvider.stateKey];

            try
            {
                return await Guid.Parse(stateLookup).AsRef<Authentication>().StorageGetAsync(
                        authentication =>
                        {
                            var subject = authentication.userIdentification;
                            var state = authentication.authorizationMaybe.GetIdMaybeNullSafe();
                            var extraParams = new Dictionary<string, string>
                            {
                                //{  CredentialProvider.accessTokenKey, apiAccessToken },
                                //{  CredentialProvider.refreshTokenKey, apiRefreshToken },
                                {  CredentialProvider.accountIdKey, subject },
                                {  CredentialProvider.stateKey, state.HasValue? state.Value.ToString() : null },
                                //{  CredentialProvider.tokenKey, accessToken },
                            };
                            if (!authentication.authenticated.HasValue)
                                return onUnauthenticated(default(Guid?), extraParams);

                            return onSuccess(extraParams);
                        },
                        () =>
                        {
                            return onInvalidCredentials($"`stateLookup` is not a valid {typeof(Authentication).FullName}");
                        });
            }
            catch (Exception ex)
            {
                return onCouldNotConnect(ex.Message);
            }
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> tokenParameters,
            Func<string, IRefOptional<Authorization>, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (!tokenParameters.TryGetValue(CredentialProvider.accountIdKey, out string subject))
                return onFailure($"Parameter with name [{CredentialProvider.accountIdKey}] was not provided");

            var state = GetState();

            return onSuccess(subject, state);

            IRefOptional<Authorization> GetState()
            {
                if (!tokenParameters.TryGetValue(CredentialProvider.stateKey, out string stateValue))
                    return RefOptional<Authorization>.Empty();

                RefOptional<Authorization>.TryParse(stateValue, out IRefOptional<Authorization> stateId);
                return stateId;
            }
        }

        #endregion

        #region IProvideLogin

        public Type CallbackController => typeof(Redirection);

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            var baseLoginUrl = controllerToLocation(typeof(Authentication));
            var validation = state.ToByteArray().Concat(this.clientSecret.FromBase64String()).ToArray().MD5HashGuid();
            var url = new Uri(baseLoginUrl, 
                $"?{Authentication.AuthorizationPropertyName}={state}" + 
                $"&{Authentication.ClientPropertyName}={clientId}" + 
                $"&{Authentication.ValidationPropertyName}={validation}");
            return url;
        }

        public Uri GetLogoutUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        public Uri GetSignupUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            return default(Uri);
        }

        #endregion

        #region IProvideSession

        public Task<bool> SupportsSessionAsync(Auth.Session session)
        {
            return true.AsTask();
        }

        #endregion

        #region IProvideLoginManagement

        public Task<TResult> CreateAuthorizationAsync<TResult>(string displayName, string userId,
                bool isEmail, string secret, bool forceChange,
            Func<Guid, TResult> onSuccess,
            Func<Guid, TResult> usernameAlreadyInUse,
            Func<TResult> onPasswordInsufficent,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> GetAuthorizationAsync<TResult>(Guid loginId,
            Func<LoginInfo, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> GetAllAuthorizationsAsync<TResult>(
            Func<LoginInfo[], TResult> onFound, Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported, Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> UpdateAuthorizationAsync<TResult>(Guid loginId,
                string password, bool forceChange,
            Func<TResult> onSuccess, Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported, Func<string, TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        public async Task<TResult> DeleteAuthorizationAsync<TResult>(string userIdentification,
            Func<TResult> onSuccess,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure)
        {
            var accountRef = userIdentification
                .MD5HashGuid()
                .AsRef<Account>();
            return await accountRef.StorageDeleteAsync(
                discard => onSuccess(),
                onNotFound: () => onFailure("Account does not exists"));
        }

        #endregion

    }

    public class CredentialProviderAttribute : Attribute, IProvideLoginProvider
    {
        public Task<TResult> ProvideLoginProviderAsync<TResult>(
            Func<IProvideLogin, TResult> onLoaded,
            Func<string, TResult> onNotAvailable)
        {
            return AppSettings.ClientKey.ConfigurationGuid(
                (clientKey) =>
                {
                    return AppSettings.ClientSecret.ConfigurationString(
                        (clientSecret) =>
                        {
                            var provider = new CredentialProvider(clientKey, clientSecret);
                            return onLoaded(provider);
                        },
                        onNotAvailable);
                },
                onNotAvailable).AsTask();
        }
    }
}

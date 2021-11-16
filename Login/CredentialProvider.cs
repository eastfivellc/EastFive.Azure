using EastFive.Api.Azure;
using EastFive.Azure.Auth;
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
    public class CredentialProvider : IProvideLogin, Auth.IProvideSession
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
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            //if (!tokenParameters.ContainsKey(CredentialProvider.tokenKey))
            //    return onInvalidCredentials($"Parameter with name [{CredentialProvider.tokenKey}] was not provided");
            //var accessToken = tokenParameters[CredentialProvider.tokenKey];

            if (!tokenParameters.ContainsKey(CredentialProvider.stateKey))
                return onInvalidCredentials($"Parameter with name [{CredentialProvider.stateKey}] was not provided");
            var stateLookup = tokenParameters[CredentialProvider.stateKey];

            if (!tokenParameters.ContainsKey(CredentialProvider.referrerKey))
                return onInvalidCredentials($"Parameter with name [{CredentialProvider.referrerKey}] was not provided");
            var referrerString = tokenParameters[CredentialProvider.referrerKey];

            if (!Uri.TryCreate(referrerString, UriKind.Absolute, out Uri referrer))
                return onInvalidCredentials($"Referrer:`{referrerString}` is not a valid absolute url.");

            try
            {
                using (var client = new HttpClient())
                {
                    //var validationUrl = new Uri(referrer, $"/api/Authentication/{stateLookup}");
                    //client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    //var result = await client.GetAsync(validationUrl);
                    //var authenticationContent = await result.Content.ReadAsStringAsync();
                    //var authentication = Newtonsoft.Json.JsonConvert.DeserializeObject<Authentication>(authenticationContent);

                    return await Guid.Parse(stateLookup).AsRef<Authentication>().StorageGetAsync(
                        authentication =>
                        {
                            var subject = authentication.userIdentification;
                            var stateString = authentication.state;
                            var extraParams = new Dictionary<string, string>
                            {
                                //{  CredentialProvider.accessTokenKey, apiAccessToken },
                                //{  CredentialProvider.refreshTokenKey, apiRefreshToken },
                                {  CredentialProvider.accountIdKey, subject },
                                {  CredentialProvider.stateKey, stateString },
                                //{  CredentialProvider.tokenKey, accessToken },
                            };
                            if (!authentication.authenticated.HasValue)
                                return onUnauthenticated(default(Guid?), extraParams);

                            var state = default(Guid?);
                            if (Guid.TryParse(stateString, out Guid parsedState))
                                state = parsedState;
                            return onSuccess(subject, state, default(Guid?), extraParams);
                        },
                        () =>
                        {
                            return onInvalidCredentials($"`stateLookup` is not a valid {typeof(Authentication).FullName}");
                        });
                }
            }
            catch (Exception ex)
            {
                return onCouldNotConnect(ex.Message);
            }
        }

        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> tokenParameters,
            Func<string, Guid?, Guid?, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (!tokenParameters.ContainsKey(CredentialProvider.accountIdKey))
                return onFailure($"Parameter with name [{CredentialProvider.accountIdKey}] was not provided");
            var subject = tokenParameters[CredentialProvider.accountIdKey];

            var state = default(Guid?);
            if (tokenParameters.ContainsKey(CredentialProvider.stateKey))
                if (Guid.TryParse(tokenParameters[CredentialProvider.stateKey], out Guid parsedState))
                    state = parsedState;

            return onSuccess(subject, state, default(Guid?));
        }

        #endregion

        #region IProvideLogin

        public Type CallbackController => typeof(Redirection);

        public Uri GetLoginUrl(Guid state, Uri responseControllerLocation, Func<Type, Uri> controllerToLocation)
        {
            var baseLoginUrl = controllerToLocation(typeof(Authentication));
            var validation = state.ToByteArray().Concat(this.clientSecret.FromBase64String()).ToArray().MD5HashGuid();
            var url = new Uri(baseLoginUrl, 
                $"?{Authentication.StatePropertyName}={state}" + 
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

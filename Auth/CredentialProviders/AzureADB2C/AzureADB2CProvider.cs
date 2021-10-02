using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.IdentityModel.Tokens.Jwt;

using Microsoft.IdentityModel.Tokens;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Serialization;
using EastFive.Azure.Auth;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using EastFive.AzureADB2C;
using EastFive.Api;

namespace EastFive.Azure.Auth.CredentialProviders
{
    [IntegrationName(IntegrationName)]
    public class AzureADB2CProvider : IProvideLogin, IProvideLoginManagement
    {
        private static readonly string clientId;
        private static readonly Lazy<Task<Endpoints>> endpointsAsync;
        private readonly Endpoints endpoints;

        private struct Endpoints
        {
            public string loginEndpoint;
            public string signupEndpoint;
            public string logoutEndpoint;
            public TokenValidationParameters validationParameters;
        }

        static AzureADB2CProvider()
        {
            clientId = EastFive.Azure.AppSettings.AzureADB2C.ApplicationId.ConfigurationString(
                (value) => value,
                (why) => throw new Exception(why));

            endpointsAsync = new Lazy<Task<Endpoints>>(
                () =>
                {
                    return EastFive.Azure.AppSettings.AzureADB2C.Tenant.ConfigurationString(
                        (tenant) =>
                        {
                            return EastFive.Azure.AppSettings.AzureADB2C.SigninFlow.ConfigurationString(
                                (signInFlow) =>
                                {
                                    return EastFive.Azure.AppSettings.AzureADB2C.SignupFlow.ConfigurationString(
                                        (signUpFlow) =>
                                        {
                                            return EastFive.Azure.AppSettings.AzureADB2C.Audience.ConfigurationString(
                                                (audience) =>
                                                {
                                                    return EastFive.AzureADB2C.Libary.InitializeAsync(tenant,
                                                            signUpFlow, signInFlow,
                                                            audience,
                                                        (signupEndpoint, signinEndpoint, logoutEndpoint, validationParams) =>
                                                        {
                                                            return EastFive.AzureADB2C.B2CGraphClient.LoadFromConfig(
                                                                client =>
                                                                {
                                                                    return new Endpoints
                                                                    {
                                                                        signupEndpoint = signupEndpoint,
                                                                        loginEndpoint = signinEndpoint,
                                                                        logoutEndpoint = logoutEndpoint,
                                                                        validationParameters = validationParams,
                                                                    };
                                                                },
                                                                (why) => throw new Exception(why));
                                                        },
                                                        (why) => throw new Exception(why));
                                                },
                                                (why) => throw new Exception(why));
                                        },
                                        (why) => throw new Exception(why));
                                },
                                (why) => throw new Exception(why));
                        },
                        (why) => throw new Exception(why));
                });
        }

        public const string IntegrationName = "Password";
        public string Method => IntegrationName;

        #region Setup

        internal const string StateKey = "state";
        internal const string IdTokenKey = "id_token";
        
        private static EastFive.AzureADB2C.B2CGraphClient client
        {
            get
            {
                return EastFive.AzureADB2C.B2CGraphClient.LoadFromConfig(
                    client => client,
                    (why) =>
                    {
                        throw new ConfigurationException(why, typeof(string), why);
                    });
            }
        }

        private AzureADB2CProvider(Endpoints endpoints)
        {
            this.endpoints = endpoints;
        }

        [IntegrationName(IntegrationName)]
        public static async Task<TResult> InitializeAsync<TResult>(
            Func<IProvideAuthorization, TResult> onProvideAuthorization,
            Func<TResult> onProvideNothing,
            Func<string, TResult> onFailure)
        {
            var endpoints = await endpointsAsync.Value;
            return onProvideAuthorization(new AzureADB2CProvider(endpoints));
        }

        #endregion

        #region IProvideAuthorization
        
        public async Task<TResult> RedeemTokenAsync<TResult>(IDictionary<string, string> extraParams,
            Func<string, Guid?, Guid?, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onUnauthenticated,
            Func<string, TResult> onInvalidCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onUnspecifiedConfiguration,
            Func<string, TResult> onFailure)
        {
            if (!extraParams.ContainsKey(AzureADB2CProvider.StateKey))
                return onFailure($"{AzureADB2CProvider.StateKey} not in auth response");
            var stateParam = extraParams[AzureADB2CProvider.StateKey];
            if (!Guid.TryParse(stateParam, out Guid stateId))
                return onFailure($"Invalid state parameter [{stateParam}] is not a GUID");

            if (!extraParams.ContainsKey(AzureADB2CProvider.IdTokenKey))
                return onUnauthenticated(stateId, extraParams);
            var token = extraParams[AzureADB2CProvider.IdTokenKey];

            return await this.ValidateToken(token,
                (claims) =>
                {
                    return Web.Configuration.Settings.GetString(
                            EastFive.Security.SessionServer.Configuration.AppSettings.LoginIdClaimType,
                        (claimType) =>
                        {
                            var authClaims = claims.Claims
                                        .Where(claim => claim.Type.CompareTo(claimType) == 0)
                                        .ToArray();
                            if (authClaims.Length == 0)
                                return onFailure($"Token does not contain claim for [{claimType}] which is necessary to operate with this system");

                            string subject = authClaims[0].Value;
                            var authId = default(Guid?);
                            if (Guid.TryParse(subject, out Guid authIdGuid))
                                authId = authIdGuid;

                            // TODO: Populate extraParams from claims
                            return onSuccess(subject, stateId, authId, 
                                extraParams
                                    .Concat(
                                        claims.Claims.Select(claim => claim.Type.PairWithValue(claim.Value)))
                                    .ToDictionary());
                        },
                        onUnspecifiedConfiguration);
                },
                onInvalidCredentials).AsTask();
        }
        
        private TResult ValidateToken<TResult>(string idToken,
            Func<ClaimsPrincipal, TResult> onSuccess,
            Func<string, TResult> onFailed)
        {
            if (default(TokenValidationParameters) == endpoints.validationParameters)
                return onFailed("AADB2C Provider not initialized");
            var handler = new JwtSecurityTokenHandler();
            try
            {
                var claims = handler.ValidateToken(idToken, endpoints.validationParameters, out SecurityToken validatedToken);
                //var claims = new ClaimsPrincipal();
                return onSuccess(claims);
            }
            catch (Microsoft.IdentityModel.Tokens.SecurityTokenException ex)
            {
                return onFailed(ex.Message);
            }
        }


        public TResult ParseCredentailParameters<TResult>(IDictionary<string, string> responseParams,
            Func<string, Guid?, Guid?, TResult> onSuccess, 
            Func<string, TResult> onFailure)
        {
            if (!responseParams.ContainsKey(AzureADB2CProvider.StateKey))
                return onFailure($"{AzureADB2CProvider.StateKey} not in auth response");
            var stateParam = responseParams[AzureADB2CProvider.StateKey];
            if (!Guid.TryParse(stateParam, out Guid stateId))
                return onFailure($"Invalid state parameter [{stateParam}] is not a GUID");

            return Web.Configuration.Settings.GetString(
                    EastFive.Security.SessionServer.Configuration.AppSettings.LoginIdClaimType,
                (claimType) =>
                {
                    var authClaims = responseParams
                        .Where(claim => claim.Key.CompareTo(claimType) == 0)
                        .ToArray();

                    if (authClaims.Length == 0)
                        return onFailure($"Token does not contain claim for [{claimType}] which is necessary to operate with this system");

                    string subject = authClaims[0].Value;
                    var authId = default(Guid?);
                    if (Guid.TryParse(subject, out Guid authIdGuid))
                        authId = authIdGuid;

                    return onSuccess(subject, stateId, authId);
                },
                onFailure);
        }

        #endregion

        #region IProvideLogin

        public Guid Id =>  System.Text.Encoding.UTF8.GetBytes(Method).MD5HashGuid();

        public Type CallbackController => typeof(EastFive.Azure.Auth.OpenIdResponse);

        public Uri GetLoginUrl(Guid state, Uri responseLocation, Func<Type, Uri> controllerToLocation)
        {
            return GetUrl(endpoints.loginEndpoint, state, responseLocation);
        }

        public Uri GetLogoutUrl(Guid state, Uri responseLocation, Func<Type, Uri> controllerToLocation)
        {
            return GetUrl(endpoints.logoutEndpoint, state, responseLocation);
        }

        public Uri GetSignupUrl(Guid state, Uri callbackLocation, Func<Type, Uri> controllerToLocation)
        {
            return GetUrl(endpoints.signupEndpoint, state, callbackLocation);
        }
        
        private Uri GetUrl(string longurl, Guid stateGuid,
            Uri callbackLocation)
        {
            var uriBuilder = new UriBuilder(longurl);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["client_id"] = clientId;
            query["response_type"] = AzureADB2CProvider.IdTokenKey;
            query["redirect_uri"] = callbackLocation.AbsoluteUri;
            query["response_mode"] = "form_post";
            query["scope"] = "openid";
            
            query[StateKey] = stateGuid.ToString("N"); //  redirect_uri.Base64(System.Text.Encoding.ASCII);

            query["nonce"] = Guid.NewGuid().ToString("N");
            uriBuilder.Query = query.ToString();
            var redirect = uriBuilder.Uri; // .ToString();
            return redirect;
        }
        
        #endregion

        #region IProvideLoginManagement

        public async Task<TResult> CreateAuthorizationAsync<TResult>(string displayName, 
                string userId, bool isEmail, string secret, bool forceChange,
            Func<Guid, TResult> onSuccess,
            Func<Guid, TResult> usernameAlreadyInUse,
            Func<TResult> onPasswordInsufficent,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported, 
            Func<string, TResult> onFailure)
        {
            return await client.CreateUser(displayName,
                userId, isEmail, secret, forceChange,
                onSuccess,
                (loginId) => usernameAlreadyInUse(loginId),
                onPasswordInsufficent,
                onFailure);
        }
        
        public async Task<TResult> GetAuthorizationAsync<TResult>(Guid loginId, 
            Func<LoginInfo, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<string, TResult> onServiceNotAvailable, 
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure)
        {
            return await client.GetUserByObjectId(loginId.ToString(),
                (displayName, signinName, isEmail, otherMail, forceChange, accountEnabled) => onSuccess(new LoginInfo
                {
                    loginId = loginId,
                    userName = signinName,
                    isEmail = isEmail,
                    otherMail = otherMail,
                    forceChange = forceChange,
                    accountEnabled = accountEnabled,
                    displayName = displayName,
                    forceChangePassword = forceChange
                }),
                onNotFound,
                onFailure);
        }

        public async Task<TResult> GetAllAuthorizationsAsync<TResult>(
            Func<LoginInfo[], TResult> onFound,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported, 
            Func<string, TResult> onFailure)
        {
            var total = new LoginInfo[] { };
            return await client.GetAllUsersAsync(
                tuples =>
                {
                    total = total.Concat(tuples
                        .Select(tuple =>
                            new LoginInfo
                            {
                                loginId = tuple.Item1,
                                userName = tuple.Item3,
                                isEmail = tuple.Item4,
                                otherMail = tuple.Item5,
                                forceChange = tuple.Item6,
                                accountEnabled = tuple.Item7,
                                displayName = tuple.Item2,
                                forceChangePassword = tuple.Item6
                            }))
                        .ToArray();
                },
                () => onFound(total),
                (why) => onFailure(why));
        }

        public async Task<TResult> UpdateAuthorizationAsync<TResult>(Guid loginId,
                string password, bool forceChange,
            Func<TResult> onSuccess,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported, 
            Func<string, TResult> onFailure)
        {
            return await client.UpdateUserPasswordAsync(loginId.ToString(), password, forceChange,
                ok => onSuccess(),
                onFailure);
        }

        public async Task<TResult> UpdateEmailAsync<TResult>(Guid loginId,
                string email,
            Func<TResult> onSuccess,
            Func<string, TResult> onServiceNotAvailable,
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure)
        {
            return await client.UpdateUserEmailAsync(loginId.ToString(), email,
                ok => onSuccess(),
                onFailure);
        }

        public async Task<TResult> DeleteAuthorizationAsync<TResult>(Guid loginId,
            Func<TResult> onSuccess,
            Func<string, TResult> onServiceNotAvailable, 
            Func<TResult> onServiceNotSupported,
            Func<string, TResult> onFailure)
        {
            var result = await client.DeleteUser(loginId.ToString());
            return onSuccess();
        }

        public Task<TResult> UserParametersAsync<TResult>(Guid actorId, System.Security.Claims.Claim[] claims, IDictionary<string, string> extraParams,
            Func<IDictionary<string, string>, IDictionary<string, Type>, IDictionary<string, string>, TResult> onSuccess)
        {
            return onSuccess(
                new Dictionary<string, string>(),
                new Dictionary<string, Type>(), 
                new Dictionary<string, string>()).AsTask();
        }

        #endregion
    }

}

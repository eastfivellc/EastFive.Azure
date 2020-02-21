using System;
using System.Linq;
using System.Threading.Tasks;
using System.Configuration;
using System.Security.Claims;
using System.Collections.Generic;

using BlackBarLabs.Collections.Generic;
using BlackBarLabs.Extensions;
using BlackBarLabs;
using EastFive.Collections.Generic;
using Microsoft.ApplicationInsights;
using EastFive.Linq;
using EastFive.Api.Azure;
using EastFive.Extensions;
using EastFive.Azure.Monitoring;

namespace EastFive.Security.SessionServer
{
    public struct Session
    {
        public Guid id;
        public string name;
        public string method;
        public string token;
        public Uri loginUrl;
        public Uri logoutUrl;
        public Uri redirectUrl;
        public Uri redirectLogoutUrl;
        public Guid? authorizationId;
        public IDictionary<string, string> extraParams;
        public IDictionary<string, CustomParameter> userParams;
        public string refreshToken;
        public AuthenticationActions action;
        public IDictionary<string, string> resourceTypes;
    }

    public struct CustomParameter
    {
        public string Value;
        public string Label;
        public Type Type;
        public string Description;
    }

    public class Sessions
    {
        private Context context;
        private Persistence.DataContext dataContext;
        private TelemetryClient telemetry;

        internal Sessions(Context context, Persistence.DataContext dataContext)
        {
            this.dataContext = dataContext;
            this.context = context;

            telemetry = EastFive.Azure.AppSettings.ApplicationInsights.InstrumentationKey.LoadTelemetryClient();
        }
        
        public async Task<TResult> CreateLoginAsync<TResult>(Guid authenticationRequestId,
                string method, Uri redirectUrl, Uri redirectLogoutUrl,
                Func<Type, Uri> controllerToLocation,
            Func<Session, TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<TResult> onCredentialSystemNotAvailable,
            Func<string, TResult> onCredentialSystemNotInitialized,
            Func<string, TResult> onFailure)
        {
            return await Context.GetLoginProvider(method,
                async (provider) =>
                {
                    var callbackLocation = controllerToLocation(provider.CallbackController);
                    var sessionId = SecureGuid.Generate();
                    var methodName = method;
                    var result = await this.dataContext.AuthenticationRequests.CreateAsync(authenticationRequestId,
                            methodName, AuthenticationActions.signin, redirectUrl, redirectLogoutUrl,
                        () => EastFive.Api.Auth.JwtTools.CreateToken(sessionId, callbackLocation, TimeSpan.FromMinutes(30),
                            (token) =>
                            {
                                var session = new Session()
                                {
                                    id = authenticationRequestId,
                                    method = methodName,
                                    name = methodName,
                                    action = AuthenticationActions.signin,
                                    loginUrl = provider.GetLoginUrl(authenticationRequestId, callbackLocation, controllerToLocation),
                                    logoutUrl = provider.GetLogoutUrl(authenticationRequestId, callbackLocation, controllerToLocation),
                                    redirectUrl = redirectUrl,
                                    redirectLogoutUrl = redirectLogoutUrl,
                                    token = token,
                                };
                                return onSuccess(session);
                            },
                            why => onFailure(why),
                            (param, why) => onFailure($"Invalid configuration for {param}:{why}")),
                        onAlreadyExists);
                    return result;
                },
                onCredentialSystemNotAvailable.AsAsyncFunc(),
                onCredentialSystemNotInitialized.AsAsyncFunc());
        }

        internal async Task<TResult> CreateLoginAsync<TResult>(Guid authenticationRequestId, Guid authenticationId,
                string method, Uri callbackLocation, IDictionary<string, string> authParams,
            Func<Session, TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<string, TResult> onFailure)
        {
            return await EastFive.Web.Configuration.Settings.GetUri(
                EastFive.Security.AppSettings.TokenScope,
                async (scope) =>
                {
                    var sessionId = SecureGuid.Generate();
                    var claims = await this.context.Claims.FindByAccountIdAsync(authenticationId,
                        (cs) => cs.Select(c => c.Type.PairWithValue(c.Value)).ToDictionary(),
                        () => new Dictionary<string, string>());
                    return await Sessions.GenerateToken(sessionId, authenticationId, claims,
                                (token) => this.dataContext.AuthenticationRequests.CreateAsync(authenticationRequestId,
                                        method, AuthenticationActions.signin, authenticationId, token, callbackLocation, callbackLocation,
                                    () =>
                                    {
                                        telemetry.TrackEvent("Sessions.CreateLoginAsync - Create Session", authParams);
                                        var session = new Session()
                                        {
                                            id = authenticationRequestId,
                                            method = method,
                                            name = method,
                                            action = AuthenticationActions.signin,
                                            token = token,
                                            extraParams = authParams
                                        };
                                        return onSuccess(session);
                                    },
                                    onAlreadyExists),
                            why => onFailure(why).ToTask());
                },
                onFailure.AsAsyncFunc());
        }

        internal async Task<TResult> GenerateSessionWithClaimsAsync<TResult>(Guid sessionId, Guid authenticationId,
            Func<string, string, TResult> onSuccess,
            Func<string, TResult> onConfigurationFailure)
        {
            return await CreateSessionAsync(sessionId, authenticationId, onSuccess, onConfigurationFailure);
        }

        internal async Task<TResult> CreateSessionAsync<TResult>(Guid sessionId, Guid authenticationId,
            Func<string, string, TResult> onSuccess,
            Func<string, TResult> onConfigurationFailure)
        {
            Func<IDictionary<string, string>, TResult> authenticate =
                (claims) =>
                {
                    var refreshToken = SecureGuid.Generate().ToString("N");
                    var result = GenerateToken(sessionId, authenticationId,
                            claims,
                        (jwtToken) => onSuccess(jwtToken, refreshToken),
                        (why) => onConfigurationFailure(why));
                    return result;
                };
            return await this.context.Claims.FindByAccountIdAsync(authenticationId,
                (claims) => authenticate(claims
                    .Select(claim => new KeyValuePair<string, string>(claim.Type, claim.Value))
                    .ToDictionary()),
                () => authenticate(new Dictionary<string, string>()));
        }
        
        internal async Task<TResult> GetAsync<TResult>(Guid authenticationRequestId, Func<Type, Uri> callbackUrlFunc,
                AzureApplication application,
            Func<Session, TResult> onSuccess,
            Func<string, TResult> onNotFound,
            Func<string, TResult> onFailure)
        {
            return await await this.dataContext.AuthenticationRequests.FindByIdAsync(authenticationRequestId,
                async (authenticationRequestStorage) =>
                {
                    if (authenticationRequestStorage.Deleted.HasValue)
                        return onNotFound("Session was deleted");

                    return await await application.GetLoginProviderAsync(authenticationRequestStorage.method,
                        async (provider) =>
                        {
                            var authenticationRequest = Convert(authenticationRequestStorage);
                            if(authenticationRequest.authorizationId.HasValue)
                            {
                                authenticationRequest = await CreateSessionAsync(authenticationRequest.id, authenticationRequest.authorizationId.Value,
                                    (token, refreshToken) =>
                                    {
                                        authenticationRequest.token = token;
                                        authenticationRequest.refreshToken = refreshToken;
                                        return authenticationRequest;
                                    },
                                    (why) => authenticationRequest);
                            }
                            var callbackUrl = callbackUrlFunc(provider.CallbackController);
                            authenticationRequest.loginUrl = provider.GetLoginUrl(authenticationRequestId, callbackUrl, callbackUrlFunc);
                            authenticationRequest.logoutUrl = provider.GetLogoutUrl(authenticationRequestId, callbackUrl, callbackUrlFunc);
                            return onSuccess(authenticationRequest);
                        },
                        () => onFailure("The credential provider for this request is no longer enabled in this system").ToTask(),
                        (why) => onFailure(why).ToTask());
                },
                () => onNotFound("Session does not exist").ToTask());
        }

        public async Task<TResult> LookupCredentialMappingAsync<TResult>(
                string method, string subject, Guid? loginId, Guid sessionId, 
            IDictionary<string, string> extraParams,
            Func<Guid, string, string, IDictionary<string, string>, TResult> onSuccess,
            Func<TResult> alreadyExists,
            Func<TResult> credentialNotInSystem,
            Func<string, TResult> onConfigurationFailure)
        {
            // Convert authentication unique ID to Actor ID
            var resultLookup = await await dataContext.CredentialMappings.LookupCredentialMappingAsync(method, subject, loginId,
                (actorId) => CreateSessionAsync(sessionId, actorId,
                    (token, refreshToken) => onSuccess(actorId, token, refreshToken, extraParams),
                    onConfigurationFailure),
                () => credentialNotInSystem().ToTask());
            return resultLookup;
        }

        internal async Task<TResult> CreateAsync<TResult>(Guid sessionId, Guid actorId, System.Security.Claims.Claim[] claims,
            Func<string, string, TResult> onSuccess,
            Func<TResult> alreadyExists,
            Func<string, TResult> onConfigurationFailure)
        {
            var refreshToken = EastFive.Security.SecureGuid.Generate().ToString("N");
            var resultFound = await this.dataContext.Sessions.CreateAsync(sessionId, refreshToken, actorId,
                () =>
                {
                    return GenerateToken(sessionId, actorId, claims
                        .Select(claim => new KeyValuePair<string, string>(claim.Type, claim.Value))
                        .ToDictionary(),
                        (jwtToken) => onSuccess(jwtToken, refreshToken),
                        (why) => onConfigurationFailure(why));
                },
                () => alreadyExists());
            return resultFound;
        }
        
        public async Task<TResult> UpdateWithAuthenticationAsync<TResult>(
                Guid sessionId,
                AzureApplication application, string method,
                IDictionary<string, string> extraParams,
            Func<Guid, Guid, string, string, AuthenticationActions, IDictionary<string, string>, Uri, TResult> onLogin,
            Func<Uri, string, IDictionary<string, string>, TResult> onLogout,
            Func<string, TResult> onInvalidToken,
            Func<TResult> lookupCredentialNotFound,
            Func<string, TResult> systemOffline,
            Func<string, TResult> onNotConfigured,
            Func<string, TResult> onFailure)
        {
            return await await application.GetAuthorizationProviderAsync(method,
                async (provider) =>
                {
                    return await await provider.RedeemTokenAsync(extraParams,
                        async (subject, stateId, loginId, extraParamsWithRedemptionParams) =>
                        {
                            if (stateId.HasValue)
                            {
                                if (stateId.Value != sessionId)
                                    return onInvalidToken("The authorization flow did not match this resource");

                                return await AuthenticateStateAsync(stateId.Value, loginId, method, subject, extraParamsWithRedemptionParams,
                                    onLogin,
                                    onLogout,
                                    onInvalidToken,
                                    onNotConfigured,
                                    onFailure);
                            }

                            return await await dataContext.CredentialMappings.LookupCredentialMappingAsync(method, subject, loginId,
                                (authenticationId) =>
                                {
                                    return context.Sessions.CreateSessionAsync(sessionId, authenticationId,
                                        (token, refreshToken) => onLogin(sessionId, authenticationId,
                                            token, refreshToken, AuthenticationActions.signin, extraParamsWithRedemptionParams,
                                            default(Uri)), // No redirect URL is available since an AuthorizationRequest was not provided
                                        onNotConfigured);
                                },
                                () => onInvalidToken($"The token does not map to a user in this system.").ToTask());
                        },
                        async (stateId, extraParamsWithRedemptionParams) =>
                        {
                            if (!stateId.HasValue)
                                onLogout(default(Uri), "State id missing.", extraParamsWithRedemptionParams);

                            return await dataContext.AuthenticationRequests.FindByIdAsync(stateId.Value,
                                (authRequest) => onLogout(authRequest.redirectLogout, $"Not authenticated. [{stateId.Value}]", extraParamsWithRedemptionParams),
                                () => onLogout(default(Uri), $"Authentication request not found. [{stateId.Value}]", extraParamsWithRedemptionParams));
                        },
                        onInvalidToken.AsAsyncFunc(),
                        systemOffline.AsAsyncFunc(),
                        onNotConfigured.AsAsyncFunc(),
                        onFailure.AsAsyncFunc());
                },
                () => systemOffline($"The requested credential system is not enabled for this deployment. [{method}]").ToTask(),
                (why) => onNotConfigured(why).ToTask());
        }

        public delegate Task<TResult> LookupCredentialNotFoundDelegate<TResult>(string subject, IProvideAuthorization authorizationProvider, 
            IDictionary<string, string> parameters,
            Func<
                Guid, // AuthorizationId
                Func<Guid, string, string, AuthenticationActions, Uri, Task<TResult>>, // On mapping created created
                Func<string, TResult>, // mapping creation failed
                Task<TResult>> createLookupCallback);

        public async Task<TResult> CreateOrUpdateWithAuthenticationAsync<TResult>(
                AzureApplication application, string method,
                IDictionary<string, string> extraParams,
            Func<Guid, Guid, string, string, AuthenticationActions, IProvideAuthorization, IDictionary<string, string>, Uri, TResult> onLogin,
            Func<Uri, string, IProvideAuthorization, IDictionary<string, string>, TResult> onLogout,
            LookupCredentialNotFoundDelegate<TResult> lookupCredentialNotFound,
            Func<string, TResult> onInvalidToken,
            Func<string, TResult> systemOffline,
            Func<string, TResult> onNotConfigured,
            Func<string, TResult> onFailure)
        {
            return await await application.GetAuthorizationProviderAsync(method,
                async (provider) =>
                {
                    return await await provider.RedeemTokenAsync(extraParams,
                        (subject, stateId, loginId, extraParamsWithRedemptionParams) =>
                            TokenRedeemedAsync(method, provider,
                                subject, stateId, loginId, extraParamsWithRedemptionParams,
                                onLogin,
                                onLogout,
                                lookupCredentialNotFound,
                                onInvalidToken,
                                onNotConfigured,
                                onFailure,
                                this.telemetry),
                        async (stateId, extraParamsWithRedemptionParams) =>
                        {
                            telemetry.TrackEvent("Sessions.CreateOrUpdateWithAuthenticationAsync:  Not Authenticated");
                            if (!stateId.HasValue)
                                onLogout(default(Uri), "State id missing.", provider, extraParamsWithRedemptionParams);

                            return await dataContext.AuthenticationRequests.FindByIdAsync(stateId.Value,
                                (authRequest) => onLogout(authRequest.redirectLogout, $"Not authenticated. [{stateId.Value}]", provider, extraParamsWithRedemptionParams),
                                () => onLogout(default(Uri), $"Authentication request not found. [{stateId.Value}]", provider, extraParamsWithRedemptionParams));
                        },
                        onInvalidToken.AsAsyncFunc(),
                        systemOffline.AsAsyncFunc(),
                        onNotConfigured.AsAsyncFunc(),
                        onFailure.AsAsyncFunc());
                },
                () => systemOffline($"The requested credential system is not enabled for this deployment. [{method}]").ToTask(),
                (why) => onNotConfigured(why).ToTask());
        }
        
        public async Task<TResult> TokenRedeemedAsync<TResult>(string method, IProvideAuthorization provider,
                string subject, Guid? stateId, Guid? loginId, IDictionary<string, string> extraParamsWithRedemptionParams,
            Func<Guid, Guid, string, string, AuthenticationActions, IProvideAuthorization, IDictionary<string, string>, Uri, TResult> onLogin,
            Func<Uri, string, IProvideAuthorization, IDictionary<string, string>, TResult> onLogout,
            LookupCredentialNotFoundDelegate<TResult> lookupCredentialNotFound,
            Func<string, TResult> onInvalidToken,
            Func<string, TResult> onNotConfigured,
            Func<string, TResult> onFailure,
            TelemetryClient telemetry)
        {
            telemetry.TrackEvent("Sessions.CreateOrUpdateWithAuthenticationAsync:  Authenticated", extraParamsWithRedemptionParams);
            // This is the case where the login process started from an existing authentication resource
            if (stateId.HasValue)
            {
                telemetry.TrackEvent($"Sessions.CreateOrUpdateWithAuthenticationAsync:  StateId: {stateId.Value.ToString()}");
                return await AuthenticateStateAsync(stateId.Value, loginId, method, subject, extraParamsWithRedemptionParams,
                    (authorizationId, authenticationId, token, refreshToken, action, extraParamsAuthenticated, redirectUrl) =>
                        onLogin(authorizationId, authenticationId, token, refreshToken, action, provider, extraParamsAuthenticated, redirectUrl),
                    (redirectUrl, reason, extraParamsAuthenticated) => onLogout(redirectUrl, reason, provider, extraParamsAuthenticated),
                    onInvalidToken,
                    onNotConfigured,
                    onFailure);
            }
            // This is the case where the login process started from an external system
            telemetry.TrackEvent("StateId not found.  Starting external system login flow.");
            return await await dataContext.CredentialMappings.LookupCredentialMappingAsync(method, subject, loginId,
                (authenticationId) =>
                {
                    telemetry.TrackEvent($"Sessions.CreateOrUpdateWithAuthenticationAsync:  Called from external login system.  AuthenticationId: {authenticationId.ToString()}");
                    var authorizationId = Guid.NewGuid();
                    return this.CreateLoginAsync(authorizationId, authenticationId, method, default(Uri), extraParamsWithRedemptionParams,
                        (session) => onLogin(authorizationId, authenticationId, session.token, session.refreshToken, AuthenticationActions.signin, provider, session.extraParams,
                                default(Uri)),
                        "Guid not unique for creating authentication started from external system".AsFunctionException<TResult>(),
                        onFailure);
                },
                () => lookupCredentialNotFound(subject, provider, extraParamsWithRedemptionParams,
                    async (authenticationId, onCreatedLogin, onFailureToCreateLogin) =>
                    {
                        var sessionId = Guid.NewGuid();
                        var authorizationId = sessionId;
                        return await await dataContext.CredentialMappings.CreateCredentialMappingAsync<Task<TResult>>(
                            sessionId, method, subject, authenticationId,
                            async () =>
                            {
                                return await await this.CreateLoginAsync<Task<TResult>>(authorizationId, authenticationId, method, default(Uri), extraParamsWithRedemptionParams,
                                    (session) => onCreatedLogin(sessionId, session.token, session.refreshToken, AuthenticationActions.signin, session.redirectUrl),
                                    "Guid not unique for creating authentication started from external system".AsFunctionException<Task<TResult>>(),
                                    onFailureToCreateLogin.AsAsyncFunc());
                            },
                            () => "Guid not unique for creating authentication started from external system".AsFunctionException<Task<TResult>>()(),
                            () => onFailureToCreateLogin("Token is already in use.").ToTask());
                    }));
        }

        private async Task<TResult> AuthenticateStateAsync<TResult>(Guid sessionId, Guid? loginId, string method,
                string subject, IDictionary<string, string> extraParams,
            Func<Guid, Guid, string, string, AuthenticationActions, IDictionary<string, string>, Uri, TResult> onLogin,
            Func<Uri, string, IDictionary<string, string>, TResult> onLogout,
            Func<string, TResult> onInvalidToken,
            Func<string, TResult> onNotConfigured,
            Func<string, TResult> onFailure)
        {
            return await this.dataContext.AuthenticationRequests.UpdateAsync(sessionId,
                async (authenticationRequest, saveAuthRequest) =>
                {
                    if (authenticationRequest.Deleted.HasValue)
                        return onLogout(authenticationRequest.redirectLogout, $"Authentication request deleted. [{sessionId}]", extraParams);

                    if (authenticationRequest.method != method)
                        return onInvalidToken($"The credential's authentication method does not match the callback method. [{sessionId}]");

                    if (AuthenticationActions.link == authenticationRequest.action)
                        return await context.Invites.CreateInviteCredentialAsync(sessionId, sessionId,
                                authenticationRequest.authorizationId, method, subject, authenticationRequest.name,
                                extraParams, saveAuthRequest, authenticationRequest.redirect,
                            onLogin,
                            onInvalidToken,
                            onNotConfigured,
                            onFailure);

                    if (AuthenticationActions.access == authenticationRequest.action)
                        return await context.Integrations.UpdateAsync(sessionId,
                                subject, extraParams,
                            (redirect) =>
                            {
                                return onLogin(sessionId, authenticationRequest.authorizationId.Value, string.Empty, string.Empty, AuthenticationActions.access, extraParams, redirect);
                            },
                            () => onFailure($"Authentication request was deleted. [{sessionId}]"),
                            () => onFailure($"No authentication on integration. [{sessionId}]"));

                    if (authenticationRequest.authorizationId.HasValue)
                        return onInvalidToken($"Session's authentication request cannot be re-used. [{sessionId}]");

                    return await await dataContext.CredentialMappings.LookupCredentialMappingAsync(method, subject, loginId,
                        async (authenticationId) =>
                        {
                            return await await this.CreateSessionAsync(sessionId, authenticationId,
                                async (token, refreshToken) =>
                                {
                                    await saveAuthRequest(authenticationId, authenticationRequest.name, token, extraParams);
                                    return onLogin(sessionId, authenticationId,
                                        token, refreshToken, AuthenticationActions.signin, extraParams, authenticationRequest.redirect);
                                },
                                onNotConfigured.AsAsyncFunc());
                        },
                        () => onInvalidToken($"The token does not match a user in this system. [{subject}]").ToTask());
                },
                () => onInvalidToken($"The token does not match an Authentication request. [{sessionId}]"));
        }

        private static TResult GenerateToken<TResult>(Guid sessionId, Guid? actorId, IDictionary<string, string> claims,
            Func<string, TResult> onTokenGenerated,
            Func<string, TResult> onConfigurationIssue)
        {
            var resultExpiration = Web.Configuration.Settings.GetDouble(Configuration.AppSettings.TokenExpirationInMinutes,
                tokenExpirationInMinutes =>
                {
                    return Web.Configuration.Settings.GetString(EastFive.Api.AppSettings.ActorIdClaimType,
                        actorIdClaimType =>
                        {
                            if(actorId.HasValue)
                                claims.AddOrReplace(actorIdClaimType, actorId.ToString());
                            var result = Web.Configuration.Settings.GetUri(AppSettings.TokenScope,
                                (scope) =>
                                {
                                    var jwtToken = EastFive.Api.Auth.JwtTools.CreateToken(
                                        sessionId, scope,
                                        TimeSpan.FromMinutes(tokenExpirationInMinutes),
                                        claims,
                                        (token) => token,
                                        (configName) => configName,
                                        (configName, issue) => configName + ":" + issue,
                                        AppSettings.TokenIssuer,
                                        AppSettings.TokenKey);
                                    return onTokenGenerated(jwtToken);
                                },
                                (why) => onConfigurationIssue(why));
                            return result;
                        },
                        (why) => onConfigurationIssue(why));
                },
                (why) => onConfigurationIssue(why));
            return resultExpiration;
        }

        private static Session Convert(Persistence.AuthenticationRequest authenticationRequestStorage)
        {
            return new Session
            {
                id = authenticationRequestStorage.id,
                method = authenticationRequestStorage.method,
                action = authenticationRequestStorage.action,
                token = authenticationRequestStorage.token,
                authorizationId = authenticationRequestStorage.authorizationId,
                extraParams = authenticationRequestStorage.extraParams,
                redirectUrl = authenticationRequestStorage.redirect,
                redirectLogoutUrl = authenticationRequestStorage.redirectLogout,
            };
        }

    }
}

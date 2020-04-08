using System;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

using EastFive;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Net;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Security.SessionServer;
using EastFive.Security;
using EastFive.Linq.Async;
using EastFive.Security.SessionServer.Persistence.Documents;
using EastFive.Api;
using BlackBarLabs;
using EastFive.Azure.Auth;

namespace EastFive.Azure
{
    public struct Integration
    {
        public string method;
        public Guid integrationId;
        public Guid authorizationId;
        public IDictionary<string, string> parameters;
    }

    public class Integrations
    {
        private Context context;
        private Security.SessionServer.Persistence.DataContext dataContext;

        internal Integrations(Context context, Security.SessionServer.Persistence.DataContext dataContext)
        {
            this.dataContext = dataContext;
            this.context = context;
        }
        
        //public async Task<TResult> CreateLinkAsync<TResult>(Guid integrationId, 
        //        Uri callbackLocation,
        //        string method, Uri redirectUrl,
        //        Guid authenticationId, Guid actorId, System.Security.Claims.Claim[] claims,
        //        Func<Type, Uri> typeToUrl,
        //    Func<Session, TResult> onSuccess,
        //    Func<TResult> onAlreadyExists,
        //    Func<string, TResult> onUnauthorized,
        //    Func<TResult> onCredentialSystemNotAvailable,
        //    Func<string, TResult> onCredentialSystemNotInitialized,
        //    Func<string, TResult> onFailure)
        //{
        //    if (!await Library.configurationManager.CanAdministerCredentialAsync(authenticationId, actorId, claims))
        //        return onUnauthorized($"Provided token does not permit access to link {authenticationId} to a login");
        //    return await Context.GetLoginProvider<Task<TResult>>(method,
        //        async (provider) =>
        //        {
        //            var sessionId = SecureGuid.Generate();
        //            return await EastFive.Api.Auth.JwtTools.CreateToken<Task<TResult>>(sessionId, callbackLocation, TimeSpan.FromMinutes(30),
        //                async (token) => await await this.dataContext.AuthenticationRequests.CreateAsync<Task<TResult>>(integrationId,
        //                        method, AuthenticationActions.access, authenticationId, token, redirectUrl, redirectUrl,
        //                    () => dataContext.Integrations.CreateUnauthenticatedAsync(integrationId, authenticationId, method,
        //                        () => onSuccess(
        //                            new Session()
        //                            {
        //                                id = integrationId,
        //                                //method = method,
        //                                name = method.ToString(),
        //                                action = AuthenticationActions.access,
        //                                loginUrl = provider.GetLoginUrl(integrationId, callbackLocation, typeToUrl),
        //                                logoutUrl = provider.GetLogoutUrl(integrationId, callbackLocation, typeToUrl),
        //                                redirectUrl = redirectUrl,
        //                                authorizationId = authenticationId,
        //                                token = token,
        //                            }),
        //                        onAlreadyExists),
        //                onAlreadyExists.AsAsyncFunc()),
        //                why => onFailure(why).AsTask(),
        //                (param, why) => onFailure($"Invalid configuration for {param}:{why}").AsTask());
        //        },
        //        onCredentialSystemNotAvailable.AsAsyncFunc(),
        //        onCredentialSystemNotInitialized.AsAsyncFunc());
        //}

        public async Task<TResult> CreateOrUpdateParamsByActorAsync<TResult>(Guid actorId, string method,
            Func<
                Guid,
                IDictionary<string, string>,
                Func<IDictionary<string, string>, Task>,
                Task<TResult>> onFound,
            Func<
                Func<IDictionary<string, string>, Task<Guid>>, Task<TResult>> onNotFound)
        {
            return await this.dataContext.Integrations.FindUpdatableAsync(actorId, method,
                onFound,
                (createAsync) =>
                {
                    return onNotFound(
                        async (parameters) =>
                        {
                            var integrationId = Guid.NewGuid();
                            return await await this.dataContext.AuthenticationRequests.CreateAsync(integrationId, method, AuthenticationActions.link, actorId,
                                string.Empty, default(Uri), default(Uri),
                                async () =>
                                {
                                    await createAsync(integrationId, parameters);
                                    return integrationId;
                                },
                                "Guid not unique".AsFunctionException<Task<Guid>>());
                        });
                });
        }

        //[Obsolete("Use method string instead.")]
        //public async Task<TResult> CreateOrUpdateAuthenticatedIntegrationAsync<TResult>(Guid actorId, Api.Azure.Credentials.CredentialValidationMethodTypes method,
        //   Func<
        //       Guid?,
        //       IDictionary<string, string>,
        //       Func<IDictionary<string, string>, Task<Guid>>,
        //       Task<TResult>> onCreatedOrFound)
        //{
        //    var methodName = Enum.GetName(typeof(Api.Azure.Credentials.CredentialValidationMethodTypes), method);
        //    return await this.dataContext.Integrations.CreateOrUpdateAsync(actorId, methodName,
        //        (integrationIdMaybe, paramsCurrent, updateAsync) =>
        //        {
        //            return onCreatedOrFound(integrationIdMaybe, paramsCurrent, updateAsync);
        //        });
        //}

        //public async Task<TResult> CreateOrUpdateAuthenticatedIntegrationAsync<TResult>(Guid actorId, string method,
        //   Func<
        //       Guid?,
        //       IDictionary<string, string>,
        //       Func<IDictionary<string, string>, Task<Guid>>,
        //       Task<TResult>> onCreatedOrFound)
        //{
        //    return await this.dataContext.Integrations.CreateOrUpdateAsync(actorId, method,
        //        (integrationIdMaybe, paramsCurrent, updateAsync) =>
        //        {
        //            return onCreatedOrFound(integrationIdMaybe, paramsCurrent, updateAsync);
        //        });
        //}

        //public static Task<TResult> UpdateAsync<TResult>(Guid integrationId,
        //    Func<Integration, Func<Integration, Task<string>>, Task<TResult>> onFound,
        //    Func<TResult> onNotFound)
        //{
        //    return AuthenticationRequestDocument.UpdateAsync(integrationId, onFound, onNotFound);
        //}

        //internal async Task<TResult> GetAsync<TResult>(Guid authenticationRequestId, Func<Type, Uri> callbackUrlFunc,
        //    Func<Session, TResult> onSuccess,
        //    Func<TResult> onNotFound,
        //    Func<string, TResult> onFailure)
        //{
        //    return await await this.dataContext.AuthenticationRequests.FindByIdAsync(authenticationRequestId,
        //        async (authenticationRequestStorage) =>
        //        {
        //            return await Context.GetLoginProvider(authenticationRequestStorage.method,
        //                async (provider) =>
        //                {
        //                    if (!(provider is EastFive.Azure.Auth.IProvideIntegration))
        //                        return onFailure("provider is not of type EastFive.Azure.Auth.IProvideIntegration");

        //                    var extraParams = authenticationRequestStorage.extraParams;
        //                    return await await (provider as EastFive.Azure.Auth.IProvideIntegration)
        //                        .UserParametersAsync(authenticationRequestStorage.authorizationId.Value, null, extraParams,
        //                            async (labels, types, descriptions) =>
        //                            {
        //                                var callbackUrl = callbackUrlFunc(provider.CallbackController);
        //                                var loginUrl = provider.GetLoginUrl(authenticationRequestId, callbackUrl, callbackUrlFunc);
        //                                var authenticationRequest = await Convert(authenticationRequestStorage, provider, loginUrl, extraParams, labels, types, descriptions);
        //                                return onSuccess(authenticationRequest);
        //                            });
        //                },
        //                () => onFailure("The credential provider for this request is no longer enabled in this system").AsTask(),
        //                (why) => onFailure(why).AsTask());
        //        },
        //        ()=> onNotFound().AsTask());
        //}

        //public Task<TResult> GetByIdAsync<TResult>(Guid authenticationRequestId,
        //    Func<Guid?, string, TResult> onSuccess,
        //    Func<TResult> onNotFound)
        //{
        //    return this.dataContext.AuthenticationRequests.FindByIdAsync(authenticationRequestId,
        //        (authenticationRequestStorage) => onSuccess(authenticationRequestStorage.authorizationId, authenticationRequestStorage.method),
        //        () => onNotFound());
        //}

        //public async Task<TResult> GetAuthenticatedByIdAsync<TResult>(Guid authenticationRequestId,
        //    Func<Uri,   // redirect 
        //        Integration, 
        //        TResult> onSuccess,
        //    Func<TResult> onNotFound)
        //{
        //    return await this.dataContext.AzureStorageRepository.UpdateAsync<AuthenticationRequestDocument, TResult>(authenticationRequestId,
        //        async (authenticationRequestStorage, saveAsync) =>
        //        {
        //            if (!authenticationRequestStorage.LinkedAuthenticationId.HasValue)
        //                return onNotFound();

        //            if (!Uri.TryCreate(authenticationRequestStorage.RedirectUrl, UriKind.Absolute, out Uri discard))
        //            {
        //                authenticationRequestStorage.RedirectUrl = "https://shield.affirmhealth.com/profile#integrations";
        //                await saveAsync(authenticationRequestStorage);
        //            }

        //            return onSuccess(new Uri(authenticationRequestStorage.RedirectUrl),
        //                new Integration
        //                {
        //                    authorizationId = authenticationRequestStorage.LinkedAuthenticationId.Value,
        //                    integrationId = authenticationRequestId,
        //                    method = authenticationRequestStorage.Method,
        //                    parameters = authenticationRequestStorage.GetExtraParams(),
        //                });
        //        },
        //        () =>
        //        {
        //            return onNotFound();
        //        });
        //}

        public Task<TResult> GetAuthenticatedByIdAsync<TResult>(Guid authenticationRequestId,
           Func<Integration,
                TResult> onSuccess,
           Func<TResult> onNotFound)
        {
            return this.dataContext.AuthenticationRequests.FindByIdAsync(authenticationRequestId,
                (authenticationRequestStorage) =>
                {
                    if (!authenticationRequestStorage.authorizationId.HasValue)
                        return onNotFound();
                    return onSuccess(
                        new Integration
                        {
                            authorizationId = authenticationRequestStorage.authorizationId.Value,
                            integrationId = authenticationRequestId,
                            method = authenticationRequestStorage.method,
                            parameters = authenticationRequestStorage.extraParams,
                        });
                },
                () => onNotFound());
        }

        //public async Task<TResult> GetByActorAsync<TResult>(Guid actorId, Func<Type, Uri> callbackUrlFunc,
        //        Guid actingAs, System.Security.Claims.Claim [] claims,
        //    Func<Session[], TResult> onSuccess,
        //    Func<TResult> onNotFound,
        //    Func<TResult> onUnathorized,
        //    Func<string, TResult> onFailure)
        //{
        //    if (!await Library.configurationManager.CanAdministerCredentialAsync(actorId,
        //        actingAs, claims))
        //        return onUnathorized();

        //    var integrations = await ServiceConfiguration.loginProviders
        //        .Where(ap => ap.Value is EastFive.Azure.Auth.IProvideIntegration)
        //        .Select(
        //            async ap => await await this.dataContext.Integrations.FindAsync<Task<Session?>>(actorId, ap.Key,
        //                async (authenticationRequestId) =>
        //                {
        //                    var provider = ap.Value;
        //                    var integrationProvider = provider as EastFive.Azure.Auth.IProvideIntegration;
        //                    var method = ap.Key;
        //                    return await await this.dataContext.AuthenticationRequests.FindByIdAsync(authenticationRequestId,
        //                        async (authRequest) =>
        //                        {
        //                            return await await integrationProvider.UserParametersAsync(actorId, null, null,
        //                                async (labels, types, descriptions) =>
        //                                {
        //                                    var callbackUrl = callbackUrlFunc(provider.CallbackController);
        //                                    var loginUrl = provider.GetLoginUrl(authenticationRequestId, callbackUrl, callbackUrlFunc);
        //                                    var authenticationRequest = await Convert(authenticationRequestId, ap.Key, authRequest.name, provider, AuthenticationActions.access,
        //                                        default(string), authenticationRequestId, loginUrl, authRequest.redirect, authRequest.extraParams, labels, types, descriptions);
        //                                    return authenticationRequest;
        //                                });
        //                        },
        //                        async () =>
        //                        {
        //                            #region SHIM
        //                            var integrationId = authenticationRequestId;
        //                            return await await this.dataContext.AuthenticationRequests.CreateAsync(integrationId, method, AuthenticationActions.link, default(Uri), default(Uri),
        //                                async () =>
        //                                {
        //                                    return await await integrationProvider.UserParametersAsync(actorId, null, null,
        //                                        async (labels, types, descriptions) =>
        //                                        {
        //                                            var callbackUrl = callbackUrlFunc(provider.CallbackController);
        //                                            var loginUrl = provider.GetLoginUrl(authenticationRequestId, callbackUrl, callbackUrlFunc);
        //                                            var authenticationRequest = await Convert(authenticationRequestId, ap.Key, default(string), provider, AuthenticationActions.access,
        //                                                default(string), authenticationRequestId, loginUrl, default(Uri), default(IDictionary<string, string>), labels, types, descriptions);
        //                                            return authenticationRequest;
        //                                        });
        //                                },
        //                                "Guid not unique".AsFunctionException<Task<Session>>());
        //                            #endregion
        //                        });
                            
        //                },
        //                () => default(Session?).AsTask()))
        //        .WhenAllAsync()
        //        .SelectWhereHasValueAsync()
        //        .ToArrayAsync();
        //    return onSuccess(integrations);
        //}

        //internal async Task<TResult> GetAllAsync<TResult>(
        //        Func<Type, Uri> callbackUrlFunc,
        //        Guid actingAs, System.Security.Claims.Claim[] claims,
        //    Func<Session[], TResult> onSuccess,
        //    Func<TResult> onNotFound,
        //    Func<TResult> onUnathorized,
        //    Func<string, TResult> onFailure)
        //{
        //    var accesses = await this.dataContext.Integrations.FindAllAsync();
        //    var integrations = await accesses
        //        .FlatMap(
        //            async (accessKvp, next, skip) =>
        //            {
        //                var access = accessKvp.Key;
        //                if (!await Library.configurationManager.CanAdministerCredentialAsync(access.authorizationId, actingAs, claims))
        //                    return await skip();
        //                var session = new Session
        //                {
        //                    authorizationId = access.authorizationId,
        //                    extraParams = access.parameters, // TODO: Only if super admin!!
        //                    method = access.method,
        //                    name = access.method,
        //                };
        //                if (!accessKvp.Value.HasValue)
        //                    return await next(session);

        //                var authRequest = accessKvp.Value.Value;
        //                session.id = authRequest.id;
        //                session.action = authRequest.action;
        //                //session.extraParams = authRequest.extraParams;
        //                session.token = authRequest.token;
        //                return await next(session);
        //            },
        //            (IEnumerable<Session> sessions) => sessions.ToArray().AsTask());
        //    return onSuccess(integrations);
        //}


        private struct InvocationResult
        {
            public string why;
            public object obj;
            public bool success;
        }


        public Task<TResult> UpdateAsync<TResult>(Guid authenticationRequestId,
              string token, IDictionary<string, string> updatedUserParameters,
          Func<Uri, TResult> onUpdated,
          Func<TResult> onAutheticationRequestNotFound,
          Func<TResult> onUnauthenticatedAuthenticationRequest)
        {
            return dataContext.AuthenticationRequests.UpdateAsync(authenticationRequestId,
                async (authRequestStorage, saveAsync) =>
                {
                    if (!authRequestStorage.authorizationId.HasValue)
                        return onUnauthenticatedAuthenticationRequest();

                    await saveAsync(authRequestStorage.authorizationId.Value, authRequestStorage.name, token, updatedUserParameters);
                    return onUpdated(authRequestStorage.redirect);
                },
                onAutheticationRequestNotFound);
        }


        //public async Task<TResult> DeleteByIdAsync<TResult>(Guid accessId,
        //        Guid performingActorId, System.Security.Claims.Claim [] claims, IHttpRequest request,
        //    Func<IHttpResponse, TResult> onSuccess, 
        //    Func<TResult> onNotFound,
        //    Func<TResult> onUnathorized)
        //{
        //    return await await this.dataContext.AuthenticationRequests.DeleteByIdAsync(accessId,
        //        async (integration, deleteAsync) =>
        //        {
        //            var integrationDeleted = await Convert(integration, default(IProvideLogin), default(Uri), default(Dictionary<string, string>),
        //                           default(Dictionary<string, string>), default(Dictionary<string, Type>), default(Dictionary<string, string>));
        //            if (!integration.authorizationId.HasValue)
        //            {
                        
        //                return await Library.configurationManager.RemoveIntegrationAsync(integrationDeleted, request,
        //                    async (response) =>
        //                    {
        //                        await deleteAsync();
        //                        return onSuccess(response);
        //                    },
        //                    () => onSuccess(request.CreateResponse(HttpStatusCode.InternalServerError).AddReason("failure")).AsTask());
        //            }
                    
        //            return await dataContext.Integrations.DeleteAsync(integration.authorizationId.Value, integration.method,
        //                async (parames) =>
        //                {
        //                    return await await Library.configurationManager.RemoveIntegrationAsync(integrationDeleted, request,
        //                        async (response) =>
        //                        {
        //                            await deleteAsync();
        //                            return onSuccess(response);
        //                        },
        //                        () => onSuccess(request.CreateResponse(HttpStatusCode.InternalServerError).AddReason("failure")).AsTask());
        //                },
        //                onNotFound.AsAsyncFunc());
        //        },
        //        async () =>
        //        {
        //            var x = await context.GetLoginProviders<Task<bool[]>>(
        //                async (accessProviders) =>
        //                {
        //                    return await accessProviders
        //                        .Select(
        //                            accessProvider =>
        //                            {
        //                                return dataContext.Integrations.DeleteAsync(performingActorId,
        //                                        accessProvider.GetType().GetCustomAttribute<IntegrationNameAttribute>().Name,
        //                                    (parames) => true,
        //                                    () => false);
        //                            })
        //                        .WhenAllAsync();
        //                },
        //                (why) => (new bool[] { }).AsTask());
        //            return onSuccess(request.CreateResponse(HttpStatusCode.NoContent).AddReason("Access ID not found"));
        //        });
        //}

        //private static Task<Session> Convert(
        //    Security.SessionServer.Persistence.AuthenticationRequest authenticationRequest,
        //    IProvideLogin provider,
        //    Uri loginUrl,
        //    IDictionary<string, string> extraParams, 
        //    IDictionary<string, string> labels, 
        //    IDictionary<string, Type> types, 
        //    IDictionary<string, string> descriptions)
        //{
        //    return Convert(authenticationRequest.id, authenticationRequest.method, authenticationRequest.name, provider, authenticationRequest.action, authenticationRequest.token, 
        //        authenticationRequest.authorizationId.Value, loginUrl, authenticationRequest.redirect, extraParams, labels, types, descriptions);
        //}

        //private async static Task<Session> Convert(
        //    Guid authenticationRequestStorageId,
        //    string method,
        //    string name,
        //    IProvideLogin provider,
        //    AuthenticationActions action,
        //    string token,
        //    Guid authorizationId,
        //    Uri loginUrl,
        //    Uri redirect,
        //    IDictionary<string, string> extraParams,
        //    IDictionary<string, string> labels,
        //    IDictionary<string, Type> types,
        //    IDictionary<string, string> descriptions)
        //{
        //    var keys = labels.SelectKeys().Concat(types.SelectKeys()).Concat(descriptions.SelectKeys());
        //    var userParams = keys
        //        .Distinct()
        //        .Select(
        //            key =>
        //            {
        //                var val = default(string);
        //                if (null != extraParams)
        //                    val = extraParams.ContainsKey(key) ? extraParams[key] : default(string);

        //                return (new CustomParameter
        //                {
        //                    Value = val,
        //                    Type = types.ContainsKey(key) ? types[key] : default(Type),
        //                    Label = labels.ContainsKey(key) ? labels[key] : default(string),
        //                    Description = descriptions.ContainsKey(key) ? descriptions[key] : default(string),
        //                }).PairWithKey(key);
        //            })
        //        .ToDictionary();

        //    var resourceTypes = await ServiceConfiguration.IntegrationResourceTypesAsync(authenticationRequestStorageId,
        //        (resourceTypesInner) => resourceTypesInner,
        //        () => new string[] { });

        //    var computedName = !string.IsNullOrWhiteSpace(name) ?
        //        name :
        //        provider is IProvideIntegration integrationProvider ? integrationProvider.GetDefaultName(extraParams) : method;

        //    return new Session
        //    {
        //        id = authenticationRequestStorageId,
        //        method = method,
        //        name = computedName,
        //        action = action,
        //        token = token,
        //        authorizationId = authorizationId,
        //        redirectUrl = redirect,
        //        loginUrl = loginUrl,
        //        userParams = userParams,
        //        resourceTypes = resourceTypes
        //            .Select(resourceType => resourceType.PairWithValue(resourceType))
        //            .ToDictionary(),
        //    };
        //}
    }
}

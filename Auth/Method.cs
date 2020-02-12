using EastFive.Api;
using EastFive.Persistence;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web.Http.Routing;
using EastFive.Linq.Async;
using EastFive.Api.Controllers;
using System.Runtime.Serialization;
using EastFive.Extensions;
using EastFive.Collections.Generic;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using BlackBarLabs.Extensions;
using BlackBarLabs.Api;
using EastFive.Security.SessionServer;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController6(
        Route = "AuthenticationMethod",
        Resource = typeof(Method),
        ContentType = "x-application/auth-authentication-method",
        ContentTypeVersion = "0.1")]
    public struct Method : IReferenceable
    {
        public Guid id => authenticationId.id;

        public const string AuthenticationIdPropertyName = "id";
        [ApiProperty(PropertyName = AuthenticationIdPropertyName)]
        [JsonProperty(PropertyName = AuthenticationIdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<Method> authenticationId;

        public const string NamePropertyName = "name";
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        [Storage(Name = NamePropertyName)]
        public string name;

        public Task<TResult> GetLoginProviderAsync<TResult>(Api.Azure.AzureApplication application,
            Func<string, Security.SessionServer.IProvideLogin, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return GetLoginProviderAsync(this.id, application,
                onFound,
                onNotFound);
        }

        private static Task<TResult> GetLoginProviderAsync<TResult>(Guid authenticationId, Api.Azure.AzureApplication application,
            Func<string, Security.SessionServer.IProvideLogin, TResult> onFound,
            Func<TResult> onNotFound)
        {
            //var debug = application.LoginProviders.ToArrayAsync().Result;
            return application.LoginProviders
                .Where(
                    loginProvider =>
                    {
                        return loginProvider.Value.Id == authenticationId;
                    })
                .FirstAsync(
                    (loginProviderKvp) =>
                    {
                        var loginProviderKey = loginProviderKvp.Key;
                        var loginProvider = loginProviderKvp.Value;
                        return onFound(loginProviderKey, loginProvider);
                    },
                    onNotFound);
        }

        [HttpGet]
        public static Task<HttpResponseMessage> QueryByIdAsync(
                [QueryId]IRef<Method> methodRef,
            Api.Azure.AzureApplication application,
            ContentTypeResponse<Method> onFound,
            NotFoundResponse onNotFound)
        {
            return ById(methodRef, application,
                method => onFound(method),
                () => onNotFound());
        }

        [Obsolete]
        [HttpGet]
        public static Task<HttpResponseMessage> QueryAsync(
            Api.Azure.AzureApplication application,
            MultipartResponseAsync<Method> onContent)
        {
            return onContent(
                application.LoginProviders
                    .Select(
                        (loginProvider) =>
                        {
                            return new Method
                            {
                                authenticationId = loginProvider.Value.Id.AsRef<Method>(),
                                name = loginProvider.Value.Method,
                            };
                        }));
        }

        [HttpGet]
        public static async Task<HttpResponseMessage> QueryByIntegrationAsync(
            [QueryParameter(Name = "integration")]IRef<Integration> integrationRef,
            Api.Azure.AzureApplication application, EastFive.Api.SessionToken security,
            MultipartResponseAsync<Method> onContent,
            UnauthorizedResponse onUnauthorized,
            ReferencedDocumentNotFoundResponse<Integration> onIntegrationNotFound)
        {
            return await await integrationRef.StorageGetAsync(
                async (integration) =>
                {
                    var accountId = integration.accountId;
                    if (!await application.CanAdministerCredentialAsync(accountId, security))
                        return onUnauthorized();

                    var integrationProviders = application.LoginProviders
                        .Where(loginProvider => loginProvider.Value.GetType().IsSubClassOfGeneric(typeof(IProvideIntegration)))
                        .Select(
                            async loginProvider =>
                            {
                                var integrationProvider = loginProvider.Value as IProvideIntegration;
                                var supportsIntegration = await integrationProvider.SupportsIntegrationAsync(accountId);
                                return supportsIntegration.PairWithValue(loginProvider);
                            })
                        .Await()
                        .Where(kvp => kvp.Key)
                        .SelectValues()
                        .Select(
                            (loginProvider) =>
                            {
                                var integrationProvider = loginProvider.Value as IProvideIntegration;
                                return new Method
                                {
                                    authenticationId = new Ref<Method>(loginProvider.Value.Id),
                                    name = integrationProvider.GetDefaultName(new Dictionary<string, string>()),
                                };
                            });
                    return await onContent(integrationProviders);

                },
                () => onIntegrationNotFound().AsTask());
        }

        [HttpGet]
        public static async Task<HttpResponseMessage> QueryByIntegrationAccountAsync(
            [QueryParameter(Name = "integration_account")]Guid accountId,
            Api.Azure.AzureApplication application, EastFive.Api.SessionToken security,
            MultipartResponseAsync<Method> onContent,
            UnauthorizedResponse onUnauthorized)
        {
            if (!await application.CanAdministerCredentialAsync(accountId, security))
                return onUnauthorized();

            var integrationProviders = application.LoginProviders
                .Where(loginProvider => loginProvider.Value.GetType().IsSubClassOfGeneric(typeof(IProvideIntegration)))
                .Select(
                    async loginProvider =>
                    {
                        var integrationProvider = loginProvider.Value as IProvideIntegration;
                        var supportsIntegration = await integrationProvider.SupportsIntegrationAsync(accountId);
                        return supportsIntegration.PairWithValue(loginProvider);
                    })
                .Await()
                .Where(kvp => kvp.Key)
                .SelectValues()
                .Select(
                    (loginProvider) =>
                    {
                        var integrationProvider = loginProvider.Value as IProvideIntegration;
                        return new Method
                        {
                            authenticationId = new Ref<Method>(loginProvider.Value.Id),
                            name = integrationProvider.GetDefaultName(new Dictionary<string,string>()),
                        };
                    });
            return await onContent(integrationProviders);
        }

        [HttpGet]
        public static async Task<HttpResponseMessage> QueryBySessionAsync(
                [QueryParameter(Name = "session")]IRef<Session> sessionRef,
                Api.Azure.AzureApplication application,
            MultipartResponseAsync<Method> onContent,
            ReferencedDocumentNotFoundResponse<Session> onSessionNotFound,
            UnauthorizedResponse onHacked)
        {
            return await await sessionRef.StorageGetAsync(
                session =>
                {
                    var integrationProviders = application.LoginProviders
                        .Where(loginProvider => loginProvider.Value.GetType().IsSubClassOfGeneric(typeof(IProvideSession)))
                        .Select(
                            async loginProvider =>
                            {
                                var supportsIntegration = await (loginProvider.Value as IProvideSession).SupportsSessionAsync(session);
                                return supportsIntegration.PairWithValue(loginProvider);
                            })
                        .Await()
                        .Where(kvp => kvp.Key)
                        .SelectValues()
                        .Select(
                            (loginProvider) =>
                            {
                                return new Method
                                {
                                    authenticationId = new Ref<Method>(loginProvider.Value.Id),
                                    name = loginProvider.Value.Method,
                                };
                            });
                    return onContent(integrationProviders);
                },
                //() => onSessionNotFound().AsTask());
                () => onHacked().AsTask());
        }

        public static Task<TResult> ById<TResult>(IRef<Method> method, Api.Azure.AzureApplication application,
            Func<Method, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return GetLoginProviderAsync(method.id, application,
                (key, loginProvider) =>
                {
                    var authentication = new Method
                    {
                        authenticationId = new Ref<Method>(loginProvider.Id),
                        name = loginProvider.Method,
                    };
                    return onFound(authentication);
                },
                onNotFound);
        }

        public static Task<Method> ByMethodName(string methodName, Api.Azure.AzureApplication application)
        {
            return application.LoginProviders
                .SelectValues()
                .Where(loginProvider => loginProvider.Method == methodName)
                .FirstAsync(
                    (loginProvider) =>
                    {
                        return new Method
                        {
                            authenticationId = new Ref<Method>(loginProvider.Id),
                            name = loginProvider.Method,
                        };
                    },
                    () => throw new Exception($"Login provider `{methodName}` is not enabled."));
        }

        public async Task<TResult> ParseTokenAsync<TResult>(IDictionary<string, string> parameters, 
            Api.Azure.AzureApplication application,
            Func<string, IProvideLogin, TResult> onParsed,
            Func<string, TResult> onFailure)
        {
            var methodName = this.name;
            var matchingLoginProviders = await application.LoginProviders
                .SelectValues()
                .Where(loginProvider => loginProvider.Method == methodName)
                .ToArrayAsync();
            if (!matchingLoginProviders.Any())
                return onFailure("Method does not match any existing authentication.");
            var matchingLoginProvider = matchingLoginProviders.First();

            return matchingLoginProvider.ParseCredentailParameters(parameters,
                (externalId, authorizationIdMaybeDiscard, lookupDiscard) =>
                {
                    return onParsed(externalId, matchingLoginProvider);
                },
                onFailure);
        }

        public async Task<TResult> RedeemTokenAsync<TResult>(
                IDictionary<string, string> parameters,
                Api.Azure.AzureApplication application,
            Func<string, IRefOptional<Authorization>, Security.SessionServer.IProvideLogin, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onLogout,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onFailure)
        {
            var methodName = this.name;
            var matchingLoginProviders = await application.LoginProviders
                .SelectValues()
                .Where(loginProvider => loginProvider.Method == methodName)
                .ToArrayAsync();
            if (!matchingLoginProviders.Any())
                return onFailure("Method does not match any existing authentication.");
            var matchingLoginProvider = matchingLoginProviders.First();

            return await matchingLoginProvider.RedeemTokenAsync(parameters,
                (userKey, authorizationIdMaybe, deprecatedId, updatedParameters) =>
                {
                    var allParameters = updatedParameters
                        .Concat(
                            parameters
                                .Where(param => !updatedParameters.ContainsKey(param.Key)))
                        .ToDictionary();
                    var authorizationRef = authorizationIdMaybe.HasValue ?
                        new RefOptional<Authorization>(authorizationIdMaybe.Value)
                        :
                        new RefOptional<Authorization>();
                    return onSuccess(userKey, authorizationRef,
                        matchingLoginProvider, allParameters);
                },
                (authorizationId, extraParams) => onLogout(authorizationId, extraParams),
                (why) => onFailure(why),
                (why) => onCouldNotConnect(why),
                (why) => onFailure(why),
                (why) => onFailure(why));
        }

        internal Task<Uri> GetLoginUrlAsync(Api.Azure.AzureApplication application,
            UrlHelper urlHelper, Guid authorizationIdSecure)
        {
            var authenticationId = this.id;
            return GetLoginProviderAsync(application,
                (name, loginProvider) =>
                {
                    var redirectionResource = loginProvider.CallbackController;
                    var redirectionLocation = urlHelper.GetLocation(redirectionResource);
                    return loginProvider.GetLoginUrl(authorizationIdSecure, redirectionLocation,
                        type => urlHelper.GetLocation(type));
                },
                () => throw new Exception($"Login provider with id {authenticationId} does not exists."));
        }

        internal Task<Uri> GetLogoutUrlAsync(Api.Azure.AzureApplication application,
            UrlHelper urlHelper, Guid authorizationIdSecure)
        {
            var authenticationId = this.id;
            return GetLoginProviderAsync(application,
                (name, loginProvider) =>
                {
                    var redirectionResource = loginProvider.CallbackController;
                    var redirectionLocation = urlHelper.GetLocation(redirectionResource);
                    return loginProvider.GetLogoutUrl(authorizationIdSecure, redirectionLocation,
                        type => urlHelper.GetLocation(type));
                },
                () => throw new Exception($"Login provider with id {authenticationId} does not exists."));
        }

        public Task<TResult> GetAuthorizationKeyAsync<TResult>(Api.Azure.AzureApplication application,
            IDictionary<string, string> parameters,
            Func<string, TResult> onAuthorizeKey,
            Func<string, TResult> onFailure,
            Func<TResult> loginMethodNoLongerSupported)
        {
            return GetLoginProviderAsync(application,
                (name, loginProvider) => loginProvider.ParseCredentailParameters(parameters,
                    (externalUserKey, authenticationIdMaybe, scopeMaybeDiscard) => onAuthorizeKey(externalUserKey),
                    why => onFailure(why)),
                () => loginMethodNoLongerSupported());
        }
    }
}

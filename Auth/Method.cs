using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;

using Microsoft.AspNetCore.Mvc.Routing;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Collections.Generic;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using BlackBarLabs.Api;
using EastFive.Security.SessionServer;
using EastFive.Api.Meta.Flows;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController(
        Route = "AuthenticationMethod",
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
        [ResourceIdentifier]
        public IRef<Method> authenticationId;

        public const string NamePropertyName = "name";
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        [Storage(Name = NamePropertyName)]
        [ResourceTitle]
        public string name;

        public Task<TResult> GetLoginProviderAsync<TResult>(IAuthApplication application,
            Func<string, IProvideLogin, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return GetLoginProvider(this.id, application,
                onFound,
                onNotFound).AsTask();
        }

        private static TResult GetLoginProvider<TResult>(Guid authenticationId, IAuthApplication application,
            Func<string, IProvideLogin, TResult> onFound,
            Func<TResult> onNotFound)
        {
            //var debug = application.LoginProviders.ToArrayAsync().Result;
            return application.LoginProviders
                .Where(
                    loginProvider =>
                    {
                        return loginProvider.Value.Id == authenticationId;
                    })
                .First(
                    (loginProviderKvp, next) =>
                    {
                        var loginProviderKey = loginProviderKvp.Key;
                        var loginProvider = loginProviderKvp.Value;
                        return onFound(loginProviderKey, loginProvider);
                    },
                    onNotFound);
        }

        [HttpGet]
        public static Task<IHttpResponse> QueryByIdAsync(
                [QueryId]IRef<Method> methodRef,
            IAuthApplication application,
            ContentTypeResponse<Method> onFound,
            NotFoundResponse onNotFound)
        {
            return ById(methodRef, application,
                method => onFound(method),
                () => onNotFound());
        }

        [HttpGet]
        [WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Step = 1.0,
            StepName = "List Methods")]
        [WorkflowStep2(
            FlowName = Workflows.HijackLoginFlow.FlowName,
            Step = 1.0,
            StepName = "List Methods")]
        public static IHttpResponse QueryAsync(
            IAuthApplication application,
            MultipartAcceptArrayResponse<Method> onContent)
        {
            var methods = application.LoginProviders
                    .Select(
                        (loginProvider) =>
                        {
                            return new Method
                            {
                                authenticationId = loginProvider.Value.Id.AsRef<Method>(),
                                name = loginProvider.Value.Method,
                            };
                        });
            return onContent(methods);
        }

        [HttpGet]
        public static async Task<IHttpResponse> QueryByIntegrationAsync(
            [QueryParameter(Name = "integration")]IRef<XIntegration> integrationRef,
            IAuthApplication application, EastFive.Api.SessionToken security,
            MultipartAsyncResponse<Method> onContent,
            UnauthorizedResponse onUnauthorized,
            ReferencedDocumentNotFoundResponse<XIntegration> onIntegrationNotFound)
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
                        .AsyncEnumerable()
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
                    return onContent(integrationProviders);

                },
                () => onIntegrationNotFound().AsTask());
        }

        [HttpGet]
        public static async Task<IHttpResponse> QueryByIntegrationAccountAsync(
            [QueryParameter(Name = "integration_account")]Guid accountId,
            IAuthApplication application, EastFive.Api.SessionToken security,
            MultipartAsyncResponse<Method> onContent,
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
                .AsyncEnumerable()
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
            return onContent(integrationProviders);
        }

        [HttpGet]
        public static Task<IHttpResponse> QueryBySessionAsync(
                [QueryParameter(Name = "session")]IRef<Session> sessionRef,
                IAuthApplication application,
            MultipartAsyncResponse<Method> onContent,
            ReferencedDocumentNotFoundResponse<Session> onSessionNotFound,
            UnauthorizedResponse onHacked)
        {
            return sessionRef.StorageGetAsync(
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
                        .AsyncEnumerable()
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
                () => onHacked());
        }

        public static Task<TResult> ById<TResult>(IRef<Method> method, IAuthApplication application,
            Func<Method, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return GetLoginProvider(method.id, application,
                (key, loginProvider) =>
                {
                    var authentication = new Method
                    {
                        authenticationId = new Ref<Method>(loginProvider.Id),
                        name = loginProvider.Method,
                    };
                    return onFound(authentication);
                },
                onNotFound).AsTask();
        }

        public static Method ByMethodName(string methodName, IAuthApplication application)
        {
            return application.LoginProviders
                .SelectValues()
                .Where(loginProvider => loginProvider.Method == methodName)
                .First<IProvideLogin, Method>(
                    (loginProvider, next) =>
                    {
                        return new Method
                        {
                            authenticationId = new Ref<Method>(loginProvider.Id),
                            name = loginProvider.Method,
                        };
                    },
                    () => throw new Exception($"Login provider `{methodName}` is not enabled."));
        }

        public Task<TResult> ParseTokenAsync<TResult>(IDictionary<string, string> parameters,
            IAuthApplication application,
            Func<string, IProvideLogin, TResult> onParsed,
            Func<string, TResult> onFailure)
        {
            var methodName = this.name;
            var matchingLoginProviders = application.LoginProviders
                .SelectValues()
                .Where(loginProvider => loginProvider.Method == methodName)
                .ToArray();
            if (!matchingLoginProviders.Any())
                return onFailure("Method does not match any existing authentication.").AsTask();
            var matchingLoginProvider = matchingLoginProviders.First();

            return matchingLoginProvider.ParseCredentailParameters(parameters,
                (externalId, authorizationIdMaybeDiscard, lookupDiscard) =>
                {
                    return onParsed(externalId, matchingLoginProvider);
                },
                onFailure).AsTask();
        }

        public async Task<TResult> RedeemTokenAsync<TResult>(
                IDictionary<string, string> parameters,
                IAuthApplication application,
            Func<string, IRefOptional<Authorization>, IProvideLogin, IDictionary<string, string>, TResult> onSuccess,
            Func<Guid?, IDictionary<string, string>, TResult> onLogout,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onFailure)
        {
            var methodName = this.name;
            var matchingLoginProviders = application.LoginProviders
                .SelectValues()
                .Where(loginProvider => loginProvider.Method == methodName)
                .ToArray();
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

        internal Task<Uri> GetLoginUrlAsync(IAuthApplication application,
            IProvideUrl urlHelper, Guid authorizationIdSecure)
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

        internal Task<Uri> GetLogoutUrlAsync(IAuthApplication application,
            IProvideUrl urlHelper, Guid authorizationIdSecure)
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

        public Task<TResult> GetAuthorizationKeyAsync<TResult>(IAuthApplication application,
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

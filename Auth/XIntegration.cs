using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Security;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController(
        Route = "XIntegration",
        ContentType = "x-application/auth-xintegration",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    public struct XIntegration : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => integrationRef.id;

        public const string IntegrationIdPropertyName = "id";
        [ApiProperty(PropertyName = IntegrationIdPropertyName)]
        [JsonProperty(PropertyName = IntegrationIdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 3)]
        public IRef<XIntegration> integrationRef;
        
        [JsonIgnore]
        [Storage]
        public IRef<Method> Method { get; set; }

        public const string MethodNamePropertyName = "method_name";
        [ApiProperty(PropertyName = MethodNamePropertyName)]
        [JsonProperty(PropertyName = MethodNamePropertyName)]
        public string methodName { get; set; }

        public const string AccountPropertyName = "account";
        [ApiProperty(PropertyName = AccountPropertyName)]
        [JsonProperty(PropertyName = AccountPropertyName)]
        [Storage]
        [IdStandardPartitionLookup]
        public Guid accountId { get; set; }
        
        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        [Storage(Name = AuthorizationPropertyName)]
        [StorageConstraintUnique]
        public IRefOptional<Authorization> authorization { get; set; }
        
        #endregion

        #region Http Methods

        [Api.HttpGet]
        public async static Task<IHttpResponse> GetByIdAsync(
                [QueryParameter(Name = IntegrationIdPropertyName)]IRef<XIntegration> integrationRef,
                IAuthApplication application, SessionToken security,
            ContentTypeResponse<XIntegration> onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            if (!security.accountIdMaybe.HasValue)
                return onUnauthorized();
            var accountId = security.accountIdMaybe.Value;

            return await await integrationRef.StorageGetAsync(
                integration =>
                {
                    return Auth.Method.ById(integration.Method,
                                application,
                            method =>
                            {
                                integration.methodName = method.name;
                                return onFound(integration);
                            },
                            () => onNotFound());
                },
                () => onNotFound().AsTask());
        }

        [Api.HttpGet]
        public async static Task<IHttpResponse> GetByAccountAsync(
                [QueryParameter(Name = AccountPropertyName)]Guid accountId,
                IAuthApplication application, SessionToken security,
            MultipartAsyncResponse<XIntegration> onContents,
            UnauthorizedResponse onUnauthorized)
        {
            if (!await application.CanAdministerCredentialAsync(accountId, security))
                return onUnauthorized();

            var integrations = GetIntegrationsByAccount(accountId)
                .Select(
                    kvp =>
                    {
                        var integration = kvp.Key;
                        return Auth.Method.ById(kvp.Value.Method,
                                application,
                            method =>
                            {
                                integration.methodName = method.name;
                                return integration;
                            },
                            () => default(XIntegration?));
                    })
                .Await()
                .SelectWhereHasValue();

            return onContents(integrations);
        }

        [Api.HttpGet]
        public static IHttpResponse GetByMethodAsync(
                [QueryParameter(Name = Authorization.MethodPropertyName)]IRef<Method> methodRef,
                IAuthApplication application, SessionToken security,
            MultipartAsyncResponse<XIntegration> onContents,
            UnauthorizedResponse onUnauthorized)
        {
            if (!security.accountIdMaybe.HasValue)
                return onUnauthorized();
            var accountId = security.accountIdMaybe.Value;

            var integrations = GetIntegrationsByAccount(accountId)
                .Where(integration => integration.Value.Method.id == methodRef.id)
                .Select(
                    kvp =>
                    {
                        var integration = kvp.Key;
                        return Auth.Method.ById(kvp.Value.Method,
                                application,
                            method =>
                            {
                                integration.methodName = method.name;
                                return integration;
                            },
                            () => default(XIntegration?));
                    })
                .Await()
                .SelectWhereHasValue();
            return onContents(integrations);
        }

        [Api.HttpPost]
        public async static Task<IHttpResponse> CreateAsync(
                [Property(Name = AccountPropertyName)]Guid accountId,
                [PropertyOptional(Name = AuthorizationPropertyName)]IRefOptional<Authorization> authorizationRefMaybe,
                [Resource]XIntegration integration,
                IAuthApplication application, EastFive.Api.SessionToken security,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists,
            ForbiddenResponse onForbidden,
            ReferencedDocumentDoesNotExistsResponse<Authorization> onAuthorizationDoesNotExist,
            GeneralConflictResponse onFailure)
        {
            if (!await application.CanAdministerCredentialAsync(accountId, security))
                return onForbidden();

            return await await authorizationRefMaybe.StorageGetAsync(
                async authorization =>
                {
                    if(!await application.ShouldAuthorizeIntegrationAsync(integration, authorization))
                        return onFailure("Authorization is not accessable to this account.");

                    return await CreateWithAuthorization(integration, authorization,
                        () => onCreated(),
                        () => onAlreadyExists(),
                        (why) => onFailure(why));
                },
                async () =>
                {
                    if (authorizationRefMaybe.HasValue)
                        return onAuthorizationDoesNotExist();
                    return await integration.StorageCreateAsync(
                        (discard) => onCreated(),
                        () => onAlreadyExists());
                });
        }

        [HttpDelete]
        public static async Task<IHttpResponse> DeleteAsync(
        [UpdateId(Name = IntegrationIdPropertyName)]IRef<XIntegration> integrationRef,
                IAuthApplication application, EastFive.Api.SessionToken security,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound,
            ForbiddenResponse onForbidden)
        {
            var integrationMaybe = await integrationRef.StorageGetAsync(i => i, () => default(XIntegration?));
            if (!integrationMaybe.HasValue)
                return onNotFound();

            var integration = integrationMaybe.Value;
            if (!await application.CanAdministerCredentialAsync(integration.accountId, security))
                return onForbidden();

            return await DeleteInternalAsync(integrationRef, () => onDeleted(), () => onNotFound());
        }

        [Api.HttpPatch] //(MatchAllBodyParameters = false)]
        public async static Task<IHttpResponse> UpdateAsync(
                [Property(Name = IntegrationIdPropertyName)]IRef<XIntegration> integrationRef,
                [Property(Name = AuthorizationPropertyName)]IRef<Authorization> authorizationRef,
                Api.Azure.AzureApplication application, EastFive.Api.SessionToken security,
            ContentTypeResponse<XIntegration> onUpdated,
            NotFoundResponse onNotFound,
            ForbiddenResponse onForbidden,
            ReferencedDocumentDoesNotExistsResponse<Authorization> onAuthorizationDoesNotExist,
            UnauthorizedResponse onUnauthorized)
        {
            return await integrationRef.StorageUpdateAsync(
                async (integration, saveAsync) =>
                {
                    var accountId = integration.accountId;
                    if (!await application.CanAdministerCredentialAsync(accountId, security))
                        return onUnauthorized();

                    return await await authorizationRef.StorageGetAsync(
                        async authorization =>
                        {
                            if (!await application.ShouldAuthorizeIntegrationAsync(integration, authorization))
                                return onForbidden().AddReason("Authorization is not accessable to this account.");

                            integration.Method = authorization.Method; // method is used in the .mappingId
                            integration.authorization = authorizationRef.Optional();
                            integration.methodName = await Auth.Method.ById(authorization.Method,
                                    application,
                                method => method.name,
                                () => string.Empty);

                            await saveAsync(integration);
                            return onUpdated(integration);
                        },
                        () => onAuthorizationDoesNotExist().AsTask());
                },
                () => onNotFound(),
                onModificationFailures:
                    StorageConstraintUniqueAttribute.ModificationFailure(
                        (XIntegration x) => x.authorization,
                        () =>
                        {
                            // TODO: Check if mapping is to this integration and reply already created.
                            return onForbidden().AddReason("Authorization is already in use.");
                        }).AsArray());

        }
        
        #endregion

        #region Utility Methods

        public static Task<TResult> DeleteInternalAsync<TResult>(
                IRef<XIntegration> integrationRef,
            Func<TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            return integrationRef.StorageDeleteAsync(
                () =>
                {
                    return onDeleted();
                },
                () => onNotFound());
        }

        public static IEnumerableAsync<KeyValuePair<XIntegration, Authorization>> GetIntegrationsByAccount(Guid accountId)
        {
            return accountId
                .StorageGetByIdProperty((XIntegration integration) => integration.accountId)
                .Where(integration => integration.authorization.HasValue)
                .Select(
                    integration =>
                    {
                        return integration.authorization.StorageGetAsync(
                            authorization =>
                            {
                                return authorization.PairWithKey(integration).AsOptional();
                            },
                            () => default);
                    })
                .Await()
                .SelectWhereHasValue();;
        }

        public TResult GetService<T, TResult>(Func<T, TResult> onServiceLocated, Func<TResult> onServiceNotAvailable)
        {
            throw new NotImplementedException();
        }

        private static Dictionary<string, object[]> services = new Dictionary<string, object[]>();

        public static void RegisterService<T>(string integrationMethod, T service)
        {
            services.AddIfMissing(integrationMethod,
                (addValue) =>
                {
                    return addValue(service.AsArray<object>());
                },
                (currentServices, current, alreadyAdded) =>
                {
                    if(!alreadyAdded)
                        current[integrationMethod] = currentServices.Append((object)service).ToArray();
                    return true;
                });
        }

        public static Task<TResult> CreateWithAuthorization<TResult>(
            XIntegration integration, Authorization authorization,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists,
            Func<string, TResult> onFailure)
        {
            integration.Method = authorization.Method; // method is used in the .mappingId
            return integration
                .StorageCreateAsync(
                        discard => onCreated(),
                        () => onAlreadyExists(),
                        onModificationFailures:
                            StorageConstraintUniqueAttribute.ModificationFailure(
                                (XIntegration x) => x.authorization,
                                () =>
                                {
                                    // TODO: Check if mapping is to this integration and reply already created.
                                    return onFailure("Authorization is already mapped to another integration.");
                                }).AsArray());
        }

        public static async Task<TResult> CreateByMethodAndKeyAsync<TResult>(IRef<XIntegration> integrationRef, 
                IRef<Authorization> authorizationRef, IRef<Method> methodRef,
                Guid accountId, IDictionary<string, string> parameters,
            Func<XIntegration, Authorization, TResult> onCreated,
            Func<TResult> onIntegrationAlreadyExists,
            Func<TResult> onAuthorizationAlreadyExists,
            Func<string, TResult> onFailure)
        {
            var authorization = new Authorization
            {
                authorizationRef = authorizationRef,
                parameters = parameters,
                Method = methodRef,
                authorized = true,
                accountIdMaybe = accountId,
            };
            return await await authorization.StorageCreateAsync<Authorization, Task<TResult>>(
                (discardId) =>
                {
                    var integration = new XIntegration
                    {
                        integrationRef = integrationRef,
                        accountId = accountId,
                        authorization = authorizationRef.Optional(),
                        Method = methodRef,
                    };
                    return CreateWithAuthorization(integration, authorization,
                        () => onCreated(integration, authorization),
                        () => onIntegrationAlreadyExists(),
                        (why) => onFailure(why));
                },
                () => onAuthorizationAlreadyExists().AsTask());
        }

        public static Task<TResult> CreateByMethodAndKeyAsync<TResult>(IRef<Method> methodRef,
                Guid accountId, IDictionary<string, string> parameters,
            Func<XIntegration, Authorization, TResult> onCreated,
            Func<string, TResult> onFailure)
        {
            return CreateByMethodAndKeyAsync(
                Guid.NewGuid().AsRef<XIntegration>(),
                SecureGuid.Generate().AsRef<Authorization>(),
                methodRef,
                accountId,
                parameters,
                onCreated,
                () => throw new Exception("Guid not unique"),
                () => throw new Exception("Guid not unique"),
                onFailure);
        }                

        public static IEnumerableAsync<KeyValuePair<XIntegration, Authorization>> GetParametersByAccountId(
                IRef<Method> methodId, Guid accountId)
        {
            return accountId
                .StorageGetByIdProperty((XIntegration intgration) => intgration.accountId)
                .Where(integration => integration.Method != null)
                .Where(integration => integration.Method.id == methodId.id)
                .Where(integration => integration.authorization.HasValue)
                .Select(
                    integration =>
                    {
                        return integration.authorization.StorageGetAsync(
                            authorization =>
                            {
                                return authorization.PairWithKey(integration);
                            },
                            () => default(KeyValuePair<XIntegration, Authorization>?));
                    })
                .Await()
                .SelectWhereHasValue();
        }

        #endregion
    }
}
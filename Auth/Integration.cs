﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Http.Routing;
using BlackBarLabs.Api;
using BlackBarLabs.Persistence.Azure.Attributes;
using EastFive;
using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Security;
using EastFive.Serialization;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController6(
        Route = "Integration",
        Resource = typeof(Integration),
        ContentType = "x-application/auth-integration",
        ContentTypeVersion = "0.1")]
    [StorageResource(typeof(StandardPartitionKeyGenerator))]
    [StorageTable]
    public struct Integration : IReferenceable
    {
        [JsonIgnore]
        public Guid id => integrationRef.id;

        public const string IntegrationIdPropertyName = "id";
        [ApiProperty(PropertyName = IntegrationIdPropertyName)]
        [JsonProperty(PropertyName = IntegrationIdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<Integration> integrationRef;
        
        [JsonIgnore]
        [Storage]
        public IRef<Method> Method { get; set; }

        public const string AccountPropertyName = "account";
        [ApiProperty(PropertyName = AccountPropertyName)]
        [JsonProperty(PropertyName = AccountPropertyName)]
        [Storage]
        public Guid accountId { get; set; }

        [StorageResource(typeof(StandardPartitionKeyGenerator))]
        [StorageTable]
        public struct AuthorizationIntegrationLookup : IReferenceable
        {
            [JsonIgnore]
            public Guid id => authorizationLookupRef.id;

            [RowKey]
            [StandardParititionKey]
            [JsonIgnore]
            public IRef<Authorization> authorizationLookupRef;

            [JsonIgnore]
            [Storage]
            public IRef<Integration> integrationMappingRef;
        }

        [StorageResource(typeof(StandardPartitionKeyGenerator))]
        [StorageTable]
        public struct AccountIntegrationLookup : IReferenceable
        {
            [JsonIgnore]
            public Guid id => accountIntegrationLookupRef.id;

            [RowKey]
            [StandardParititionKey]
            [JsonIgnore]
            public IRef<AccountIntegrationLookup> accountIntegrationLookupRef;

            [JsonIgnore]
            [Storage]
            public IRefs<Integration> integrationRefs;
        }

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        [Storage(Name = AuthorizationPropertyName)]
        public IRefOptional<Authorization> authorization { get; set; }

        [Api.HttpGet] //(MatchAllBodyParameters = false)]
        public async static Task<HttpResponseMessage> GetByAccountAsync(
                [QueryParameter(Name = AccountPropertyName)]Guid accountId,
                Api.Azure.AzureApplication application, SessionToken security,
            MultipartResponseAsync<Integration> onContents,
            ReferencedDocumentNotFoundResponse<object> onAccountNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            if (!await application.CanAdministerCredentialAsync(accountId, security))
                return onUnauthorized();

            return await await GetIntegrationsByAccountAsync(accountId,
                (kvps) => onContents(kvps.SelectKeys()),
                () => onAccountNotFound().AsTask());
        }

        [Api.HttpPost] //(MatchAllBodyParameters = false)]
        public async static Task<HttpResponseMessage> CreateAsync(
                [Property(Name = AccountPropertyName)]Guid accountId,
                [PropertyOptional(Name = AuthorizationPropertyName)]IRefOptional<Authorization> authorizationRefMaybe,
                [Resource]Integration integration,
                Api.Azure.AzureApplication application, EastFive.Api.Controllers.SessionToken security,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists,
            ForbiddenResponse forbidden,
            ReferencedDocumentDoesNotExistsResponse<Authorization> onAuthenticationDoesNotExist,
            GeneralConflictResponse onFailure)
        {
            if (!await application.CanAdministerCredentialAsync(accountId, security))
                return forbidden();

            return await await authorizationRefMaybe.StorageGetAsync(
                authorization =>
                {
                    // TODO? This
                    // var accountIdDidMatch = await await authorization.ParseCredentailParameters(
                    return CreateWithAuthorization(integration, authorization,
                            accountId,
                        () => onCreated(),
                        () => onAlreadyExists(),
                        (why) => onFailure(why));
                },
                async () =>
                {
                    return await await integration.StorageCreateAsync(
                        discard =>
                        {
                            return SaveAccountLookupAsync(accountId, integration,
                                () => onCreated());
                        },
                        () => onAlreadyExists().AsTask());
                });
        }

        [HttpDelete]
        public static async Task<HttpResponseMessage> DeleteAsync(
        [UpdateId(Name = IntegrationIdPropertyName)]IRef<Integration> integrationRef,
                Api.Azure.AzureApplication application, EastFive.Api.Controllers.SessionToken security,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound,
            ForbiddenResponse onForbidden)
        {
            var integrationMaybe = await integrationRef.StorageGetAsync(i => i, () => default(Integration?));
            if (!integrationMaybe.HasValue)
                return onNotFound();

            var integration = integrationMaybe.Value;
            if (!await application.CanAdministerCredentialAsync(integration.accountId, security))
                return onForbidden();

            return await DeleteInternalAsync(integrationRef, application, () => onDeleted(), () => onNotFound());
        }

        [Api.HttpPatch] //(MatchAllBodyParameters = false)]
        public async static Task<HttpResponseMessage> UpdateAsync(
                [Property(Name = IntegrationIdPropertyName)]IRef<Integration> integrationRef,
                [Property(Name = AuthorizationPropertyName)]IRef<Authorization> authorizationRef,
                Api.Azure.AzureApplication application, EastFive.Api.Controllers.SessionToken security,
            ContentTypeResponse<Integration> onUpdated,
            NotFoundResponse onNotFound,
            NotModifiedResponse onNotModified,
            ForbiddenResponse onForbidden,
            ReferencedDocumentDoesNotExistsResponse<Authorization> onAuthenticationDoesNotExist,
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
                            // TODO? This
                            // var accountIdDidMatch = await await authorization.ParseCredentailParameters(
                            integration.Method = authorization.Method; // method is used in the .mappingId
                            integration.authorization = authorizationRef.Optional();
                            return await await SaveAuthorizationLookupAsync(integration.integrationRef, authorization.authorizationRef,
                                async () =>
                                {
                                    await saveAsync(integration);
                                    return await SaveAccountLookupAsync(accountId, integration,
                                        () => onUpdated(integration));
                                },
                                () =>
                                {
                                    // TODO: Check if mapping is to this integration and reply already created.
                                    return onForbidden().AddReason("Authorization is already in use.").AsTask();
                                });
                        },
                        () =>
                        {
                            return onNotModified().AsTask();
                        });

                },
                () => onNotFound());

        }

        public static async Task<TResult> DeleteInternalAsync<TResult>(
                IRef<Integration> integrationRef, Api.Azure.AzureApplication application, 
            Func<TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            var integrationMaybe = await integrationRef.StorageGetAsync(i => i, () => default(Integration?));
            if (!integrationMaybe.HasValue)
                return onNotFound();

            var integration = integrationMaybe.Value;
            return await await integrationRef.StorageDeleteAsync(
                async () =>
                {
                    if (integration.authorization.HasValue)
                    {
                        var authorizationId = integration.authorization.id.Value;
                        var authorizationLookupRef = authorizationId.AsRef<AuthorizationIntegrationLookup>();
                        await authorizationLookupRef.StorageDeleteAsync(() => true);
                    }
                    var accountIntegrationRef = integration.accountId.AsRef<AccountIntegrationLookup>();
                    await accountIntegrationRef.StorageUpdateAsync(
                        async (accountLookup, saveAsync) =>
                        {
                            accountLookup.integrationRefs = accountLookup.integrationRefs.ids
                                .Where(id => id != integration.id)
                                .AsRefs<Integration>();
                            await saveAsync(accountLookup);
                            return true;
                        });
                    return onDeleted();
                },
                () => onNotFound().AsTask());
        }

        public static Task<TResult> GetIntegrationsByAccountAsync<TResult>(Guid accountId,
            Func<IEnumerableAsync<KeyValuePair<Integration,Authorization>>, TResult> onSuccess,
            Func<TResult> onAccountNotFound)
        {
            var accountIntegrationLookupRef = new Ref<AccountIntegrationLookup>(accountId);
            return accountIntegrationLookupRef.StorageGetAsync(
                accountIntegrationLookup =>
                {
                    var kvps = accountIntegrationLookup.integrationRefs
                        .StorageGet()
                        .Where(integration => integration.authorization.HasValue)
                        .Select(
                            integration =>
                            {
                                return integration.authorization.StorageGetAsync(
                                    authorization =>
                                    {
                                        return authorization.PairWithKey(integration);
                                    },
                                    () => default(KeyValuePair<Integration, Authorization>?));
                            })
                        .Await()
                        .SelectWhereHasValue();
                    return onSuccess(kvps);
                },
                () => onAccountNotFound());
        }

        public static Task<TResult> SaveAuthorizationLookupAsync<TResult>(IRef<Integration> integrationRef, IRef<Authorization> authorizationRef,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists)
        {
            var authorizationLookup = new AuthorizationIntegrationLookup
            {
                integrationMappingRef = integrationRef,
                authorizationLookupRef = authorizationRef,
            };
            return authorizationLookup.StorageCreateAsync(
                (discardAuthorizationLookup) => onCreated(),
                onAlreadyExists);
        }

        public static async Task<TResult> CreateWithAuthorization<TResult>(
            Integration integration, Authorization authorization,
            Guid accountId,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists,
            Func<string, TResult> onFailure)
        {
            integration.Method = authorization.Method; // method is used in the .mappingId
            return await await SaveAuthorizationLookupAsync(integration.integrationRef, authorization.authorizationRef,
                async () =>
                {
                    return await await integration.StorageCreateAsync(
                        discard =>
                        {
                            return SaveAccountLookupAsync(accountId, integration, onCreated);
                        },
                        () => onAlreadyExists().AsTask());
                },
                () =>
                {
                    // TODO: Check if mapping is to this integration and reply already created.
                    return onFailure("Authorization is already mapped to another integration.").AsTask();
                });
        }

        private static async Task<TResult> SaveAccountLookupAsync<TResult>(
                Guid accountId, Integration integration,
            Func<TResult> onCreated)
        {
            var accountLookupRef = new Ref<AccountIntegrationLookup>(accountId);
            return await accountLookupRef.StorageCreateOrUpdateAsync(
                (accountLookup) =>
                {
                    accountLookup.accountIntegrationLookupRef = accountLookupRef;
                    return accountLookup;
                },
                async (created, accountLookup, saveAsync) =>
                {
                    accountLookup.integrationRefs = accountLookup.integrationRefs.IsDefaultOrNull() ?
                        integration.integrationRef.id.AsArray().AsRefs<Integration>()
                        :
                        accountLookup.integrationRefs.ids
                            .Append(integration.integrationRef.id)
                            .Distinct()
                            .AsRefs<Integration>();
                    await saveAsync(accountLookup);
                    return onCreated();
                });
        }

        public static async Task<TResult> CreateByMethodAndKeyAsync<TResult>(IRef<Integration> integrationRef, 
                IRef<Authorization> authorizationRef, IRef<Method> methodRef,
                Guid accountId, IDictionary<string, string> parameters,
            Func<Integration, Authorization, TResult> onCreated,
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
                    var integration = new Integration
                    {
                        integrationRef = integrationRef,
                        accountId = accountId,
                        authorization = authorizationRef.Optional(),
                        Method = methodRef,
                    };
                    return CreateWithAuthorization(integration, authorization,
                            accountId,
                        () => onCreated(integration, authorization),
                        () => onIntegrationAlreadyExists(),
                        (why) => onFailure(why));
                },
                () => onAuthorizationAlreadyExists().AsTask());
        }

        public static Task<TResult> CreateByMethodAndKeyAsync<TResult>(IRef<Method> methodRef,
                Guid accountId, IDictionary<string, string> parameters,
            Func<Integration, Authorization, TResult> onCreated,
            Func<string, TResult> onFailure)
        {
            return CreateByMethodAndKeyAsync(
                Guid.NewGuid().AsRef<Integration>(),
                SecureGuid.Generate().AsRef<Authorization>(),
                methodRef,
                accountId,
                parameters,
                onCreated,
                () => throw new Exception("Guid not unique"),
                () => throw new Exception("Guid not unique"),
                onFailure);
        }                

        public static async Task<TResult> GetParametersByAccountIdAsync<TResult>(IRef<Method> methodId, Guid accountId,
            Func<IEnumerableAsync<KeyValuePair<Integration, Authorization>>, TResult> onFound,
            Func<TResult> onNotFound)
        {
            var accountIntegrationLookupRef = new Ref<AccountIntegrationLookup>(accountId);
            return await accountIntegrationLookupRef.StorageGetAsync(
                accountIntegrationLookup =>
                {
                    var integrationsKvp = accountIntegrationLookup.integrationRefs
                        .StorageGet()
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
                                    () => default(KeyValuePair<Integration, Authorization>?));
                            })
                        .Await()
                        .SelectWhereHasValue();
                    return onFound(integrationsKvp);
                },
                onNotFound);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Routing;
using BlackBarLabs.Api;
using BlackBarLabs.Persistence.Azure.Attributes;
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
    [FunctionViewController(
        Route = "AccountMapping",
        ContentType = "x-application/auth-account-mapping",
        ContentTypeVersion = "0.1")]
    [StorageResource(typeof(StandardPartitionKeyGenerator))]
    [StorageTable]
    public struct AccountMapping : IReferenceable
    {
        [JsonIgnore]
        public Guid id => accountMappingId;

        public const string AccountMappingIdPropertyName = "id";
        [ApiProperty(PropertyName = AccountMappingIdPropertyName)]
        [JsonProperty(PropertyName = AccountMappingIdPropertyName)]
        [Storage]
        public Guid accountMappingId;

        [RowKey]
        [StandardParititionKey]
        [JsonIgnore]
        public IRef<AccountMapping> mappingId
        {
            get
            {
                var composeId = this.Method.id
                    .ComposeGuid(this.accountId);
                return new Ref<AccountMapping>(composeId);
            }
            set
            {
            }
        }

        public const string MethodPropertyName = "method";
        [JsonIgnore]
        [Storage(Name = MethodPropertyName)]
        public IRef<Method> Method { get; set; }

        public const string AccountPropertyName = "account";
        [ApiProperty(PropertyName = AccountPropertyName)]
        [JsonProperty(PropertyName = AccountPropertyName)]
        [Storage(Name = AccountPropertyName)]
        public Guid accountId { get; set; }

        [StorageResource(typeof(StandardPartitionKeyGenerator))]
        [StorageTable]
        public struct AccountMappingLookup : IReferenceable
        {
            [JsonIgnore]
            public Guid id => accountMappingLookupId.id;

            [RowKey]
            [StandardParititionKey]
            [JsonIgnore]
            public IRef<AccountMappingLookup> accountMappingLookupId
            {
                get
                {
                    return GetLookup(this.Method, this.accountkey);
                }
                set
                {
                }
            }

            public const string AccountKeyPropertyName = "account";
            [JsonIgnore]
            [Storage]
            public string accountkey { get; set; }

            public const string MethodPropertyName = "method";
            [JsonIgnore]
            [Storage]
            public IRef<Method> Method { get; set; }

            [JsonIgnore]
            [Storage]
            public IRef<AccountMapping> accountMappingId;

            public static IRef<AccountMappingLookup> GetLookup(
                IRef<Method> method, string accountkey)
            {
                var composeId = method.id
                    .ComposeGuid(accountkey.MD5HashGuid());
                return new Ref<AccountMappingLookup>(composeId);
            }
        }

        [StorageResource(typeof(StandardPartitionKeyGenerator))]
        [StorageTable]
        public struct AuthorizationLookup : IReferenceable
        {
            [JsonIgnore]
            public Guid id => authorizationLookupRef.id;

            [RowKey]
            [StandardParititionKey]
            [JsonIgnore]
            public IRef<Authorization> authorizationLookupRef;

            [JsonIgnore]
            [Storage]
            public IRef<AccountMapping> accountMappingRef;
        }

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        [Storage(Name = AuthorizationPropertyName)]
        public IRef<Authorization> authorization { get; set; }

        [Storage]
        public IRefOptional<AccountMappingLookup> accountMappingLookup { get; set; }

        //[Api.HttpPost(MatchAllBodyParameters = true)]
        //public async static Task<HttpResponseMessage> CreateInviteAsync(
        //        [Property(Name = AccountPropertyName)]Guid accountId,
        //        [Property(Name = MethodPropertyName)]IRef<Method> method,
        //        [Resource]AccountMapping accountMapping,
        //        Api.Azure.AzureApplication application, Api.Controllers.Security security,
        //    CreatedResponse onCreated,
        //    ForbiddenResponse onForbidden,
        //    UnauthorizedResponse onUnauthorized,
        //    ReferencedDocumentDoesNotExistsResponse<Authorization> onAuthenticationDoesNotExist,
        //    GeneralConflictResponse onFailure)
        //{
        //    if (!await application.CanAdministerCredentialAsync(accountId, security))
        //        return onUnauthorized();

        //    return await accountMapping.StorageCreateAsync(
        //        createdId =>
        //        {
        //            return onCreated();
        //        },
        //        () => onForbidden().AddReason("Account is already mapped to that authentication."));
        //}

        [Api.HttpPost] //(MatchAllBodyParameters = false)]
        public async static Task<IHttpResponse> CreateAsync(
                [Property(Name = AccountPropertyName)]Guid accountId,
                [Property(Name = AuthorizationPropertyName)]IRef<Authorization> authorizationRef,
                [Resource]AccountMapping accountMapping,
                IAuthApplication application, Api.SessionToken security,
            CreatedResponse onCreated,
            ForbiddenResponse onForbidden,
            UnauthorizedResponse onUnauthorized,
            ReferencedDocumentDoesNotExistsResponse<Authorization> onAuthenticationDoesNotExist,
            GeneralConflictResponse onFailure)
        {
            if (!await application.CanAdministerCredentialAsync(accountId, security))
                return onUnauthorized();
            return await await authorizationRef.StorageGetAsync(
                async authorization =>
                {
                    accountMapping.Method = authorization.Method; // method is used in the .mappingId
                    var authorizationLookup = new AuthorizationLookup
                    {
                        accountMappingRef = accountMapping.mappingId,
                        authorizationLookupRef = authorizationRef,
                    };
                    return await await authorizationLookup.StorageCreateAsync(
                        async (idDiscard) =>
                        {
                            accountMapping.accountMappingLookup = await await authorization.ParseCredentailParameters(
                                    application,
                                (accountKey, loginProvider) =>
                                {
                                    var lookup = new AccountMappingLookup()
                                    {
                                        accountkey = accountKey,
                                        accountMappingId = accountMapping.mappingId,
                                        Method = authorization.Method,
                                    };
                                    return lookup.StorageCreateAsync(
                                        (discard) => new RefOptional<AccountMappingLookup>(
                                            lookup.accountMappingLookupId),
                                        () => new RefOptional<AccountMappingLookup>());
                                },
                                (why) =>
                                {
                                    var amLookupMaybe = new RefOptional<AccountMappingLookup>();
                                    return amLookupMaybe.AsTask();
                                });
                            return await accountMapping.StorageCreateAsync(
                                createdId =>
                                {
                                    return onCreated();
                                },
                                () => onForbidden().AddReason("Account is already mapped to that authentication."));
                        },
                        () => onFailure("Authorization is already mapped to another account.").AsTask());
                },
                () => onAuthenticationDoesNotExist().AsTask());
        }

        internal static async Task<TResult> CreateByMethodAndKeyAsync<TResult>(Authorization authorization, 
                string externalAccountKey, Guid internalAccountId,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyMapped)
        {
            var accountMapping = new AccountMapping()
            {
                accountId = internalAccountId,
            };
            accountMapping.Method = authorization.Method; // method is used in the .mappingId
            var authorizationLookup = new AuthorizationLookup
            {
                accountMappingRef = accountMapping.mappingId,
                authorizationLookupRef = authorization.authorizationRef,
            };
            bool created = await authorizationLookup.StorageCreateAsync(
                (idDiscard) =>
                {
                    return true;
                },
                () =>
                {
                    // I guess this is cool... 
                    return false;
                });

            var lookup = new AccountMappingLookup()
            {
                accountkey = externalAccountKey,
                accountMappingId = accountMapping.mappingId,
                Method = authorization.Method,
            };
            accountMapping.accountMappingLookup = await lookup.StorageCreateAsync(
                (discard) => new RefOptional<AccountMappingLookup>(
                    lookup.accountMappingLookupId),
                () => new RefOptional<AccountMappingLookup>());

            return await accountMapping.StorageCreateAsync(
                createdId =>
                {
                    return onCreated();
                },
                () => onAlreadyMapped());
        }
    

        internal static async Task<TResult> FindByMethodAndKeyAsync<TResult>(IRef<Method> authenticationId, string authorizationKey,
                Authorization authorization,
            Func<Guid, TResult> onFound,
            Func<TResult> onNotFound)
        {
            var lookupRef = AccountMappingLookup.GetLookup(authenticationId, authorizationKey);
            return await await lookupRef.StorageGetAsync(
                lookup =>
                {
                    return lookup.accountMappingId.StorageGetAsync(
                        accountMapping => onFound(accountMapping.accountId),
                        () => onNotFound());
                },
                async () =>
                {
                    var accountMappingRef = new Ref<AuthorizationLookup>(authorization.id);
                    return await await accountMappingRef.StorageGetAsync(
                        lookup =>
                        {
                            return lookup.accountMappingRef.StorageGetAsync(
                                accountMapping => onFound(accountMapping.accountId),
                                () => onNotFound());
                        },
                        () => onNotFound().AsTask());
                });
        }
    }
}
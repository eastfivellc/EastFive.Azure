﻿using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Meta.OpenApi;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Serialization;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController(
        Route = "AccountMapping",
        ContentType = "x-application/auth-account-mapping",
        ContentTypeVersion = "0.1")]
    [OpenApiRoute(Collection = AppSettings.Auth.OpenApiCollectionName)]
    [StorageTable]
    public struct AccountMapping : IReferenceable
    {
        [JsonIgnore]
        public Guid id => this.Method.id
            .ComposeGuid(this.accountkey.MD5HashGuid());

        [RowKey(StoresFullValue = false)]
        [Storage]
        public string accountkey { get; set; }

        [ETag]
        [JsonIgnore]
        public string eTag;

        public const string MethodPropertyName = "method";
        [JsonIgnore]
        [Storage(Name = MethodPropertyName)]
        [PartitionById(StoresFullValue = false)]
        public IRef<Method> Method { get; set; }

        public const string AccountPropertyName = "account";
        [ApiProperty(PropertyName = AccountPropertyName)]
        [JsonProperty(PropertyName = AccountPropertyName)]
        [Storage(Name = AccountPropertyName)]
        [IdHashXX32Lookup]
        public Guid accountId { get; set; }

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        [Storage(Name = AuthorizationPropertyName)]
        public IRef<Authorization> authorization { get; set; }

        //[Storage]
        //public IRefOptional<AccountMappingLookup> accountMappingLookup { get; set; }

        //[Api.HttpPost]
        //public async static Task<IHttpResponse> CreateAsync(
        //        [Property(Name = AccountPropertyName)]Guid accountId,
        //        [Property(Name = AuthorizationPropertyName)]IRef<Authorization> authorizationRef,
        //        [Resource]AccountMapping accountMapping,
        //        IAuthApplication application, EastFive.Azure.Auth.SessionToken security,
        //    CreatedResponse onCreated,
        //    ForbiddenResponse onForbidden,
        //    UnauthorizedResponse onUnauthorized,
        //    ReferencedDocumentDoesNotExistsResponse<Authorization> onAuthenticationDoesNotExist,
        //    GeneralConflictResponse onFailure)
        //{
        //    if (!await application.CanAdministerCredentialAsync(accountId, security))
        //        return onUnauthorized();
        //    return await await authorizationRef.StorageGetAsync(
        //        async authorization =>
        //        {
        //            accountMapping.Method = authorization.Method; // method is used in the .mappingId
        //            var authorizationLookup = new AuthorizationLookup
        //            {
        //                accountMappingRef = accountMapping.mappingId,
        //                authorizationLookupRef = authorizationRef,
        //            };
        //            return await await authorizationLookup.StorageCreateAsync(
        //                async (idDiscard) =>
        //                {
        //                    accountMapping.accountMappingLookup = await await authorization.ParseCredentailParameters(
        //                            application,
        //                        (accountKey, loginProvider) =>
        //                        {
        //                            var lookup = new AccountMappingLookup()
        //                            {
        //                                accountkey = accountKey,
        //                                accountMappingId = accountMapping.mappingId,
        //                                Method = authorization.Method,
        //                            };
        //                            return lookup.StorageCreateAsync(
        //                                (discard) => new RefOptional<AccountMappingLookup>(
        //                                    lookup.accountMappingLookupId),
        //                                () => new RefOptional<AccountMappingLookup>());
        //                        },
        //                        (why) =>
        //                        {
        //                            var amLookupMaybe = new RefOptional<AccountMappingLookup>();
        //                            return amLookupMaybe.AsTask();
        //                        });
        //                    return await accountMapping.StorageCreateAsync(
        //                        createdId =>
        //                        {
        //                            return onCreated();
        //                        },
        //                        () => onForbidden().AddReason("Account is already mapped to that authentication."));
        //                },
        //                () => onFailure("Authorization is already mapped to another account.").AsTask());
        //        },
        //        () => onAuthenticationDoesNotExist().AsTask());
        //}

        internal static async Task<TResult> CreateByMethodAndKeyAsync<TResult>(Authorization authorization, 
                string externalAccountKey, Guid internalAccountId,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyMapped,
                bool shouldRemap = false)
        {
            var accountMapping = new AccountMapping()
            {
                accountId = internalAccountId,
                Method = authorization.Method,
                authorization = authorization.authorizationRef,
                accountkey = externalAccountKey,
            };
            //accountMapping.Method = authorization.Method; // method is used in the .mappingId
            //accountMapping.authorization = authorization.authorizationRef;
            //var authorizationLookup = new AuthorizationLookup
            //{
            //    accountMappingRef = accountMapping.mappingId,
            //    authorizationLookupRef = authorization.authorizationRef,
            //};
            //bool created = await authorizationLookup.StorageCreateAsync(
            //    (idDiscard) =>
            //    {
            //        return true;
            //    },
            //    () =>
            //    {
            //        // I guess this is cool... 
            //        return false;
            //    });

            //var lookup = new AccountMappingLookup()
            //{
            //    accountkey = externalAccountKey,
            //    accountMappingId = accountMapping.mappingId,
            //    Method = authorization.Method,
            //};
            //accountMapping.accountMappingLookup = await await lookup.StorageCreateAsync(
            //    (discard) => lookup.accountMappingLookupId.Optional().AsTask(),
            //    async () =>
            //    {
            //        if (!shouldRemap)
            //            return RefOptional<AccountMappingLookup>.Empty();
            //        return await lookup.accountMappingLookupId.StorageCreateOrUpdateAsync(
            //            async (created, lookupToUpdate, saveAsync) =>
            //            {
            //                lookupToUpdate.accountMappingId = accountMapping.mappingId;
            //                await saveAsync(lookupToUpdate);
            //                return lookupToUpdate.accountMappingLookupId.Optional();
            //            });
            //    });

            return await accountMapping.StorageCreateAsync(
                createdId =>
                {
                    return onCreated(); //.AsTask();
                },
                onAlreadyExists:() =>
                {
                    return onAlreadyMapped();
                    //if (!shouldRemap)
                    //    return onAlreadyMapped();
                    //return await accountMapping.mappingId.StorageCreateOrUpdateAsync(
                    //    async (created, mapping, saveAsync) =>
                    //    {
                    //        mapping.accountMappingLookup = accountMapping.accountMappingLookup;
                    //        mapping.authorization = accountMapping.authorization;
                    //        await saveAsync(mapping);
                    //        return onCreated();
                    //    });
                });
        }
    
        public static Task<TResult> FindByMethodAndKeyAsync<TResult>(IRef<Method> authenticationMethodRef, string authorizationKey,
                Authorization authorization,
            Func<Guid, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return authorizationKey.StorageGetAsync(authenticationMethodRef,
                (AccountMapping accountMapping) =>
                {
                    return onFound(accountMapping.accountId);
                },
                () =>
                {
                    return onNotFound();
                });

            //var lookupRef = AccountMappingLookup.GetLookup(authenticationMethodRef, authorizationKey);
            //return await await lookupRef.StorageGetAsync(
            //    lookup =>
            //    {
            //        return lookup.accountMappingId.StorageGetAsync(
            //            accountMapping => onFound(accountMapping.accountId),
            //            () => onNotFound());
            //    },
            //    async () =>
            //    {
            //        var accountMappingRef = new Ref<AuthorizationLookup>(authorization.id);
            //        return await await accountMappingRef.StorageGetAsync(
            //            lookup =>
            //            {
            //                return lookup.accountMappingRef.StorageGetAsync(
            //                    accountMapping => onFound(accountMapping.accountId),
            //                    () => onNotFound());
            //            },
            //            () => onNotFound().AsTask());
            //    });
        }

        public static async Task<TResult> DeleteByMethodAndKeyAsync<TResult>(IRef<Method> authenticationId, string authorizationKey,
            Func<Guid, TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            return await authorizationKey.StorageDeleteAsync(authenticationId,
                (AccountMapping discard) =>
                {
                    return onDeleted(discard.id);
                },
                () => onNotFound());
        }
    }

    [StorageTable(TableName = "accountmapping")]
    public struct AccountMappingOld : IReferenceable
    {
        [JsonIgnore]
        public Guid id => mappingId.IsNotDefaultOrNull() ? mappingId.id : default(Guid);

        [RowKey]
        [StandardParititionKey]
        [JsonIgnore]
        public IRef<AccountMapping> mappingId
        {
            get
            {
                var composeId = this.Method.id
                    .ComposeGuid(this.accountId)
                    .AsRef<AccountMapping>();
                return composeId;
            }
            set
            {
            }
        }

        public const string MethodPropertyName = "method";
        [Storage(Name = MethodPropertyName)]
        public IRef<Method> Method { get; set; }

        public const string AccountPropertyName = "account";
        [ApiProperty(PropertyName = AccountPropertyName)]
        [JsonProperty(PropertyName = AccountPropertyName)]
        [Storage(Name = AccountPropertyName)]
        public Guid accountId { get; set; }

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
            [Storage]
            public string accountkey { get; set; }

            public const string MethodPropertyName = "method";
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

    }
}
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Security;
using EastFive.Serialization;
using System.Net.Http;
using EastFive.Api.Auth;

namespace EastFive.Azure.Login
{
    [FunctionViewController(
        Route = "XAccount",
        Resource = typeof(Account),
        ContentType = "x-application/login-account",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    [Html]
    public struct Account : IReferenceable
    {
        [JsonIgnore]
        public Guid id => accountRef.id;

        public const string AccountPropertyName = "id";
        [ApiProperty(PropertyName = AccountPropertyName)]
        [JsonProperty(PropertyName = AccountPropertyName)]
        [RowKey]
        [StandardParititionKey]
        [HtmlInputHidden]
        public IRef<Account> accountRef;

        public const string UserIdentificationPropertyName = "user_identification";
        [ApiProperty(PropertyName = UserIdentificationPropertyName)]
        [JsonProperty(PropertyName = UserIdentificationPropertyName)]
        [Storage]
        [HtmlInput(Label = "Username or email")]
        public string userIdentification;

        public const string PasswordPropertyName = "password";
        [ApiProperty(PropertyName = PasswordPropertyName)]
        [JsonProperty(PropertyName = PasswordPropertyName)]
        [Storage]
        [HtmlInput(Label = "Password")]
        public string password;

        [Api.HttpPost]
        [HtmlAction(Label = "Create")]
        public static async Task<IHttpResponse> UpdateAsync(
                [Property(Name = UserIdentificationPropertyName)]string userIdentification,
                [Property(Name = PasswordPropertyName)]string password,
            CreatedResponse onUpdated,
            AlreadyExistsResponse onUsernameAlreadyTaken,
            GeneralConflictResponse onInvalidPassword)
        {
            if (!password.HasBlackSpace())
                return onInvalidPassword("Password cannot be empty");

            var accountRef = userIdentification
                .MD5HashGuid()
                .AsRef<Account>();
            return await accountRef
                .StorageCreateOrUpdateAsync(
                    async (created, account, saveAsync) =>
                    {
                        if (!created)
                            return onUsernameAlreadyTaken();

                        account.userIdentification = userIdentification;
                        account.password = Account.GeneratePasswordHash(userIdentification, password);
                        await saveAsync(account);
                        return onUpdated();
                    });
        }

        [Api.HttpGet]
        [RequiredClaim(
            System.Security.Claims.ClaimTypes.Role,
            Auth.ClaimValues.Roles.SuperAdmin)]
        public static IHttpResponse List(
            MultipartAsyncResponse<Account> onFound)
        {
            var accounts = typeof(Authentication)
                .StorageGetAll()
                .Select(obj => (Authentication)obj)
                .Distinct(auth => auth.userIdentification)
                .Select(
                    (auth) =>
                    {
                        var accountRef = auth.userIdentification
                            .MD5HashGuid()
                            .AsRef<Account>();
                        return accountRef.StorageUpdateAsync(
                            async (account, saveAsync) =>
                            {
                                account.userIdentification = auth.userIdentification;
                                await saveAsync(account);
                                account.password = "*********";
                                return account;
                            },
                            () =>
                            {
                                return new Account
                                {
                                    accountRef = accountRef,
                                    userIdentification = auth.userIdentification,
                                    password = "*********",
                                };
                            });
                    })
                .Await();
            return onFound(accounts);
        }

        [Api.HttpPatch]
        [RequiredClaim(
            System.Security.Claims.ClaimTypes.Role,
            Auth.ClaimValues.Roles.SuperAdmin)]
        public static Task<IHttpResponse> Update(
                [UpdateId]IRef<Account> accountRef,
                [Property(Name = PasswordPropertyName)] string password,
            NoContentResponse onUpdated,
            NotFoundResponse onNotFound)
        {
            return accountRef.StorageUpdateAsync(
                async (account, saveAsync) =>
                {
                    account.password = Account.GeneratePasswordHash(
                        account.userIdentification, password);
                    await saveAsync(account);
                    return onUpdated();
                },
                () => onNotFound());
        }

        internal static string GeneratePasswordHash(string userIdentification, string password)
        {
            return $"{userIdentification} {password}".SHAHash().ToBase64String();
        }
    }
}

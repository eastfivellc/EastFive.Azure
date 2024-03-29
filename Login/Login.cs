﻿using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Security;
using EastFive.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Login
{
    [FunctionViewController(
        Namespace = "Azure/Login",
        Route = "Login",
        ContentType = "x-application/login",
        ContentTypeVersion = "0.1")]
    public struct Login : IReferenceable
    {
        [JsonIgnore]
        public Guid id => loginRef.id;

        public const string LoginPropertyName = "id";
        [ApiProperty(PropertyName = LoginPropertyName)]
        [JsonProperty(PropertyName = LoginPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<Login> loginRef;

        public const string UserNamePropertyName = "username";
        [ApiProperty(PropertyName = UserNamePropertyName)]
        [JsonProperty(PropertyName = UserNamePropertyName)]
        [Storage]
        public string username;

        public const string PasswordPropertyName = "password";
        [ApiProperty(PropertyName = PasswordPropertyName)]
        [JsonProperty(PropertyName = PasswordPropertyName)]
        [Storage]
        public string password;

        [Api.HttpAction("Authenticate")]
        public static async Task<IHttpResponse> AuthenticateAsync(
                [Property(Name = UserNamePropertyName)]string username,
                [Property(Name = PasswordPropertyName)] string password,
                IAzureApplication application, IHttpRequest httpRequest,
            CreatedBodyResponse<Auth.Session> onSuccess,
            GeneralConflictResponse onInvalidUserNameOrPassword)
        {
            return await await Authentication.CheckCredentialsAsync(username, password,
                async account =>
                {
                    var session = await CreateSession(username, application, httpRequest);
                    return onSuccess(session);
                },
                why => onInvalidUserNameOrPassword(why).AsTask());
        }

        [Api.HttpAction("CreateAccount")]
        public static async Task<IHttpResponse> CreateAccountAsync(
                [Property(Name = UserNamePropertyName)]string username,
                [Property(Name = PasswordPropertyName)]string password,
                IAzureApplication application, IHttpRequest httpRequest,
            CreatedBodyResponse<Auth.Session> onCreated,
            AlreadyExistsResponse onUsernameAlreadyTaken,
            GeneralConflictResponse onInvalidPassword)
        {
            if (password.IsNullOrWhiteSpace())
                return onInvalidPassword("Password cannot be empty");

            return await Account
                .GetRef(username)
                .StorageCreateOrUpdateAsync(
                    async (created, account, saveAsync) =>
                    {
                        if (!created)
                            return onUsernameAlreadyTaken();

                        account.userIdentification = username;
                        account.password = Account.GeneratePasswordHash(username, password);
                        await saveAsync(account);
                        var session = await CreateSession(username, application, httpRequest);
                        return onCreated(session);
                    });
        }

        private static async Task<Auth.Session> CreateSession(string userIdentification,
            IAzureApplication application, IHttpRequest request)
        {
            var authentication = new Authentication
            {
                authenticationRef = Ref<Authentication>.SecureRef(),
                authenticated = DateTime.UtcNow,
                userIdentification = userIdentification,
                token = SecureGuid.Generate().ToString("N"),
            };
            return await await authentication
                .StorageCreateAsync(
                    async (authenticationDiscard) =>
                    {
                        var method = EastFive.Azure.Auth.Method.ByMethodName(
                            CredentialProvider.IntegrationName, application);

                        var parameters = new Dictionary<string, string>()
                        {
                            { "state",  authentication.authenticationRef.id.ToString() },
                            { "token",  authentication.token },
                            {  CredentialProvider.referrerKey, "https://example.com/internal" }
                        };

                        return await await method.RedeemTokenAsync(parameters, application,
                            async (externalAccountKey, authorizationRefMaybe, loginProvider, extraParams) =>
                            {
                                var authorization = new Auth.Authorization
                                {
                                    authorizationRef = new Ref<Auth.Authorization>(Security.SecureGuid.Generate()),
                                    Method = method.authenticationId,
                                    parameters = extraParams,
                                    authorized = true,
                                };

                                return await await Auth.Redirection.AuthorizeWithAccountAsync(
                                        authorization,
                                        async (authorizationToSave) =>
                                        {
                                            bool created = await authorizationToSave.StorageCreateAsync(
                                                discard => true);
                                        },
                                        method,
                                        externalAccountKey, extraParams,
                                        application, request, loginProvider,
                                        request.RequestUri,
                                    async (accountId, authorizationUpdated) =>
                                    {
                                        return await CreateSessionAsync(authorization);
                                    },
                                    (interruptTo, accountId, authorizationUpdated) => throw new Exception($"Cannot redirect to `{interruptTo}`"),
                                    (why, authorizationUpdated) => throw new Exception(why),
                                        default);

                                Task<Auth.Session> CreateSessionAsync(Auth.Authorization authorization)
                                {
                                    return Auth.Session.CreateAsync(application,
                                        authorization.authorizationRef.Optional());
                                }
                            },
                            (aId, paramsSet) => throw new Exception("Logout was not provided."),
                            (why) => throw new Exception(why),
                            why => throw new Exception(why));
                    });
        }
    }
}

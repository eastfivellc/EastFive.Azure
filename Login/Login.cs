using EastFive.Api;
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
                [Resource]Login login,
                IAzureApplication application,
            CreatedBodyResponse<Auth.Session> onSuccess,
            AlreadyExistsResponse onAlreadyExists,
            GeneralConflictResponse onInvalidUserNameOrPassword)
        {
            return await await Account
                .GetRef(login.username)
                .StorageGetAsync(
                    async (account) =>
                    {
                        if (!account.IsPasswordValid(login.password))
                            return onInvalidUserNameOrPassword("Invalid username or password");

                        var session = await CreateSession(login.username, application);
                        return onSuccess(session);
                    },
                    () => onInvalidUserNameOrPassword("Invalid username or password").AsTask());
        }

        [Api.HttpAction("CreateAccount")]
        public static async Task<IHttpResponse> CreateAccountAsync(
                [Property(Name = UserNamePropertyName)]string username,
                [Property(Name = PasswordPropertyName)]string password,
                IAzureApplication application,
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
                        var session = await CreateSession(username, application);
                        return onCreated(session);
                    });
        }

        private static async Task<Auth.Session> CreateSession(string userIdentification,
            IAzureApplication application)
        {
            var authentication = new Authentication
            {
                authenticationRef = Ref<Authentication>.SecureRef(),
                authenticated = DateTime.UtcNow,
                userIdentification = userIdentification,
                state = SecureGuid.Generate().ToString("N"),
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
                                { "token",  authentication.state },
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


                                return await await Auth.Redirection.IdentifyAccountAsync(authorization, method,
                                        externalAccountKey, extraParams,
                                        application, loginProvider, default,
                                    async (accountId, onRecreate) =>
                                    {
                                        authorization.accountIdMaybe = accountId;
                                        bool created = await authorization.StorageCreateAsync(
                                            discard => true);
                                        return await CreateSessionAsync(authorization);
                                    },
                                    async (accountId) =>
                                    {
                                        authorization.accountIdMaybe = accountId;
                                        bool created = await authorization.StorageCreateAsync(
                                            discard => true);
                                        return await CreateSessionAsync(authorization);
                                    },
                                    () => throw new Exception(),
                                    (interruptTo) => throw new Exception($"Cannot redirect to `{interruptTo}`"),
                                    why => throw new Exception(why),
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

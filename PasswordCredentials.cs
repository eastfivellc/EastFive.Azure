using System;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

using BlackBarLabs.Extensions;
using System.Security.Claims;
using BlackBarLabs;
using EastFive.Linq.Async;
using EastFive.Extensions;
using EastFive.Security.SessionServer;
using EastFive.Azure;

namespace EastFive.Api.Azure.Credentials
{
    public struct PasswordCredential
    {
        public Guid id;
        public Guid actorId;
        public string displayName;
        public string userId;
        public bool isEmail;
        internal bool forceChangePassword;
        internal DateTime? lastSent;
    }

    public class PasswordCredentials
    {
        private Context context;
        private EastFive.Security.SessionServer.Persistence.DataContext dataContext;
        private IProvideLoginManagement managmentProvider;

        internal PasswordCredentials(Context context, EastFive.Security.SessionServer.Persistence.DataContext dataContext)
        {
            this.dataContext = dataContext;
            this.context = context;
            // TODO: Refactor this class so it works with any Management Provider
            context.GetManagementProvider(Enum.GetName(typeof(CredentialValidationMethodTypes), CredentialValidationMethodTypes.Password),
                (identityService) =>
                {
                    this.managmentProvider = identityService;
                    return true;
                },
                () => false,
                (why) => false);
        }

        #region Password Credential

        public async Task<TResult> CreatePasswordCredentialsAsync<TResult>(Guid passwordCredentialId, Guid actorId,
                string displayName, string username, bool isEmail, string token, bool forceChange,
                DateTime? emailLastSent, Uri callbackUrl,
                EastFive.Api.SessionToken security, IAuthApplication application,
            Func<TResult> onSuccess,
            Func<TResult> credentialAlreadyExists,
            Func<Guid, TResult> onUsernameAlreadyInUse,
            Func<TResult> onPasswordInsufficent,
            Func<TResult> onRelationshipAlreadyExists,
            Func<TResult> onLoginAlreadyUsed,
            Func<TResult> onUnathorized,
            Func<TResult> onServiceNotAvailable,
            Func<string, TResult> onFailure)
        {
            if (!await application.CanAdministerCredentialAsync(actorId, security))
                return onUnathorized();
            
            return await await (application).InstantiateAll<IProvideLoginManagement>()
                .FirstAsync(
                    async managmentProvider =>
                    {
                        if (string.IsNullOrWhiteSpace(displayName))
                            displayName = "User";

                        return await await managmentProvider.CreateAuthorizationAsync(displayName,
                            username, isEmail, token, forceChange,
                            async loginId =>
                            {
                                var result = await await dataContext.PasswordCredentials.CreatePasswordCredentialAsync(
                                    passwordCredentialId, actorId, loginId, emailLastSent,
                                    async () =>
                                    {
                                        if (!isEmail || !emailLastSent.HasValue)
                                            return onSuccess();

                                        return await Web.Configuration.Settings.GetUri(EastFive.Security.SessionServer.Configuration.AppSettings.LandingPage,
                                            async (landingPage) =>
                                            {
                                                var method = CredentialValidationMethodTypes.Password.ToString();
                                                return await await context.Sessions.CreateLoginAsync(Guid.NewGuid(),
                                                    method, landingPage, landingPage,
                                                    (type) => callbackUrl,
                                                    async (authenticationRequest) =>
                                                    {
                                                        var loginUrl = authenticationRequest.loginUrl;
                                                        var resultMail = await SendInvitePasswordAsync(username, username, token, loginUrl,
                                                            onSuccess, onServiceNotAvailable, onFailure);
                                                        return resultMail;
                                                    },
                                                    "GUID not unique".AsFunctionException<Task<TResult>>(),
                                                    "Password system said not available while in use".AsFunctionException<Task<TResult>>(),
                                                    "Password system said not initialized while in use".AsFunctionException<string, Task<TResult>>(),
                                                    onFailure.AsAsyncFunc());
                                            },
                                            onFailure.AsAsyncFunc());
                                    },
                                    async () =>
                                        await managmentProvider.DeleteAuthorizationAsync(loginId,
                                            () => credentialAlreadyExists(),
                                            (why) => onFailure(why),
                                            () => onFailure("Service became unsupported after credentialAlreadyExists"),
                                            (why) => onFailure(why)),
                                    async () =>
                                        await managmentProvider.DeleteAuthorizationAsync(loginId,
                                            () => onRelationshipAlreadyExists(),
                                            (why) => onFailure(why),
                                            () => onFailure("Service became unsupported after onRelationshipAlreadyExists"),
                                            (why) => onFailure(why)),
                                    async () =>
                                        await managmentProvider.DeleteAuthorizationAsync(loginId,
                                            () => onRelationshipAlreadyExists(),
                                            (why) => onFailure(why),
                                            () => onFailure("Service became unsupported after onLoginAlreadyUsed"),
                                            (why) => onFailure(why)));
                                return result;
                            },
                            (loginId) => onUsernameAlreadyInUse(loginId).ToTask(),
                            () => onPasswordInsufficent().ToTask(),
                            (why) => onFailure(why).ToTask(),
                            () => onFailure("Service not supported").ToTask(),
                            (why) => onFailure(why).ToTask());
                    },
                    onServiceNotAvailable.AsAsyncFunc());
        }

        public async Task<TResult> CreatePasswordCredentialsAsync<TResult>(Guid passwordCredentialId, Guid actorId,
            string displayName, string username, string token, bool forceChange,
            Guid performingActorId, System.Security.Claims.Claim[] claims,
            Func<TResult> onSuccess,
            Func<TResult> credentialAlreadyExists,
            Func<Guid, TResult> onUsernameAlreadyInUse,
            Func<TResult> onPasswordInsufficent,
            Func<TResult> onRelationshipAlreadyExists,
            Func<TResult> onLoginAlreadyUsed,
            Func<TResult> onUnathorized,
            Func<TResult> onServiceNotAvailable,
            Func<string, TResult> onFailure)
        {
            if (!await Library.configurationManager.CanAdministerCredentialAsync(
                actorId, performingActorId, claims))
                return onUnathorized();

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = "User";

            var createLoginResult = await await managmentProvider.CreateAuthorizationAsync(displayName,
                username, false, token, forceChange,
                async loginId =>
                {
                    var result = await await dataContext.PasswordCredentials.CreatePasswordCredentialAsync(
                        passwordCredentialId, actorId, loginId, default(DateTime?),
                        onSuccess.AsAsyncFunc(),
                        async () =>
                            await managmentProvider.DeleteAuthorizationAsync(loginId,
                                () => credentialAlreadyExists(),
                                (why) => onFailure(why),
                                () => onFailure("Service became unsupported after credentialAlreadyExists"),
                                (why) => onFailure(why)),
                        async () =>
                            await managmentProvider.DeleteAuthorizationAsync(loginId,
                                () => onRelationshipAlreadyExists(),
                                (why) => onFailure(why),
                                () => onFailure("Service became unsupported after onRelationshipAlreadyExists"),
                                (why) => onFailure(why)),
                        async () =>
                            await managmentProvider.DeleteAuthorizationAsync(loginId,
                                () => onRelationshipAlreadyExists(),
                                (why) => onFailure(why),
                                () => onFailure("Service became unsupported after onLoginAlreadyUsed"),
                                (why) => onFailure(why)));
                    return result;
                },
                (loginId) => onUsernameAlreadyInUse(loginId).ToTask(),
                () => onPasswordInsufficent().ToTask(),
                (why) => onFailure(why).ToTask(),
                () => onFailure("Service not supported").ToTask(),
                (why) => onFailure(why).ToTask());
            return createLoginResult;
        }

        internal async Task<TResult> GetPasswordCredentialAsync<TResult>(Guid passwordCredentialId,
                EastFive.Api.SessionToken security, IAzureApplication application,
            Func<PasswordCredential, TResult> success,
            Func<TResult> notFound,
            Func<TResult> onUnauthorized,
            Func<string, TResult> onServiceNotAvailable)
        {
            return await await this.dataContext.PasswordCredentials.FindPasswordCredentialAsync(passwordCredentialId,
                async (actorId, loginId, lastSent) =>
                {
                    if (!await application.CanAdministerCredentialAsync(actorId, security))
                        return onUnauthorized();
                    
                    return await this.managmentProvider.GetAuthorizationAsync(loginId,
                        (loginInfo) =>
                        {
                            return success(new PasswordCredential
                            {
                                id = passwordCredentialId,
                                displayName = loginInfo.displayName,
                                actorId = actorId,
                                userId = loginInfo.userName,
                                isEmail = loginInfo.isEmail,
                                forceChangePassword = loginInfo.forceChangePassword,
                                lastSent = lastSent,
                            });
                        },
                        () => notFound(), // TODO: Log this
                        (why) => onServiceNotAvailable(why),
                        () => onServiceNotAvailable("not supported"),
                        (why) => onServiceNotAvailable(why));
                },
                () => notFound().ToTask());
        }

        internal async Task<TResult> GetPasswordCredentialByActorAsync<TResult>(Guid actorId,
                EastFive.Api.SessionToken security, IAzureApplication application,
            Func<PasswordCredential[], TResult> success,
            Func<TResult> notFound,
            Func<TResult> onUnauthorized,
            Func<string, TResult> onServiceNotAvailable)
        {
            if (!await application.CanAdministerCredentialAsync(actorId, security))
                return onUnauthorized();

            return await await this.dataContext.PasswordCredentials.FindPasswordCredentialByActorAsync(actorId,
                async (credentials) =>
                {
                    var pwCreds = await credentials.Select(
                        async credential =>
                        {
                            return await managmentProvider.GetAuthorizationAsync(credential.loginId,
                                (loginInfo) =>
                                {
                                    return new PasswordCredential
                                    {
                                        id = credential.id,
                                        displayName = loginInfo.displayName,
                                        actorId = actorId,
                                        userId = loginInfo.userName,
                                        isEmail = loginInfo.isEmail,
                                        forceChangePassword = loginInfo.forceChangePassword,
                                        lastSent = credential.lastSent,
                                    };
                                },
                                () => default(PasswordCredential?), // TODO: Log this
                                (why) => default(PasswordCredential?),
                                () => default(PasswordCredential?),
                                (why) => default(PasswordCredential?));
                        })
                        .WhenAllAsync()
                        .SelectWhereHasValueAsync()
                        .ToArrayAsync();
                    return success(pwCreds);
                },
                () => notFound().ToTask());
        }

        public struct LoginInfo
        {
            public string Username;
            public string Email;
            public Guid LoginId;
            public Guid ActorId;
            public bool AccountEnabled;
            public IDictionary<string, string> Tokens;
            public EastFive.Api.Azure.Credentials.CredentialValidationMethodTypes Method;
            public string DisplayName;
        }
       
        public async Task<TResult> GetAllLoginInfoAsync<TResult>(Guid actorPerforming, System.Security.Claims.Claim [] claims,
            Func<LoginInfo[], TResult> onSuccess,
            Func<TResult> onNoTokenProviders)
        {
            if (!ServiceConfiguration.credentialProviders.Any())
                return onNoTokenProviders();

            var tokenProvider = ServiceConfiguration.credentialProviders.First().Value;
            Enum.TryParse(tokenProvider.GetType().GetCustomAttribute<Attributes.IntegrationNameAttribute>().Name, out EastFive.Api.Azure.Credentials.CredentialValidationMethodTypes method);
            return await await this.context.Credentials.GetAllAccountIdAsync(
                async credentialMappings =>
                {
                    var mappings = await credentialMappings
                        .WhereAsync(info => Library.configurationManager.CanAdministerCredentialAsync(info.actorId, actorPerforming, claims))
                        .SelectAsync(
                            async credentialMapping =>
                            {
                                return new LoginInfo
                                {
                                    AccountEnabled = true,
                                    ActorId = credentialMapping.actorId,
                                    LoginId = credentialMapping.loginId,
                                    Method = method,
                                }.PairWithValue(credentialMapping.method);
                            })
                        .WhenAllAsync();
                    var pairs = await mappings
                        .Select(x => x.Value)
                        .Distinct()
                        .Select(async m =>
                        {
                            if (!ServiceConfiguration.managementProviders.ContainsKey(m))
                                return m.PairWithValue(new EastFive.Security.SessionServer.LoginInfo[] { });

                            // AADB2C fails when it is called too often so now make one call to get it all
                            var provider = ServiceConfiguration.managementProviders[m];
                            return m.PairWithValue(await provider.GetAllAuthorizationsAsync(
                                infos => infos,
                                (why) => new EastFive.Security.SessionServer.LoginInfo[] { },
                                () => new EastFive.Security.SessionServer.LoginInfo[] { },
                                (why) => new EastFive.Security.SessionServer.LoginInfo[] { }));
                        })
                        .WhenAllAsync();
                    var lookups = mappings
                        .Select(mapping =>
                        {
                            if (default(Guid) == mapping.Key.LoginId)
                                return mapping.Key;
                            var users = pairs.First(x => x.Key == mapping.Value).Value;
                            var user = users.Where(x => x.loginId == mapping.Key.LoginId).ToArray();
                            var map = mapping.Key;
                            if (user.Length == 0)
                            {
                                map.AccountEnabled = false;
                                return map;
                            }
                            map.AccountEnabled = user[0].accountEnabled;
                            map.Username = user[0].userName;
                            map.DisplayName = user[0].displayName;
                            map.Email = user[0].GetEmail(email => email, () => "");
                            return map;
                        })
                        .ToArray();
                    return onSuccess(lookups);
                });
        }

        internal async Task<TResult> UpdatePasswordCredentialAsync<TResult>(Guid passwordCredentialId,
                string password, bool forceChange, DateTime? emailLastSent,
                EastFive.Api.SessionToken security, IAzureApplication application,
            Func<TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<TResult> onUnathorized,
            Func<TResult> onServiceNotAvailable,
            Func<string, TResult> onFailure)
        {
            if (!security.accountIdMaybe.HasValue)
                return onUnathorized();
            var resultUpdatePassword = await dataContext.PasswordCredentials.UpdatePasswordCredentialAsync(passwordCredentialId,
                async (actorId, loginId, emailLastSentCurrent, updateEmailLastSentAsync)  =>
                {
                    DiscriminatedDelegate<Guid, TResult, Task<TResult>> resultSuccess =
                        (success, fail) => success(loginId);
                    DiscriminatedDelegate<Guid, TResult, Task<TResult>> resultNotFound =
                        (success, fail) => fail(onNotFound());
                    var failureMessage = "";
                    DiscriminatedDelegate<Guid, TResult, Task<TResult>> resultFailure =
                        (success, fail) => fail(onFailure(failureMessage));

                    if (!await application.CanAdministerCredentialAsync(actorId, security))
                        return (success, fail) => fail(onUnathorized());

                    if (emailLastSent.HasValue &&
                        (!emailLastSentCurrent.HasValue ||
                          emailLastSent.Value > emailLastSentCurrent.Value))
                    {
                        var resultGetLogin = await await managmentProvider.GetAuthorizationAsync(loginId,
                            async (loginInfo) =>
                            {
                                var email = await Library.configurationManager.GetActorAdministrationEmailAsync(actorId, 
                                        security.accountIdMaybe.Value, security.claims,
                                    address => address,
                                    () => string.Empty,
                                    () => string.Empty,
                                    (why) => string.Empty);
                                if (email.IsNullOrWhiteSpace())
                                {
                                    email = loginInfo.GetEmail(
                                        address => address,
                                        () => string.Empty);
                                }
                                
                                if (string.IsNullOrWhiteSpace(email))
                                {
                                    failureMessage = "No email address found for user.";
                                    return resultFailure;
                                }

                                return await Web.Configuration.Settings.GetUri(EastFive.Security.SessionServer.Configuration.AppSettings.LandingPage,
                                    async (landingPage) =>
                                    {
                                        if (string.IsNullOrWhiteSpace(password))
                                            password = EastFive.Security.SecureGuid.Generate()
                                                .ToByteArray()
                                                .Base58Encode();
                                        // TODO: the purpose of the next line is to send the password. 
                                        // If we don't want it sent, don't update the last sent value!!!
                                        return await await SendInvitePasswordAsync(email, loginInfo.userName, password, landingPage,
                                            async () =>
                                            {
                                                await updateEmailLastSentAsync(emailLastSent.Value);
                                                return resultSuccess;
                                            },
                                            () =>
                                            {
                                                DiscriminatedDelegate<Guid, TResult, Task<TResult>> resultServiceUnavailable =
                                                (success, fail) => fail(onServiceNotAvailable());
                                                return resultServiceUnavailable.ToTask();
                                            },
                                            (why) =>
                                            {
                                                failureMessage = why;
                                                return resultFailure.ToTask();
                                            });
                                    },
                                    (why) =>
                                    {
                                        DiscriminatedDelegate<Guid, TResult, Task<TResult>> resultFailureConfig =
                                                (success, fail) => fail(onFailure(why));
                                        return resultFailureConfig.ToTask();
                                    });
                            },
                            () => resultNotFound.ToTask(),
                            (why) => resultFailure.ToTask(),
                            () => resultFailure.ToTask(),
                            (why) => resultFailure.ToTask());
                        return resultGetLogin;
                    }
                    return resultSuccess;
                },
                () =>
                {
                    DiscriminatedDelegate<Guid, TResult, Task<TResult>> result =
                           (success, fail) => fail(onNotFound());
                    return result;
                });
            return await resultUpdatePassword(
                async (loginId) =>
                {
                    if (string.IsNullOrWhiteSpace(password))
                        return onSuccess();
                    
                    return await managmentProvider.UpdateAuthorizationAsync(loginId, password, forceChange,
                        () => onSuccess(),
                        (why) => onFailure(why),
                        () => onServiceNotAvailable(),
                        (why) => onFailure(why));
                },
                (r) => r.ToTask());
        }

        public async Task<TResult> UpdateEmailAsync<TResult>(Guid actorId,
            string email,
            Guid performingActorId, System.Security.Claims.Claim[] claims,
            Func<TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<TResult> onUnathorized,
            Func<TResult> onServiceNotAvailable,
            Func<string, TResult> onFailure)
        {
            return await await dataContext.PasswordCredentials.FindPasswordCredentialByActorAsync(actorId,
                async (credentials) =>
                {
                    if (!await Library.configurationManager.CanAdministerCredentialAsync(
                        actorId, performingActorId, claims))
                        return onUnathorized();

                    if (string.IsNullOrWhiteSpace(email))
                        return onSuccess();

                    var reasons = await credentials
                        .Select(
                            c => managmentProvider.UpdateEmailAsync(c.loginId, email,
                                () => string.Empty,
                                (why) => why,
                                () => "service unavailable",
                                (why) => why))
                        .WhenAllAsync();
                    return reasons.Any(x => !string.IsNullOrEmpty(x)) ? onFailure(reasons.First()) : onSuccess();
                },
                onNotFound.AsAsyncFunc());
        }

        private Task<TResult> SendInvitePasswordAsync<TResult>(string emailAddress, string userName, string password, Uri loginUrl,
            Func<TResult> onSuccess,
            Func<TResult> onServiceNotAvailable,
            Func<string, TResult> onFailure)
        {
            return EastFive.Web.Configuration.Settings.GetString(EastFive.Security.SessionServer.Configuration.EmailTemplateDefinitions.InvitePassword,
                templateName => EastFive.Web.Configuration.Settings.GetString(EastFive.Security.SessionServer.Configuration.EmailTemplateDefinitions.InviteFromAddress,
                    fromAddress => EastFive.Web.Configuration.Settings.GetString(EastFive.Security.SessionServer.Configuration.EmailTemplateDefinitions.InviteFromName,
                        fromName => EastFive.Web.Configuration.Settings.GetString(EastFive.Security.SessionServer.Configuration.EmailTemplateDefinitions.InviteSubject,
                            subject => Web.Services.ServiceConfiguration.SendMessageService()
                                .SendEmailMessageAsync(templateName,
                                    emailAddress, string.Empty,
                                    fromAddress, fromName,  //"newaccounts@orderowl.com", "New Account Services"
                                    subject, //"New Order Owl Account"
                                    new Dictionary<string, string>()
                                    {
                                        { "login_link", loginUrl.AbsoluteUri },
                                        { "username",   userName },
                                        { "password",   password }
                                    },
                                    default(IDictionary<string, IDictionary<string, string>[]>),
                                    (sentCode) => onSuccess(),
                                    onServiceNotAvailable,
                                    onFailure),
                        onFailure.AsAsyncFunc()),
                    onFailure.AsAsyncFunc()),
                onFailure.AsAsyncFunc()),
            onFailure.AsAsyncFunc());
        }
        
        internal async Task<TResult> DeletePasswordCredentialAsync<TResult>(Guid passwordCredentialId,
                EastFive.Api.SessionToken security, IAzureApplication application,
            Func<TResult> success,
            Func<TResult> onUnauthorized,
            Func<TResult> notFound,
            Func<string, TResult> onServiceNotAvailable)
        {
            return await await this.dataContext.PasswordCredentials.DeletePasswordCredentialAsync(passwordCredentialId,
                async (loginId) =>
                {
                    if (!await application.CanAdministerCredentialAsync(loginId, security))
                        return onUnauthorized();
                    return await managmentProvider.DeleteAuthorizationAsync(loginId,
                        success,
                        onServiceNotAvailable,
                        () => onServiceNotAvailable("Not supported"), 
                        onServiceNotAvailable);
                },
                () => notFound().ToTask());
        }

        #endregion
    }
}
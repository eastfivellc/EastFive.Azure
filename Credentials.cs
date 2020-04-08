using System;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;

using BlackBarLabs.Extensions;
using System.Security.Claims;
using System.Security.Cryptography;

namespace EastFive.Security.SessionServer
{
    public struct Invite
    {
        public Guid id;
        public Guid actorId;
        public string email;
        internal bool isToken;
        internal DateTime? lastSent;
    }

    public class Credentials
    {
        private Context context;
        private Persistence.DataContext dataContext;

        internal Credentials(Context context, Persistence.DataContext dataContext)
        {
            this.dataContext = dataContext;
            this.context = context;
        }

        #region InviteCredential

        public async Task<TResult> CreateInviteCredentialAsync<TResult>(Guid sessionId, Guid? stateId,
            Guid? authorizationId, string method, string subject, string name,
            IDictionary<string, string> extraParams, 
            Func<Guid, string, string, IDictionary<string, string>, Task> saveAuthRequest,
            Uri redirectUrl,
            Func<Guid, Guid, string, string, AuthenticationActions, IDictionary<string, string>, Uri, TResult> onLogin,
            Func<string, TResult> onInvalidToken,
            Func<string, TResult> onNotConfigured,
            Func<string, TResult> onFailure)
        {
            if (!authorizationId.HasValue)
                return onFailure("The credential is corrupt");

            var authenticationId = authorizationId.Value;

            return await await dataContext.CredentialMappings.CreateCredentialMappingAsync(Guid.NewGuid(), method, subject,
                    authorizationId.Value,
                async () => await await context.Sessions.GenerateSessionWithClaimsAsync(sessionId, authenticationId,
                    async (token, refreshToken) =>
                    {
                        await saveAuthRequest(authenticationId, name, token, extraParams);
                        return onLogin(stateId.Value, authenticationId, token, refreshToken, AuthenticationActions.link, extraParams,
                            redirectUrl);
                    },
                    onNotConfigured.AsAsyncFunc()),
                "GUID not unique".AsFunctionException<Task<TResult>>(),
                () => onInvalidToken("Login is already mapped.").ToTask());
        }

        public async Task<TResult> SendEmailInviteAsync<TResult>(Guid inviteId, Guid actorId, string email,
                EastFive.Api.Azure.AzureApplication application, Guid performingActorId, System.Security.Claims.Claim[] claims,
                Func<Guid, Guid, Uri> getRedirectLink,
            Func<TResult> success,
            Func<TResult> inviteAlreadyExists,
            Func<TResult> onCredentialMappingDoesNotExists,
            Func<TResult> onUnauthorized,
            Func<TResult> onServiceNotAvailable,
            Func<string, TResult> onFailed)
        {
            if (!await Library.configurationManager.CanAdministerCredentialAsync(actorId, performingActorId, claims))
                return onUnauthorized();

            var token = SecureGuid.Generate();
            var loginId = Guid.NewGuid(); // This creates a "user" in the invite system
            var result = await await this.dataContext.CredentialMappings.CreateCredentialMappingAsync(inviteId,
                loginId, actorId, email, token, DateTime.UtcNow, false, false,
                async () =>
                {
                    var templateName = ConfigurationManager.AppSettings[Configuration.EmailTemplateDefinitions.InviteNewAccount];
                    if (string.IsNullOrEmpty(templateName))
                        return onFailed($"Email template setting not found.  Expected template value for key {Configuration.EmailTemplateDefinitions.InviteNewAccount}");
                    
                    var resultMail = await application.SendMessageService.SendEmailMessageAsync(templateName, 
                        email, string.Empty,
                        "newaccounts@orderowl.com", "New Account Services",
                        "New Order Owl Account",
                        new Dictionary<string, string>()
                        {
                            { "create_account_link", getRedirectLink(inviteId, token).AbsoluteUri }
                        },
                        null,
                        (sentCode) => success(),
                        () => onServiceNotAvailable(),
                        (why) => onFailed(why));
                    return resultMail;
                },
                () => inviteAlreadyExists().ToTask(),
                () => { throw new Exception("Token generated was not unique"); },
                () => { throw new Exception("Login Id generated was not unique"); });
            return result;
        }
        
        internal Task<TResult> GetInviteAsync<TResult>(Guid inviteId,
            Func<Invite, TResult> success,
            Func<TResult> notFound)
        {
            return this.dataContext.CredentialMappings.FindInviteAsync(inviteId, false,
                (actorId, email, lastSent) =>
                {
                    return success(new Invite
                    {
                        id = inviteId,
                        actorId = actorId,
                        email = email,
                        lastSent = lastSent,
                    });
                },
                () => notFound());
        }

        internal Task<TResult> GetInviteByTokenAsync<TResult>(Guid token,
            Func<Guid, TResult> redirect,
            Func<Guid, IDictionary<string, string>, TResult> success,
            Func<TResult> onAlreadyUsed,
            Func<TResult> notFound)
        {
            return this.dataContext.CredentialMappings.FindInviteByTokenAsync(token,
                (inviteId, actorId, loginId) =>
                {
                    if (loginId.HasValue)
                        return success(actorId, new Dictionary<string, string>());
                    
                    return redirect(token);
                },
                () => notFound());
        }

        internal Task<TResult> GetInvitesByActorAsync<TResult>(Guid actorId,
            Func<Invite[], TResult> success,
            Func<TResult> notFound)
        {
            return this.dataContext.CredentialMappings.FindInviteByActorAsync(actorId, false,
                (invites) =>
                {
                    return success(invites);
                },
                () => notFound());
        }

        #endregion
        

        public async Task<TResult> CreateSamlCredentialAsync<TResult>(Guid samlCredentialId,
            Guid actorId, string nameId,
            Guid performingActorId, System.Security.Claims.Claim[] claims,
            Func<TResult> onSuccess,
            Func<TResult> onCredentialAlreadyExist,
            Func<TResult> onNameIdAlreadyInUse,
            Func<TResult> onRelationshipAlreadyExist,
            Func<TResult> onUnauthorized,
            Func<string, TResult> onFailure)
        {
            // TODO: Verify that the logged in user is the admin
            
            // TODO: Check other error conditions
            var result = await this.CreateSamlCredentialAsync(samlCredentialId, actorId, nameId,
                onSuccess, onCredentialAlreadyExist, (fetchActorIdAsync) => onNameIdAlreadyInUse(), onRelationshipAlreadyExist, onFailure);
            return result;
        }

        public Task<TResult> CreateSamlCredentialAsync<TResult>(Guid samlCredentialId,
            Guid actorId, string nameId,
            Func<TResult> onSuccess,
            Func<TResult> onCredentialAlreadyExist,
            Func<Func<Task<TResult>>, TResult> onLoginIdAlreadyInUse,
            Func<TResult> onRelationshipAlreadyExist,
            Func<string, TResult> onFailure)
        {
            return CreateSamlCredentialInnerAsync(samlCredentialId, actorId, nameId, false, onSuccess, onCredentialAlreadyExist, onLoginIdAlreadyInUse, onRelationshipAlreadyExist, onFailure);
        }

        private struct CSCIResult<TResult>
        {
            internal TResult result;
            internal bool rerun;
        }

        public async Task<TResult> CreateSamlCredentialInnerAsync<TResult>(Guid samlCredentialId,
            Guid actorId, string nameId, bool overrideLogin,
            Func<TResult> onSuccess,
            Func<TResult> onCredentialAlreadyExist,
            Func<Func<Task<TResult>>, TResult> onLoginIdAlreadyInUse,
            Func<TResult> onRelationshipAlreadyExist,
            Func<string, TResult> onFailure)
        {
            // TODO: Verify that the logged in user is the admin
            var tokenBytes = SHA512.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(nameId));
            var loginId = new Guid(tokenBytes.Take(16).ToArray());
            var token = Guid.NewGuid(); // This creates a "user" in the "Token system"

            // TODO: Check other error conditions
            var result = await this.dataContext.CredentialMappings.CreateCredentialMappingAsync(samlCredentialId,
                loginId, actorId, nameId, token, DateTime.UtcNow, true, overrideLogin,
                () => new CSCIResult<TResult> { result = onSuccess(), rerun = false },
                () => new CSCIResult<TResult> { result = onCredentialAlreadyExist(), rerun = false },
                () => { throw new Exception("Token generated was not unique"); },
                () => new CSCIResult<TResult> { rerun = true });
            if(result.rerun)
                return onLoginIdAlreadyInUse(
                        () => CreateSamlCredentialInnerAsync(samlCredentialId, actorId, nameId, true,
                            onSuccess,
                            onCredentialAlreadyExist,
                            (callback) => onFailure("Override of loginID failed"),
                            onRelationshipAlreadyExist,
                            onFailure));
            return result.result;
        }

        #region Tokens

        public async Task<TResult> CreateTokenCredentialAsync<TResult>(Guid inviteId, Guid actorId, string email,
                Guid loggedInActorId, System.Security.Claims.Claim[] claim,
                Func<Guid, Guid, Uri> getRedirectLink,
            Func<TResult> onSuccess,
            Func<TResult> onInviteAlreadyExists,
            Func<string, TResult> onServiceNotAvailable,
            Func<string, TResult> onFailed)
        {
            // TODO: Verify that the logged in user is the admin
            var token = EastFive.Security.SecureGuid.Generate();
            var loginId = Guid.NewGuid(); // This creates a "user" in the "Token system"
            var result = await await this.dataContext.CredentialMappings.CreateCredentialMappingAsync(inviteId,
                loginId, actorId, email, token, DateTime.UtcNow, true, false,
                async () =>
                {
                    return await LoadConfiguration(
                        async (templateName, fromEmail, fromName, subject) =>
                        {
                            var mailService = Web.Services.ServiceConfiguration.SendMessageService();
                            var resultMail = await mailService.SendEmailMessageAsync(templateName,
                                email, string.Empty,
                                fromEmail, fromName,
                                subject,
                                new Dictionary<string, string>()
                                {
                                    { "token_login_link", getRedirectLink(inviteId, token).AbsoluteUri }
                                },
                                default(IDictionary<string, IDictionary<string, string>[]>),
                                (sentCode) => onSuccess(),
                                () => onServiceNotAvailable("Email system s offline"),
                                (why) => onFailed(why));
                            return resultMail;
                        },
                        (why) => onFailed(why).ToTask());
                },
                () => onInviteAlreadyExists().ToTask(),
                () => { throw new Exception("Token generated was not unique"); },
                () => { throw new Exception("Login generated was not unique"); });
            return result;
        }

        private static TResult LoadConfiguration<TResult>(
            Func<string, string, string, string, TResult> onLoaded,
            Func<string, TResult> onFailed)
        {
            return Web.Configuration.Settings.GetString(Configuration.EmailTemplateDefinitions.LoginToken,
                        (templateName) =>
                            Web.Configuration.Settings.GetString(Configuration.AppSettings.TokenCredential.FromEmail,
                                (fromEmail) =>
                                    Web.Configuration.Settings.GetString(Configuration.AppSettings.TokenCredential.FromName,
                                        (fromName) =>
                                            Web.Configuration.Settings.GetString(Configuration.AppSettings.TokenCredential.Subject,
                                                (subject) => onLoaded(templateName, fromEmail, fromName, subject),
                                                onFailed),
                                        onFailed),
                                onFailed),
                        onFailed);
        }

        internal async Task<TResult> UpdateTokenCredentialAsync<TResult>(Guid tokenCredentialId,
                string email, DateTime? lastSent,
                System.Security.Claims.Claim[] claim,
                Func<Guid, Guid, Uri> getRedirectLink,
            Func<TResult> onSuccess,
            Func<TResult> onNoChange,
            Func<TResult> onNotFound,
            Func<TResult> onServiceNotAvailable,
            Func<string, TResult> onFailure)
        {
            var result = await this.dataContext.CredentialMappings.UpdateTokenCredentialAsync(tokenCredentialId,
                async (emailCurrent, lastSentCurrent, token, saveAsync) =>
                {
                    if (
                        (lastSent.HasValue && lastSentCurrent.HasValue && lastSent.Value > lastSentCurrent.Value) ||
                        (lastSent.HasValue && (!lastSentCurrent.HasValue)) ||
                        String.Compare(emailCurrent, email) != 0)
                    {
                        var templateName = ConfigurationManager.AppSettings[Configuration.EmailTemplateDefinitions.LoginToken];
                        if (string.IsNullOrEmpty(templateName))
                            return onFailure($"Email template setting not found.  Expected template value for key {Configuration.EmailTemplateDefinitions.LoginToken}");

                        if (String.IsNullOrWhiteSpace(email))
                            email = emailCurrent;
                        var mailService = Web.Services.ServiceConfiguration.SendMessageService();
                        var resultMail = await await mailService.SendEmailMessageAsync(templateName,
                            email, string.Empty,
                            "newaccounts@orderowl.com", "New Account Services",
                            "New Order Owl Account",
                            new Dictionary<string, string>()
                            {
                                { "token_login_link", getRedirectLink(tokenCredentialId, token).AbsoluteUri }
                            },
                            default(IDictionary<string, IDictionary<string, string>[]>),
                            async (sentCode) =>
                            {
                                if (!lastSent.HasValue)
                                    lastSent = DateTime.UtcNow;
                                await saveAsync(email, lastSent);
                                return onSuccess();
                            },
                            () => onServiceNotAvailable().ToTask(),
                            (why) => onFailure(why).ToTask());
                        return resultMail;
                    }
                    return onNoChange();
                },
                () => onNotFound());
            return result;
        }

        internal Task<TResult> DeleteByIdAsync<TResult>(Guid inviteId,
            Guid performingActorId, System.Security.Claims.Claim [] claims,
            Func<TResult> onSuccess, 
            Func<TResult> onNotFound,
            Func<TResult> onUnathorized)
        {
            return this.dataContext.CredentialMappings.DeleteInviteCredentialAsync(inviteId,
                async (current, deleteAsync) =>
                {
                    if (!await Library.configurationManager.CanAdministerCredentialAsync(
                        current.actorId, performingActorId, claims))
                        return onUnathorized();

                    await deleteAsync();
                    return onSuccess();
                },
                onNotFound);
        }

        internal Task<TResult> GetTokenCredentialAsync<TResult>(Guid inviteId,
            Func<Invite, TResult> success,
            Func<TResult> notFound)
        {
            return this.dataContext.CredentialMappings.FindInviteAsync(inviteId, true,
                (actorId, email, lastSent) =>
                {
                    return success(new Invite
                    {
                        id = inviteId,
                        actorId = actorId,
                        email = email,
                        lastSent = lastSent,
                        isToken = true,
                    });
                },
                () => notFound());
        }

        internal async Task<TResult> GetTokenCredentialByTokenAsync<TResult>(Guid token,
            Func<Guid, Guid, string, string, TResult> success,
            Func<TResult> notFound,
            Func<string, TResult> onConfFailure)
        {
            return await await this.dataContext.CredentialMappings.FindTokenCredentialByTokenAsync(token,
                async (inviteId, actorId, loginId) =>
                {
                    var sessionId = Guid.NewGuid();
                    var result = await this.context.Sessions.CreateAsync(sessionId, actorId,
                        new System.Security.Claims.Claim[] { },
                        (jwtToken, refreshToken) =>
                        {
                            return success(sessionId, actorId, jwtToken, refreshToken);
                        },
                        () => default(TResult),
                        (why) => onConfFailure(why)); // Should only happen if generated Guid is not unique ;-O
                    return result;
                },
                () => notFound().ToTask());
        }

        internal Task<TResult> GetTokenCredentialByActorAsync<TResult>(Guid actorId,
            Func<Invite[], TResult> success,
            Func<TResult> notFound)
        {
            return this.dataContext.CredentialMappings.FindInviteByActorAsync(actorId, true,
                (invites) =>
                {
                    return success(invites);
                },
                () => notFound());
        }

        internal Task<TResult> DeleteTokenByIdAsync<TResult>(Guid inviteId,
            Func<TResult> onFound,
            Func<TResult> onNotFound)
        {
            return this.dataContext.CredentialMappings.DeleteInviteCredentialAsync(inviteId,
                async (invite, delete) =>
                {
                    await delete();
                    return onFound();
                },
                onNotFound);
        }


        #endregion

    }
}

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Extensions;

namespace EastFive.Security.SessionServer.Persistence
{
    public struct AuthenticationRequest
    {
        public Guid id;
        public string method;
        public string name;
        public AuthenticationActions action;
        public Guid? authorizationId;
        public IDictionary<string, string> extraParams;
        public string token;
        public Uri redirect;
        public Uri redirectLogout;
        internal DateTime? Deleted;
    }

    public class AuthenticationRequests
    {
        private AzureStorageRepository repository;
        private DataContext context;

        public AuthenticationRequests(DataContext context, AzureStorageRepository repository)
        {
            this.repository = repository;
            this.context = context;
        }

        public async Task<TResult> CreateAsync<TResult>(Guid authenticationRequestId,
                string method, AuthenticationActions action,
                Uri redirectUrl, Uri redirectLogoutUrl,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists)
        {
            var doc = new Documents.AuthenticationRequestDocument
            {
                Method = method,
                Action = Enum.GetName(typeof(AuthenticationActions), action),
                RedirectUrl = redirectUrl.IsDefault()?
                    default(string)
                    :
                    redirectUrl.AbsoluteUri,
                RedirectLogoutUrl = redirectLogoutUrl.IsDefault() ?
                    default(string)
                    :
                    redirectLogoutUrl.AbsoluteUri,
            };
            return await this.repository.CreateAsync(authenticationRequestId, doc,
                () => onSuccess(),
                () => onAlreadyExists());
        }

        public async Task<TResult> CreateAsync<TResult>(Guid authenticationRequestId,
                string method, AuthenticationActions action,
                Guid actorLinkId, string token, Uri redirectUrl, Uri redirectLogoutUrl,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists)
        {
            var doc = new Documents.AuthenticationRequestDocument
            {
                Method = method,
                Action = Enum.GetName(typeof(AuthenticationActions), action),
                LinkedAuthenticationId = actorLinkId,
                Token = token,
                RedirectUrl = redirectUrl.IsDefault() ?
                    default(string)
                    :
                    redirectUrl.AbsoluteUri,
                RedirectLogoutUrl = redirectLogoutUrl.IsDefault() ?
                    default(string)
                    :
                    redirectLogoutUrl.AbsoluteUri,
            };
            return await this.repository.CreateAsync(authenticationRequestId, doc,
                () => onSuccess(),
                () => onAlreadyExists());
        }

        public async Task<TResult> FindByIdAsync<TResult>(Guid authenticationRequestId,
            Func<AuthenticationRequest, TResult> onSuccess,
            Func<TResult> onNotFound)
        {
            return await repository.FindByIdAsync(authenticationRequestId,
                (Documents.AuthenticationRequestDocument document) =>
                    onSuccess(Convert(document)),
                () => onNotFound());
        }

        public async Task<TResult> UpdateAsync<TResult>(Guid authenticationRequestId,
            Func<AuthenticationRequest, Func<Guid, string, string, IDictionary<string, string>, Task>, Task<TResult>> onFound,
            Func<TResult> onNotFound)
        {
            return await this.repository.UpdateAsync<Documents.AuthenticationRequestDocument, TResult>(authenticationRequestId,
                async (document, saveAsync) =>
                {
                    return await onFound(Convert(document),
                        async (linkedAuthenticationId, name, token, extraParams) =>
                        {
                            document.LinkedAuthenticationId = linkedAuthenticationId;
                            document.Name = name;
                            document.Token = token;
                            document.SetExtraParams(extraParams);
                            await saveAsync(document);
                        });
                },
                () =>
                {
                    return onNotFound();
                });
        }
    

        internal static AuthenticationRequest Convert(Documents.AuthenticationRequestDocument document)
        {
            return new AuthenticationRequest
            {
                id = document.Id,
                method = document.Method,
                name = document.Name,
                action = (AuthenticationActions)Enum.Parse(typeof(AuthenticationActions), document.Action, true),
                authorizationId = document.LinkedAuthenticationId,
                token = document.Token,
                extraParams = document.GetExtraParams(),
                redirect = document.RedirectUrl.IsNullOrWhiteSpace()?
                    default(Uri)
                    :
                    new Uri(document.RedirectUrl),
                redirectLogout = document.RedirectLogoutUrl.IsNullOrWhiteSpace() ?
                    default(Uri)
                    :
                    new Uri(document.RedirectLogoutUrl),
                Deleted = document.Deleted,
            };
        }
    }
}
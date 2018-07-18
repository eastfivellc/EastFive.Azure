﻿using BlackBarLabs.Api.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Routing;
using System.Security.Claims;
using System.Net.Http;

namespace EastFive.Security.SessionServer
{
    public interface IConfigureIdentityServer
    {
        WebId GetActorLink(Guid actorId, UrlHelper urlHelper);

        Task<bool> CanAdministerCredentialAsync(Guid actorInQuestion, Guid actorTakingAction, System.Security.Claims.Claim[] claims);

        Task<TResult> GetRedirectUriAsync<TResult>(Context context, string validationType,
                AuthenticationActions action,
                Guid requestId,
                Guid? authorizationId,
                string token, string refreshToken,
                IDictionary<string, string> authParams,
                Uri redirectUrl,
            Func<Uri, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure);

        Task<TResult> CanActAsUsersAsync<TResult>(Guid actorTakingAction, System.Security.Claims.Claim[] claims, Func<TResult> canActAsUsers, Func<TResult> deny);

        Task<TResult> RemoveIntegrationAsync<TResult>(Session session, HttpRequestMessage request,
            Func<HttpResponseMessage, TResult> onSuccess,
            Func<TResult> onFailure);

        Task<TResult> GetActorAdministrationEmailAsync<TResult>(Guid actorId, Guid performingActorId, IEnumerable<System.Security.Claims.Claim> claims,
            Func<string, TResult> onSuccess,
            Func<TResult> onActorNotFound,
            Func<TResult> onNoEmail,
            Func<string, TResult> onFailure);
    }
}

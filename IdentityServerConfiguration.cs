using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Routing;
using BlackBarLabs.Api.Resources;
using BlackBarLabs;
using BlackBarLabs.Extensions;
using BlackBarLabs.Api;
using System.Security.Claims;
using System.Net.Http;
using System.Net;
using EastFive.Extensions;
using EastFive.Api;

namespace EastFive.Security.SessionServer
{
    public class IdentityServerConfiguration<TActorController> : IConfigureIdentityServer
    {
        public const string parameterAuthorizationId = "authorizationId";
        public const string parameterToken = "token";
        public const string parameterRefreshToken = "refreshToken";

        public virtual Task<bool> CanAdministerCredentialAsync(Guid actorInQuestion, Guid actorTakingAction, System.Security.Claims.Claim[] claims)
        {
            if (actorInQuestion == actorTakingAction)
                return true.ToTask();

            if (IsSuperAdmin(actorTakingAction))
                return true.ToTask();

            return false.ToTask();
        }

        private bool IsSuperAdmin(Guid actorTakingAction)
        {
            return EastFive.Web.Configuration.Settings.GetGuid(
                EastFive.Api.AppSettings.ActorIdSuperAdmin,
                (actorIdSuperAdmin) =>
                {
                    if (actorIdSuperAdmin == actorTakingAction)
                        return true;

                    return false;
                },
                (why) => false);
        }

        public virtual WebId GetActorLink(Guid actorId, UrlHelper urlHelper)
        {
            return urlHelper.GetWebId<TActorController>(actorId);
        }

        public virtual async Task<TResult> GetActorNameDetailsAsync<TResult>(Guid actorId,
            Func<string, string, string, TResult> onActorFound,
            Func<TResult> onActorNotFound)
        {
            return onActorNotFound();
        }

        public virtual async Task<TResult> GetRedirectUriAsync<TResult>(Context context,
                string validationType,
                AuthenticationActions action,
                Guid requestId,
                Guid? authorizationId,
                string token, string refreshToken,
                IDictionary<string, string> authParams,
                Uri redirectUriFromPost,
            Func<Uri, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure)
        {
            if (!redirectUriFromPost.IsDefault())
            {
                var redirectUrl = SetRedirectParameters(redirectUriFromPost, requestId, authorizationId, token, refreshToken);
                return onSuccess(redirectUrl);
            }

            if(null != authParams && authParams.ContainsKey(Configuration.AuthorizationParameters.RedirectUri))
            {
                Uri redirectUri;
                var redirectUriString = authParams[Configuration.AuthorizationParameters.RedirectUri];
                if (!Uri.TryCreate(redirectUriString, UriKind.Absolute, out redirectUri))
                    return onInvalidParameter("REDIRECT", $"BAD URL in redirect call:{redirectUriString}");
                var redirectUrl = SetRedirectParameters(redirectUri, requestId, authorizationId, token, refreshToken);
                return onSuccess(redirectUrl);
            }

            return await EastFive.Web.Configuration.Settings.GetUri(
                EastFive.Security.SessionServer.Configuration.AppSettings.LandingPage,
                (redirectUri) =>
                {
                    var redirectUrl = SetRedirectParameters(redirectUri, requestId, authorizationId, token, refreshToken);
                    return onSuccess(redirectUrl);
                },
                (why) => onFailure(why)).ToTask();
        }

        protected Uri SetRedirectParameters(Uri redirectUri, Guid requestId, Guid? authorizationId, string token, string refreshToken)
        {
            var redirectUrl = redirectUri
                //.SetQueryParam(parameterAuthorizationId, authorizationId.Value.ToString("N"))
                //.SetQueryParam(parameterToken, token)
                //.SetQueryParam(parameterRefreshToken, refreshToken)
                .SetQueryParam(EastFive.Api.Azure.AzureApplication.QueryRequestIdentfier, requestId.ToString());
            return redirectUrl;
        }

        public virtual Task<TResult> CanActAsUsersAsync<TResult>(Guid actorTakingAction, System.Security.Claims.Claim[] claims, Func<TResult> canActAsUsers, Func<TResult> deny)
        {
            if (IsSuperAdmin(actorTakingAction))
                return canActAsUsers().ToTask();

            return deny().ToTask();
        }

        public virtual Task<TResult> RemoveIntegrationAsync<TResult>(Session integration, IHttpRequest request,
            Func<IHttpResponse, TResult> onSuccess,
            Func<TResult> onFailure)
        {
            return onSuccess(request.CreateResponse(HttpStatusCode.NoContent)).ToTask();
        }

        public virtual Task<TResult> GetActorAdministrationEmailAsync<TResult>(Guid actorId, Guid performingActorId, IEnumerable<System.Security.Claims.Claim> claims, Func<string, TResult> onSuccess, Func<TResult> onActorNotFound, Func<TResult> onNoEmail, Func<string, TResult> onFailure)
        {
            return onNoEmail().ToTask();
        }
    }
}
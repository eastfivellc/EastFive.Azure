using System;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Http;
using System.Net.Http;
using System.Threading;

using BlackBarLabs.Api;
using BlackBarLabs.Extensions;
using Microsoft.AspNetCore.Mvc.Routing;
using BlackBarLabs;
using EastFive.Api.Services;
using System.Configuration;
using System.Collections.Generic;
using EastFive.Api.Azure.Credentials;
using EastFive.Api.Azure.Credentials.Controllers;
using EastFive.Security.SessionServer;

namespace EastFive.Api.Azure.Credentials
{
    public static class TokenCredentialActions
    {
        #region Queries
        
        public static async Task<HttpResponseMessage> QueryAsync(this Resources.Queries.TokenCredentialQuery query,
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            return await query.ParseAsync(request,
                q => QueryByIdAsync(q.Id.ParamSingle(), request, urlHelper),
                q => QueryByTokenAsync(q.Token.ParamSingle(), request, urlHelper),
                q => QueryByActorAsync(q.Actor.ParamSingle(), request, urlHelper));
        }

        private static async Task<HttpResponseMessage> QueryByIdAsync(Guid inviteId, 
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            var context = request.GetSessionServerContext();
            return await context.Credentials.GetTokenCredentialAsync(inviteId,
                (invite) =>
                {
                    var response = request.CreateResponse(HttpStatusCode.OK, Convert(invite, urlHelper));
                    return response;
                },
                () => request.CreateResponse(HttpStatusCode.NotFound));
        }

        private static async Task<HttpResponseMessage> QueryByTokenAsync(Guid token, 
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            var context = request.GetSessionServerContext();
            return await await context.Credentials.GetTokenCredentialByTokenAsync(token,
                (sessionId, actorId, jwtToken, refreshToken) =>
                {
                    var redirectResponseMessage = Library.configurationManager.GetRedirectUriAsync(context,
                        Enum.GetName(typeof(CredentialValidationMethodTypes), CredentialValidationMethodTypes.Token), AuthenticationActions.signin,
                        sessionId, actorId, jwtToken, refreshToken, new Dictionary<string, string>(), default(Uri),
                        (redirectUrl) =>
                        {
                            var response = request.CreateResponse(HttpStatusCode.Redirect);
                            response.Headers.Location = redirectUrl;
                            return response;
                        },
                        (paramName, reason) => request.CreateResponse(HttpStatusCode.Conflict).AddReason($"Parameter[{paramName}]:{reason}"),
                        (why) => request.CreateResponse(HttpStatusCode.Conflict).AddReason(why));
                    return redirectResponseMessage;
                    //var landingPage = Web.Configuration.Settings.Get(SessionServer.Configuration.AppSettings.LandingPage);
                    //var redirectUrl = new Uri(landingPage)
                    //    .SetQueryParam("sessionId", sessionId.ToString("N"))
                    //    .SetQueryParam("actorId", actorId.ToString("N"))
                    //    .SetQueryParam("token", jwtToken)
                    //    .SetQueryParam("refreshToken", refreshToken);
                },
                () => request.CreateResponse(HttpStatusCode.NotFound).ToTask(),
                (why) => request.CreateResponse(HttpStatusCode.InternalServerError).AddReason(why).ToTask());
        }

        private static async Task<HttpResponseMessage[]> QueryByActorAsync(Guid actorId, HttpRequestMessage request, UrlHelper urlHelper)
        {
            var context = request.GetSessionServerContext();
            return await context.Credentials.GetTokenCredentialByActorAsync(actorId,
                (invites) =>
                {
                    var responses = invites
                        .Select(invite =>
                            request.CreateResponse(HttpStatusCode.OK, Convert(invite, urlHelper)))
                        .ToArray();
                    return responses;
                },
                () => request.CreateResponse(HttpStatusCode.NotFound).AsEnumerable().ToArray());
        }

        private static Resources.TokenCredential Convert(Invite invite, UrlHelper urlHelper)
        {
            return new Resources.TokenCredential
            {
                //Id = urlHelper.GetWebId<InviteCredentialController>(invite.id),
                Actor = Library.configurationManager.GetActorLink(invite.actorId, urlHelper),
                Email = invite.email,
                LastEmailSent = invite.lastSent,
            };
        }

        #endregion

        //public static async Task<HttpResponseMessage> CreateAsync(this Resources.TokenCredential credential,
        //    HttpRequestMessage request, UrlHelper url)
        //{
        //    return await request.GetActorIdClaimsAsync(
        //        async (loggedInActorId, claims) =>
        //        {
        //            var credentialId = credential.Id.ToGuid();
        //            if (!credentialId.HasValue)
        //                return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("Credential property (an ID) must be specified");
        //            var actorId = credential.Actor.ToGuid();
        //            if (!actorId.HasValue)
        //                return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("Actor property (an ID) must be specified");
        //            var context = request.GetSessionServerContext();
        //            var creationResults = await context.Credentials.CreateTokenCredentialAsync(
        //                    credentialId.Value, actorId.Value, credential.Email,
        //                    loggedInActorId, claims,
        //                    (inviteId, token) => url.GetLocation<TokenCredentialController>().SetQueryParam("token", token.ToString("N")),
        //                () => request.CreateResponse(HttpStatusCode.Created),
        //                () => request.CreateResponse(HttpStatusCode.Conflict)
        //                    .AddReason($"TokenCredential resource with ID [{credentialId.Value}] already exists"),
        //                (why) => request.CreateResponse(HttpStatusCode.ServiceUnavailable)
        //                    .AddReason(why),
        //                (why) => request.CreateResponse(HttpStatusCode.Conflict)
        //                    .AddReason(why));
        //            return creationResults;
        //        });
        //}
        
        public static async Task<HttpResponseMessage> UpdateAsync(this Resources.TokenCredential credential,
            HttpRequestMessage request, UrlHelper url)
        {
            var actorId = credential.Actor.ToGuid();
            //return await request.GetClaims(
            //    async (claims) =>
            //    {
            var claims = new System.Security.Claims.Claim[] { };
            var context = request.GetSessionServerContext();
            var creationResults = await context.Credentials.UpdateTokenCredentialAsync(
                credential.Id.UUID, credential.Email, credential.LastEmailSent,
                claims.ToArray(),
                (inviteId, token) => new Uri("http://example.com/todo")
                    .SetQueryParam("token", token.ToString("N")),
                () => request.CreateResponse(HttpStatusCode.Accepted),
                () => request.CreateResponse(HttpStatusCode.NotModified),
                () => request.CreateResponse(HttpStatusCode.NotFound)
                    .AddReason($"TokenCredential not found"),
                () => request.CreateResponse(HttpStatusCode.ServiceUnavailable),
                (why) => request.CreateResponse(HttpStatusCode.Conflict)
                    .AddReason(why));
            return creationResults;
            //},
            //() => request.CreateResponse(HttpStatusCode.Unauthorized).ToTask(),
            //(why) => request.CreateResponse(HttpStatusCode.InternalServerError).AddReason(why).ToTask());
        }

        public static async Task<HttpResponseMessage> DeleteAsync(this Resources.Queries.TokenCredentialQuery query,
           HttpRequestMessage request, UrlHelper urlHelper)
        {
            return await query.ParseAsync(request,
                q => DeleteByIdAsync(q.Id.ParamSingle(), request, urlHelper));
        }

        private static async Task<HttpResponseMessage> DeleteByIdAsync(Guid inviteId, HttpRequestMessage request, UrlHelper urlHelper)
        {
            var context = request.GetSessionServerContext();
            return await context.Credentials.DeleteTokenByIdAsync(inviteId,
                () =>
                {
                    var response = request.CreateResponse(HttpStatusCode.NoContent);
                    return response;
                },
                () => request.CreateResponse(HttpStatusCode.NotFound));
        }
    }
}

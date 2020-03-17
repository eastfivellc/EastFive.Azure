using System;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Http;
using System.Net.Http;
using System.Threading;

using Microsoft.AspNetCore.Mvc.Routing;

using BlackBarLabs.Api;
using BlackBarLabs.Extensions;
using EastFive.Api.Services;
using EastFive.Security.SessionServer.Configuration;
using EastFive.Api.Azure.Credentials.Resources.Queries;
using EastFive.Security.SessionServer;
using EastFive.Security.SessionServer.Api.Resources;

namespace EastFive.Api.Azure.Credentials
{
    public static class InviteCredentialActions
    {
        #region Queries
        
        public static async Task<HttpResponseMessage> QueryAsync(this InviteCredentialQuery query,
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            return await query.ParseAsync(request,
                q => QueryByIdAsync(q.Id.ParamSingle(), request, urlHelper),
                q => QueryByActorAsync(q.Actor.ParamSingle(), request, urlHelper));
        }

        private static async Task<HttpResponseMessage> QueryByIdAsync(Guid inviteId, HttpRequestMessage request, UrlHelper urlHelper)
        {
            var context = request.GetSessionServerContext();
            return await context.Credentials.GetInviteAsync(inviteId,
                (invite) =>
                {
                    var response = request.CreateResponse(HttpStatusCode.OK, Convert(invite, urlHelper));
                    return response;
                },
                () => request.CreateResponse(HttpStatusCode.NotFound));
        }

        private static async Task<HttpResponseMessage[]> QueryByActorAsync(Guid actorId, HttpRequestMessage request, UrlHelper urlHelper)
        {
            var context = request.GetSessionServerContext();
            return await context.Credentials.GetInvitesByActorAsync(actorId,
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

        private static Resources.InviteCredential Convert(Invite invite, UrlHelper urlHelper)
        {
            return new Resources.InviteCredential
            {
                //Id = urlHelper.GetWebId<Controllers.InviteCredentialController>(invite.id),
                Actor = Library.configurationManager.GetActorLink(invite.actorId, urlHelper),
                Email = invite.email,
                LastEmailSent = invite.lastSent,
            };
        }

        #endregion

        public static async Task<HttpResponseMessage> CreateAsync(this Resources.InviteCredential credential,
            HttpRequestMessage request, UrlHelper url)
        {
            var actorId = credential.Actor.ToGuid();
            if (!actorId.HasValue)
                return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("Actor value must be set");

            return await request.GetActorIdClaimsAsync(ClaimsDefinitions.AccountIdClaimType,
                async (performingActorId, claims) =>
                {
                    var context = request.GetSessionServerContext();
                    var creationResults = await context.Credentials.SendEmailInviteAsync(
                            credential.Id.UUID, actorId.Value, credential.Email,
                            // TODO: Pass in application instead of null
                            null, performingActorId, claims.ToArray(),
                        (inviteId, token) => new Uri("http://example.com/todo")
                            .SetQueryParam("token", token.ToString("N")),
                        () => request.CreateResponse(HttpStatusCode.Created),
                        () => request.CreateResponse(HttpStatusCode.Conflict)
                            .AddReason($"Invite already exists"),
                        () => request.CreateResponse(HttpStatusCode.Conflict)
                            .AddReason($"Credential mapping not found"),
                        () => request.CreateResponse(HttpStatusCode.Unauthorized),
                        () => request.CreateResponse(HttpStatusCode.ServiceUnavailable),
                        (why) => request.CreateResponse(HttpStatusCode.Conflict)
                            .AddReason(why));
                    return creationResults;
                });
        }

        public static async Task<HttpResponseMessage> DeleteAsync(this Resources.Queries.InviteCredentialQuery query,
           HttpRequestMessage request, UrlHelper urlHelper)
        {
            return await request.GetActorIdClaimsAsync(ClaimsDefinitions.AccountIdClaimType,
                async (performingActorId, claims) => await query.ParseAsync(request,
                    q => DeleteByIdAsync(q.Id.ParamSingle(), request, urlHelper, performingActorId, claims)));
        }

        private static async Task<HttpResponseMessage> DeleteByIdAsync(Guid inviteId,
            HttpRequestMessage request, UrlHelper urlHelper,
            Guid performingActorId, System.Security.Claims.Claim [] claims)
        {
            var context = request.GetSessionServerContext();
            return await context.Credentials.DeleteByIdAsync(inviteId, performingActorId, claims,
                () =>
                {
                    var response = request.CreateResponse(HttpStatusCode.NoContent);
                    return response;
                },
                () => request.CreateResponse(HttpStatusCode.NotFound),
                () => request.CreateResponse(HttpStatusCode.Unauthorized));
        }
    }
}

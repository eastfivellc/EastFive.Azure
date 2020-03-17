using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Configuration;

using BlackBarLabs.Api;
using EastFive.Api;

namespace EastFive.Security.SessionServer.Api
{
    public static class SamlCredentialActions
    {
        public static async Task<HttpResponseMessage> CreateAsync(this Resources.SamlCredential credential,
            HttpRequestMessage request, UrlHelper url)
        {
            return await request.GetActorIdClaimsAsync(
                async (performingActorId, claims) =>
                {
                    var response = await CreateSamlCredentialAsync(credential, request, url, performingActorId, claims);
                    return response;
                });
        }

        private static async Task<HttpResponseMessage> CreateSamlCredentialAsync(Resources.SamlCredential credential,
            HttpRequestMessage request, UrlHelper url,
            Guid performingActorId, System.Security.Claims.Claim[]claims)
        {
            var credentialId = credential.Id.ToGuid();
            if (!credentialId.HasValue)
                return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("ID property is not set");
            var actorId = credential.Actor.ToGuid();
            if (!actorId.HasValue)
                return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("Actor (a property) is not set");
            var userId = credential.UserId;
            if (String.IsNullOrWhiteSpace(userId))
                return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("User (a property) is not set");

            var context = request.GetSessionServerContext();
            var creationResults = await context.Credentials.CreateSamlCredentialAsync(
                credentialId.Value, actorId.Value, userId,
                performingActorId, claims,
                () => request.CreateResponse(HttpStatusCode.Created),
                () => request.CreateResponse(HttpStatusCode.Conflict)
                    .AddReason($"Credential already exists"),
                () => request.CreateResponse(HttpStatusCode.Conflict)
                    .AddReason($"Username already in use"),
                () => request.CreateResponse(HttpStatusCode.Conflict)
                    .AddReason($"Relationship already exists"),
                () => request.CreateResponse(HttpStatusCode.Unauthorized),
                (why) => request.CreateResponse(HttpStatusCode.Conflict)
                    .AddReason(why));
            return creationResults;
        }

        public static Task<HttpResponseMessage> PutAsync(this Resources.SamlCredential credential,
            HttpRequestMessage request, UrlHelper url)
        {
            throw new NotImplementedException();
            //return await request.GetActorIdClaimsAsync(ClaimsDefinitions.AccountIdClaimType,
            //    async (performingActorId, claims) =>
            //    {
            //        var loginProviderTaskGetter = (Func<Task<IIdentityService>>)
            //        request.Properties[ServicePropertyDefinitions.IdentityService];
            //        var loginProviderTask = loginProviderTaskGetter();
            //        var loginProvider = await loginProviderTask;
            //        var callbackUrl = url.GetLocation<Controllers.OpenIdResponseController>();
            //        var landingPage = Web.Configuration.Settings.Get(SessionServer.Configuration.AppSettings.LandingPage);
            //        var loginUrl = loginProvider.GetLoginUrl(landingPage, 0, new byte[] { }, callbackUrl);

            //        var context = request.GetSessionServerContext();
            //        var creationResults = await context.PasswordCredentials.UpdatePasswordCredentialAsync(credential.Id.UUID,
            //            credential.Token, credential.ForceChange, credential.LastEmailSent, loginUrl,
            //            performingActorId, claims,
            //            () => request.CreateResponse(HttpStatusCode.NoContent),
            //            () => request.CreateResponse(HttpStatusCode.NotFound),
            //            () => request.CreateResponse(HttpStatusCode.Unauthorized),
            //            () => request.CreateResponse(HttpStatusCode.ServiceUnavailable),
            //            (why) => request.CreateResponse(HttpStatusCode.Conflict).AddReason($"Update failed:{why}"));
            //        return creationResults;
            //    });
        }

        #region Actionables

        public static Task<HttpResponseMessage> QueryAsync(this Resources.Queries.SamlCredentialQuery credential,
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            throw new NotImplementedException();
        }
        

        public static async Task<HttpResponseMessage> DeleteAsync(this Resources.Queries.SamlCredentialQuery credential,
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            throw new NotImplementedException();
        }
        

        #endregion
    }
}

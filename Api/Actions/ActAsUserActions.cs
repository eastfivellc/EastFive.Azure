using System;
using System.Net;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web.Http.Routing;
using System.Configuration;
using EastFive.Api.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Http.Results;
using BlackBarLabs;
using EastFive.Collections.Generic;
using Microsoft.ApplicationInsights;
using EastFive.Linq.Async;
using EastFive.Linq;
using BlackBarLabs.Linq;
using EastFive.Security.SessionServer;
using BlackBarLabs.Api;
using EastFive.Extensions;

namespace EastFive.Api.Azure.Credentials
{
    public static class ActAsUserActions
    {
        public static async Task<HttpResponseMessage> GetAsync(this Resources.Queries.ActAsUserQuery query,
            HttpRequestMessage request, UrlHelper urlHelper, Func<string, RedirectResult> redirect)
        {
            return await query.ParseAsync(request,
                q => QueryByActorAsync(q.RedirectUri.ParamValue(), q.ActorId.ParamSingle(), request, urlHelper),
                q => QueryByActorAndTokenAsync(q.RedirectUri.ParamValue(), q.ActorId.ParamSingle(), q.Token.ParamValue(), request, urlHelper),
                q => QueryAllActorsAsync(q.RedirectUri.ParamValue(), q.Token.ParamValue(), request, urlHelper),
                q => QueryAllActorsAsync(q.RedirectUri.ParamValue(), request, urlHelper));
        }
        
        public static async Task<HttpResponseMessage> QueryAllActorsAsync(string redirectUri,
            HttpRequestMessage request, UrlHelper url)
        {
            return await request.GetActorIdClaimsAsync(
                (actorId, claims) => QueryAllActorsAsync(redirectUri, actorId, claims, request.Headers.Authorization.ToString(), request, url));
        }

        public static async Task<HttpResponseMessage> QueryAllActorsAsync(string redirectUri, string token,
            HttpRequestMessage request, UrlHelper url)
        {
            return await request.GetActorIdClaimsFromTokenAsync(token,
                (actorId, claims) => QueryAllActorsAsync(redirectUri, actorId, claims, token, request, url));
        }

        public static async Task<HttpResponseMessage> QueryAllActorsAsync(string redirectUri, 
                Guid actorPerforming, System.Security.Claims.Claim [] claims, string token,
            HttpRequestMessage request, UrlHelper url)
        {
            var context = request.GetSessionServerContext();
            return await context.PasswordCredentials.GetAllLoginInfoAsync(actorPerforming, claims,
                loginInfos =>
                {
                    var userInfos = loginInfos
                        .Select(
                            info =>
                            {
                                var userInfo = new Resources.UserInfo
                                {
                                    Username = info.Username,
                                    DisplayName = info.DisplayName,
                                    ActorId = info.ActorId,
                                    AccountEnabled = info.AccountEnabled,
                                    Link = info.Tokens
                                        .NullToEmpty()
                                        .Append("RedirectUri".PairWithValue(redirectUri))
                                        .Append("ActorId".PairWithValue(info.ActorId.ToString()))
                                        .Append("Token".PairWithValue(token))
                                        .Aggregate(
                                            url.GetLocation<Controllers.ActAsUserController>(),
                                            (baseUrl, param) => baseUrl.AddParameter(param.Key, param.Value),
                                            (uri) => uri.AbsoluteUri),
                                };
                                return userInfo;
                            })
                        .ToArray();

                    if (request.Headers.Accept.Where(accept => accept.MediaType == "application/json").Any())
                        return request.CreateResponse(HttpStatusCode.OK, userInfos);

                    var html = GenerateActAsUserHtml(userInfos);
                    return request.CreateHtmlResponse(html);
                },
                () => request.CreateResponse(HttpStatusCode.ServiceUnavailable)
                    .AddReason("No token providers configured"));
        }

        private static string GenerateActAsUserHtml(Resources.UserInfo[] userInfos)
        {
            var tableContents = "";
            foreach (var userInfo in userInfos)
            {
                tableContents += $"<tr><td><a href=\"{userInfo.Link}\">Username: {userInfo.Username}</a></td></tr>\n";
            }
            var html = $"<html><body><table><tr><th>UserId</th></tr>{tableContents}</table></body></html>";
            return html;
        }

        private static async Task<HttpResponseMessage> QueryByActorAsync(string redirectString, Guid actorId,
            HttpRequestMessage request, UrlHelper url)
        {
            return await request.GetActorIdClaimsAsync(
                (actorPerforming, claims) => QueryByActorAsync(redirectString, actorId, actorPerforming, claims, request, url));
        }

        private static async Task<HttpResponseMessage> QueryByActorAndTokenAsync(string redirectBase, Guid actorId, string token,
            HttpRequestMessage request, UrlHelper url)
        {
            return await request.GetActorIdClaimsFromTokenAsync(token,
                (actorPerforming, claims) => QueryByActorAsync(redirectBase, actorId, actorPerforming, claims, request, url));
        }

        private static async Task<HttpResponseMessage> QueryByActorAsync(string redirectString, Guid actorId,
            Guid actorPerforming, System.Security.Claims.Claim[] claims,
            HttpRequestMessage request, UrlHelper url)
        {
            if (!await Library.configurationManager.CanAdministerCredentialAsync(actorId, actorPerforming, claims))
                return request.CreateResponse(HttpStatusCode.Unauthorized, $"Actor [{actorPerforming}] cannot administer [{actorId}]");

            var context = request.GetSessionServerContext();
            var authenticationRequestId = Guid.NewGuid();
            var redirectUrl = new Uri(redirectString);
            var method = EastFive.Security.SessionServer.ServiceConfiguration.credentialProviders.First().Value.GetType().GetCustomAttribute<Attributes.IntegrationNameAttribute>().Name;
            return await await context.Sessions.CreateLoginAsync(authenticationRequestId, actorId,
                    method, redirectUrl, new Dictionary<string, string>(),
                async (session) =>
                {
                    var config = Library.configurationManager;
                    var redirectResponse = await config.GetRedirectUriAsync(context,
                            method, AuthenticationActions.signin,
                            authenticationRequestId, actorId, session.token, session.refreshToken, session.extraParams,
                            redirectUrl,
                        (redirectUrlSelected) => request.CreateRedirectResponse(redirectUrlSelected),
                        (paramName, why) =>
                        {
                            var message = $"Invalid parameter while completing login: {paramName} - {why}";
                            return request.CreateResponse(HttpStatusCode.BadRequest, message).AddReason(why);
                        },
                        (why) =>
                        {
                            var message = $"General failure while completing login: {why}";
                            return request.CreateResponse(HttpStatusCode.BadRequest, message)
                                .AddReason(why);
                        });
                    return redirectResponse;
                },
                "Guid not unique for creation of authentication request id".AsFunctionException<Task<HttpResponseMessage>>(),
                (why) => request.CreateResponseConfiguration("", why).AsTask());
        }
    }
}

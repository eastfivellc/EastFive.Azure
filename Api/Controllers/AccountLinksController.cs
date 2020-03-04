using BlackBarLabs.Api;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using BlackBarLabs;
using BlackBarLabs.Extensions;
using EastFive.Api.Services;
using EastFive.Security.SessionServer;
using EastFive.Azure.Auth;

namespace EastFive.Api.Azure.Credentials.Controllers
{

    public class AccountLinksQuery
    {
        [JsonProperty(PropertyName = "redirect_uri")]
        public string redirect_uri { get; set; }

        [JsonProperty(PropertyName = "response_mode")]
        public string response_mode { get; set; }
    }

    [RoutePrefix("aadb2c")]
    public class AccountLinksController : Azure.Controllers.BaseController
    {
        [HttpGet]
        public async Task<IHttpActionResult> Get([FromUri]AccountLinksQuery q)
        {
            var location = EastFive.Web.Configuration.Settings.GetString("AffirmHealth.PDMS.Api.UILocation", s => s, (s) => "");

            var response_mode = q.response_mode;
            //var redirect_uri = q.redirect_uri;
            var redirect_uri = $"{location}/upgrade";

            //return this.Request.CreateResponse(System.Net.HttpStatusCode.OK,
            //    "You have an old version of the site. Please refresh your browser")
            //    .ToActionResult();

            var context = this.Request.GetSessionServerContext();
            if (String.IsNullOrWhiteSpace(redirect_uri))
                return this.Request.CreateRedirectResponse<AuthenticationRequestLinkController>(Url).ToActionResult();
            
            if (!Uri.TryCreate(redirect_uri, UriKind.Absolute, out Uri redirectUrl))
                return this.Request
                    .CreateResponseValidationFailure(q, qry => qry.redirect_uri)
                    .ToActionResult();
            
            var method = CredentialValidationMethodTypes.Password.ToString();
            var response = await Context.GetLoginProvider(method,
                async (loginProvider) =>
                {
                    var callbackUrl = this.Url.GetLocation<OpenIdResponse>(
                        typeof(OpenIdResponse)
                            .GetCustomAttributes<RoutePrefixAttribute>()
                            .Select(routePrefix => routePrefix.Prefix)
                            .First());
                    var authReqId = Guid.NewGuid();
                    return await context.Sessions.CreateLoginAsync(authReqId,
                        method, redirectUrl, redirectUrl,
                        (type) => Url.GetLocation(type),
                        (authRequest) =>
                        {
                            return this.Request.CreateResponse(System.Net.HttpStatusCode.OK,
                                new EastFive.Security.SessionServer.Api.Resources.AccountLink
                                {
                                    Login = authRequest.loginUrl,
                                    Signup = loginProvider.GetSignupUrl(authReqId, callbackUrl, type => this.Url.GetLocation(type)),
                                    Logout = authRequest.logoutUrl,
                                });
                        },
                        () => Request.CreateResponse(System.Net.HttpStatusCode.InternalServerError).AddReason("GUID NOT UNIQUE"),
                        () => Request.CreateResponse(System.Net.HttpStatusCode.InternalServerError).AddReason("AADB2C login is not enabled"),
                        (why) => Request.CreateResponse(System.Net.HttpStatusCode.InternalServerError).AddReason(why),
                        (why) => Request.CreateResponse(System.Net.HttpStatusCode.InternalServerError).AddReason(why));
                },
                () => Request.CreateResponse(System.Net.HttpStatusCode.InternalServerError)
                    .AddReason("AADB2C login is not enabled")
                    .ToTask(),
                (why) => Request.CreateResponse(System.Net.HttpStatusCode.InternalServerError)
                    .AddReason(why)
                    .ToTask());
            return response
                .ToActionResult();
        }
    }
}
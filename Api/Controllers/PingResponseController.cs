﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using BlackBarLabs.Api.Controllers;

using BlackBarLabs;
using BlackBarLabs.Api;
using EastFive.Api.Services;
using System.Xml;
using BlackBarLabs.Extensions;
using System.Text;
using System.IO;
using System.Net.Http.Headers;

namespace EastFive.Security.SessionServer.Api.Controllers
{
    public class PingRequest
    {
        public string tokenid { get; set; }

        public string agentid { get; set; }
    }

    // aadb2c/SAMLRedirect?tokenid=ID7ee36a406286079a7237b23dd7647d95b8d42ddbcde4fbe8030000015d5790a407&agentid=e924bba8
    [RoutePrefix("aadb2c")]
    public class PingResponseController : BaseController
    {
        public Task<IHttpActionResult> Get([FromUri]PingRequest query)
        {
            return ParsePingResponseAsync(query.tokenid, query.agentid);
        }

        private async Task<IHttpActionResult> ParsePingResponseAsync(string tokenId, string agentId)
        {
            if (String.IsNullOrWhiteSpace(tokenId))
                return this.Request.CreateResponse(HttpStatusCode.Conflict)
                            .AddReason("SAML Response not provided in form POST")
                            .ToActionResult();
            var context = Request.GetSessionServerContext();
            var response = await context.Sessions.CreateAsync(Guid.NewGuid(),
                CredentialValidationMethodTypes.Ping,
                tokenId + ":" + agentId,
                (authorizationId, token, refreshToken, extraParams) =>
                {
                    var config = Library.configurationManager;
                    var redirectResponse = config.GetRedirectUri<IHttpActionResult>(CredentialValidationMethodTypes.SAML,
                        authorizationId, token, refreshToken, extraParams,
                        (redirectUrl) => Redirect(redirectUrl),
                        (paramName, why) => Request.CreateResponse(HttpStatusCode.BadRequest).AddReason(why).ToActionResult(),
                        (why) => Request.CreateResponse(HttpStatusCode.BadRequest).AddReason(why).ToActionResult());
                    return redirectResponse;
                },
                () => this.Request.CreateResponse(HttpStatusCode.Conflict)
                            .AddReason("Already exists")
                            .ToActionResult(),
                (why) => this.Request.CreateResponse(HttpStatusCode.BadRequest)
                            .AddReason($"Invalid token:{why}")
                            .ToActionResult(),
                () => this.Request.CreateResponse(HttpStatusCode.Conflict)
                            .AddReason("Token does not work in this system")
                            .ToActionResult(),
                () => this.Request.CreateResponse(HttpStatusCode.Conflict)
                            .AddReason("Token is not connected to a user in this system")
                            .ToActionResult(),
                (why) => this.Request.CreateResponse(HttpStatusCode.BadGateway)
                            .AddReason(why)
                            .ToActionResult(),
                (why) => this.Request.CreateResponse(HttpStatusCode.ServiceUnavailable)
                            .AddReason(why)
                            .ToActionResult());

            return response;
        }


    }
}
﻿using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web.Http.Routing;
using System.Configuration;

using BlackBarLabs;
using BlackBarLabs.Extensions;
using EastFive.Api.Services;
using EastFive.Security.SessionServer.Configuration;
using EastFive.Api;
using EastFive.Security.SessionServer;
using EastFive.Security.SessionServer.Api.Controllers;
using EastFive.Security;
using BlackBarLabs.Api;

namespace EastFive.Api.Azure.Credentials
{
    [FunctionViewController(Route = "AuthenticationRequestLink")]
    public static class AuthenticationRequestLinkActions
    {
        public static Task<HttpResponseMessage> QueryAsync(this Resources.Queries.AuthenticationRequestLinkQuery query,
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            return query.ParseAsync(request,
                q => QueryAsync(request, urlHelper),
                q => QueryByIntegrationAsync(q.SupportsIntegration.ParamValue(), request, urlHelper));
        }

        [HttpGet]
        public static async Task<HttpResponseMessage> QueryAsync(
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            var context = request.GetSessionServerContext();
            return await context.LoginProviders.GetAllAsync(
                (methods) =>
                {
                    var response = request.CreateResponse(HttpStatusCode.OK,
                        methods.Select(method => Convert(method, urlHelper)));
                    return response;
                },
                why => request.CreateResponse(HttpStatusCode.ServiceUnavailable).AddReason(why));
        }

        [HttpGet]
        public static async Task<HttpResponseMessage> QueryByIntegrationAsync([QueryParameter]bool supportsIntegration,
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            var context = request.GetSessionServerContext();
            return await context.LoginProviders.GetAllAsync(supportsIntegration,
                (methods) =>
                {
                    var response = request.CreateResponse(HttpStatusCode.OK,
                        methods.Select(method => Convert(method, urlHelper)));
                    return response;
                },
                why => request.CreateResponse(HttpStatusCode.ServiceUnavailable).AddReason(why));
        }

        private static Resources.AuthenticationRequestLink Convert(string method, UrlHelper urlHelper)
        {
            return new Resources.AuthenticationRequestLink
            {
                Id = urlHelper.GetWebId<Controllers.SessionController>(SecureGuid.Generate()),
                Method = method,
                Name = method.ToString(),
                SecureId = SecureGuid.Generate(),
            };
        }
    }
}

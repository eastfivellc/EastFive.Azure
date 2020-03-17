using System;
using System.Net;
using System.Threading.Tasks;
using System.Net.Http;
using System.Configuration;
using EastFive.Api.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.AspNetCore.Mvc.Routing;

using BlackBarLabs.Extensions;
using EastFive.Collections.Generic;
using Microsoft.ApplicationInsights;
using EastFive.Linq.Async;
using EastFive.Linq;
using EastFive.Security.SessionServer;
using BlackBarLabs.Api;

namespace EastFive.Api.Azure.Credentials
{
    public static class MonitoringActions
    {
        public static async Task<HttpResponseMessage> GetAsync(this Resources.Queries.MonitoringQuery query,
            HttpRequestMessage request, UrlHelper urlHelper)
        {
            return await query.ParseAsync(request,
                q => QueryMonitoringDataAsync(q.ApiKeySecurity, q.Month.ParamValue(), request, urlHelper));
        }

        public static async Task<HttpResponseMessage> QueryMonitoringDataAsync(
                string apiKeySecurity, DateTime month,
            HttpRequestMessage request, UrlHelper url)
        {
            //02/15/2019, KDH - Exit early without returning data for now.  Doing this because this code was
            //required as a part of our sprint.  However, unless someone asks for this data, leaving this 
            //without functionality as it unnecessarily potentially exposes some security info.
            return request.CreateResponse(HttpStatusCode.OK);

            return await EastFive.Web.Configuration.Settings.GetString(EastFive.Azure.AppSettings.ApiSecurityKey,
                async keyFromConfig =>
                {
                    if (keyFromConfig != apiKeySecurity)
                        request.CreateResponse(HttpStatusCode.Unauthorized)
                            .AddReason("Api security key is invalid");

                    var context = request.GetSessionServerContext();
                    var content = await context.Monitoring.GetByMonthAsync(month, info => info);
                    return request.CreateResponse(HttpStatusCode.OK, content);
                },
                unspec =>
                {
                    return request.CreateResponse(HttpStatusCode.Conflict).AddReason("Could not find api security key in config").ToTask();
                });
        }
    }
}

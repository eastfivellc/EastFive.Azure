using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using Microsoft.Azure.ApplicationInsights;
using Microsoft.Azure.ApplicationInsights.Models;

namespace EastFive.Azure.Monitoring
{
    [FunctionViewController(
        Route = "ApplicationInsights",
        ContentType = "x-application/application-insights",
        ContentTypeVersion = "0.1")]
    public class ApplicationInsights
    {
        [HttpGet]
        public static Task<IHttpResponse> GetAsync(
                [QueryParameter]string eventId,
                ContentTypeResponse<EventsExceptionResult[]> onResults)
        {
            return AppSettings.ApplicationInsights.ApplicationId.ConfigurationString(
                applicationId =>
                {
                    return AppSettings.ApplicationInsights.ClientSecret.ConfigurationString(
                        async token =>
                        {
                            // Authenticate with client secret (app key)
                            var clientCred = new ApiKeyClientCredentials(token);

                            // New up a client with credentials and AI application Id
                            var client = new ApplicationInsightsDataClient(clientCred);
                            client.AppId = applicationId;
                            
                            var exceptionEvents = await client.GetExceptionEventsAsync(
                                timespan: TimeSpan.FromMinutes(240.0),
                                top: 10, 
                                format:"application/json;odata.metadata=full");
                            var requestEvents = await client.GetRequestEventsAsync(
                                timespan: TimeSpan.FromMinutes(30.0));

                            return onResults(exceptionEvents.Value.ToArray());
                        });
                });
            
        }

        [HttpPost]
        public static Task<IHttpResponse> WebhookAsync(
                [Resource]object appInsightsCallback,
                ContentTypeResponse<EventsExceptionResult[]> onResults)
        {
            return AppSettings.ApplicationInsights.ApplicationId.ConfigurationString(
                applicationId =>
                {
                    return AppSettings.ApplicationInsights.ClientSecret.ConfigurationString(
                        async token =>
                        {
                            // Authenticate with client secret (app key)
                            var clientCred = new ApiKeyClientCredentials(token);

                            // New up a client with credentials and AI application Id
                            var client = new ApplicationInsightsDataClient(clientCred);
                            client.AppId = applicationId;

                            var exceptionEvents = await client.GetExceptionEventsAsync(
                                timespan: TimeSpan.FromMinutes(240.0),
                                top: 10,
                                format: "application/json;odata.metadata=full");
                            var requestEvents = await client.GetRequestEventsAsync(
                                timespan: TimeSpan.FromMinutes(30.0));

                            return onResults(exceptionEvents.Value.ToArray());
                        });
                });

        }

        public struct AppInsightsCallback
        {
            public struct Properties
            {
                public struct Property
                {

                }
                public struct Context
                {
                    public struct ContextProperties
                    {

                    }
                }
            }
        }
    }
}
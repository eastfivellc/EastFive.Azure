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
using Microsoft.Rest.Azure.Authentication;

namespace EastFive.Azure.Monitoring
{
    [FunctionViewController6(
        Route = "ApplicationInsights",
        Resource = typeof(ApplicationInsights),
        ContentType = "x-application/application-sights-results",
        ContentTypeVersion = "0.1")]
    public class ApplicationInsights
    {
        [HttpGet]
        public static Task<HttpResponseMessage> GetAsync(
                [QueryParameter]string eventId)
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
                            
                            var asdf = await client.GetExceptionEventsAsync(
                                timespan: TimeSpan.FromMinutes(30.0));
                            // asdf.Body.Value[0].Request.

                            return new HttpResponseMessage();
                        });
                });
            
        }
    }
}
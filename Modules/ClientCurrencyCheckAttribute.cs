using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Modules
{
    public class ClientCurrencyCheckAttribute : Attribute, IHandleRoutes
    {
        private long? clientMinimumVersionMaybe;
        private const string clientVersionRequestHeader = "X-Client-Born-On";
        private const string clientVersionResponseHeader = "X-Client-Required-Born-On";

        public ClientCurrencyCheckAttribute()
        {
            this.clientMinimumVersionMaybe = AppSettings.ClientMinimumVersion.ConfigurationLong(
                clientMinimumVersion => clientMinimumVersion,
                (why) => default,
                () => default);
        }

        public async Task<HttpResponseMessage> HandleRouteAsync(Type controllerType, 
            IApplication httpApp, HttpRequestMessage request, string routeName, 
            RouteHandlingDelegate continueExecution)
        {
            var response = await continueExecution(controllerType, httpApp, request, routeName);
            if (!clientMinimumVersionMaybe.HasValue)
                return response;
            var clientMinimumVersion = clientMinimumVersionMaybe.Value;

            if (!request.Headers.Contains(clientVersionRequestHeader))
                return response;

            if (!request.Headers.TryGetValues(clientVersionRequestHeader, 
                    out IEnumerable<string> clientVersionsRequest))
                return response;

            if (!clientVersionsRequest.Any())
                return response;

            var clientVersionRequest = clientVersionsRequest.First();
            if (!long.TryParse(clientVersionRequest, out long clientVersionSent))
            {
                // TODO: Log
                return response;
            }

            if(clientVersionSent >= clientMinimumVersion)
                return response;

            response.Headers.Add(clientVersionResponseHeader, clientMinimumVersion.ToString());
            response.Headers.Add("Access-Control-Expose-Headers", clientVersionResponseHeader);
            return response;
        }
    }
}

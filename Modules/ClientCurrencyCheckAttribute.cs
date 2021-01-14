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

        public async Task<IHttpResponse> HandleRouteAsync(Type controllerType, 
            IApplication httpApp, IHttpRequest request,
            RouteHandlingDelegate continueExecution)
        {
            var response = await continueExecution(controllerType, httpApp, request);
            
            if (Api.Azure.Modules.SpaHandler.SpaMinimumVersion.HasValue)
                this.clientMinimumVersionMaybe = Api.Azure.Modules.SpaHandler.SpaMinimumVersion.Value;

            if (!clientMinimumVersionMaybe.HasValue)
                return response;
            var clientMinimumVersion = clientMinimumVersionMaybe.Value;

            if (!request.TryGetHeader(clientVersionRequestHeader, out string clientVersionRequest))
                return response;

            if (!long.TryParse(clientVersionRequest, out long clientVersionSent))
            {
                // TODO: Log
                return response;
            }

            if(clientVersionSent >= clientMinimumVersion)
                return response;

            response.SetHeader(clientVersionResponseHeader, clientMinimumVersion.ToString());
            response.SetHeader("Access-Control-Expose-Headers", clientVersionResponseHeader);
            return response;
        }
    }
}

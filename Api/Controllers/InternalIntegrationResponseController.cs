//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Threading.Tasks;
//using System.Web;
//using System.Web.Http;
//using BlackBarLabs.Api.Controllers;

//using BlackBarLabs;
//using BlackBarLabs.Api;
//using EastFive.Api.Services;
//using System.Xml;
//using BlackBarLabs.Extensions;
//using System.Text;
//using System.IO;
//using System.Net.Http.Headers;
//using Microsoft.ApplicationInsights;
//using EastFive.Security.SessionServer.Configuration;
//using EastFive.Security.SessionServer.Exceptions;
//using EastFive.Extensions;
//using EastFive.Security.SessionServer.Api.Controllers;
//using EastFive.Collections.Generic;
//using EastFive.Api.Controllers;
//using Newtonsoft.Json;

//namespace EastFive.Api.Azure.Credentials.Controllers
//{
//    [FunctionViewController(Route = "InternalIntegrationResponse")]
//    public class InternalIntegrationResponseController
//    {
//        public const string IntegrationIdPropertyName = "integrationId";
//        [JsonProperty(PropertyName = IntegrationIdPropertyName)]
//        public Guid IntegrationId;

//        public const string resourceTypesPropertyName = "resource_types";
//        [JsonProperty(PropertyName = resourceTypesPropertyName)]
//        public string ResourceTypes;
        
//        [HttpGet]
//        public static async Task<HttpResponseMessage> GetResponse(AzureApplication application, 
//                [QueryParameter(CheckFileName = true, Name = IntegrationIdPropertyName)]Guid integrationId,
//                [QueryParameter(Name = resourceTypesPropertyName)]string resourceTypes,
//                HttpRequestMessage request,
//            RedirectResponse onRedirect)
//        {
//            var extraParams = new Dictionary<string, string>()
//            {
//                { InternalProvider.integrationIdKey, integrationId.ToString() },
//                { InternalProvider.resourceTypes, resourceTypes },
//            };
//            return await ResponseController.ProcessRequestAsync(application, InternalProvider.IntegrationName, request.RequestUri, extraParams,
//                (location, why) => onRedirect(location),
//                (code, body, reason) =>
//                    request
//                        .CreateResponse(code, body)
//                        .AddReason(reason));
            
//        }
//    }
//}
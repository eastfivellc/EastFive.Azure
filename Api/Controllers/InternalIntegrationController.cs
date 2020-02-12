//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net.Http;
//using System.Text;
//using System.Threading.Tasks;

//using BlackBarLabs.Extensions;
//using EastFive.Api;
//using EastFive.Api.Azure.Credentials.Controllers;
//using EastFive.Collections.Generic;
//using EastFive.Security.SessionServer;
//using EastFive.Serialization;
//using EastFive.Sheets;


//namespace EastFive.Api.Controllers
//{
//    [FunctionViewController(Route = "InternalIntegration")]
//    public static class InternalIntegrationController
//    {
//        public const string StateQueryParameter = "state";

//        [HttpGet]
//        public async static Task<HttpResponseMessage> IntegrationUploadAsync(EastFive.Security.SessionServer.Context context,
//                [QueryParameter(CheckFileName = true, Name = StateQueryParameter)]Guid integrationId,
//                HttpRequestMessage request, System.Web.Http.Routing.UrlHelper url,
//            ViewFileResponse onLoadUploadPage)
//        {
//            return await onLoadUploadPage("InternalIntegration/ConfigureIntegration.cshtml", integrationId).ToTask();
//        }

//        [HttpPost(MatchAllParameters = false)]
//        public async static Task<HttpResponseMessage> XlsPostAsync(EastFive.Security.SessionServer.Context context,
//                [QueryParameter]Guid integration,
//                [QueryParameter]IDictionary<string, bool> resourceTypes,
//                Azure.AzureApplication application,
//                HttpRequestMessage request, System.Web.Http.Routing.UrlHelper url,
//            RedirectResponse onSuccess,
//            NotFoundResponse onNotFound,
//            GeneralConflictResponse onError)
//        {
//            var resourceTypesList = resourceTypes.SelectKeys().Join(",");
//            return onSuccess(
//                url.GetLocation<InternalIntegrationResponseController>(
//                    (irc) => irc.IntegrationId.AssignQueryValue(integration),
//                    (irc) => irc.ResourceTypes.AssignQueryValue(resourceTypesList),
//                    application));
//        }
//    }
//}

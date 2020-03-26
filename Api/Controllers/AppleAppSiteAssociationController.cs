using BlackBarLabs.Api;
using BlackBarLabs.Extensions;
using EastFive.Api;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace EastFive.Security.SessionServer.Api.Controllers
{
    [FunctionViewController6(
        Prefix = "/",
        Route= "apple-app-site-association", 
        Resource = typeof(AppleAppSiteAssociationController))]
    public class AppleAppSiteAssociationController : ApiController
    {
        [HttpGet]
        public IHttpResponse Get(
            ContentResponse onSuccess,
            NotFoundResponse onNotFound)
        {
            return Configuration.AppSettings.AppleAppSiteAssociationId.ConfigurationString(
                (appId) =>
                {
                    var content = new
                    {
                        applinks = new
                        {
                            apps = new string[] { },
                            details = new object[]
                            {
                                new
                                {
                                    appID = appId,
                                    paths = new string [] { "*" },
                                }
                            }
                        }
                    };
                    return onSuccess(content);
                },
                (why) => onNotFound().AddReason(why));
        }
    }
}

//{
//    "applinks": {
//        "apps": [],
//        "details": [
//            {
//                "appID": "W6R55DKE7X.com.eastfive.orderowl",
//                "paths": [ "*"]
//            }
//        ]
//    }
//}

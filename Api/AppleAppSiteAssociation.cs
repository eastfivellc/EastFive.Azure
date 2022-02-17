using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Extensions;
using EastFive.Web.Configuration;


namespace EastFive.Api.Azure.Apple
{
    [FunctionViewController(
        Namespace = ".well-known",
        Route = "apple-app-site-association")]
    public class AppleAppSiteAssociationController
    {
        [EastFive.Api.HttpGet]
        public static IHttpResponse Get(
                ContentResponse onSuccess,
                NotFoundResponse onNotFound)
        {
            return EastFive.Azure.AppSettings.Apple.AppleAppSiteAssociationId.ConfigurationString(
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

    //public class AppleAppSiteAssociation
    //{
    //    public Applinks applinks { get; set; }

    //    [EastFive.Api.HttpGet]
    //    public static IHttpResponse Get(
    //        ContentResponse onSuccess,
    //        NotFoundResponse onNotFound)
    //    {
    //        return EastFive.Azure.AppSettings.Apple.AppleAppSiteAssociationId.ConfigurationString(
    //            (appId) =>
    //            {
    //                var content = new AppleAppSiteAssociation
    //                {
    //                    applinks = new Applinks
    //                    {
    //                        details = new Detail[]
    //                        {
    //                            new Detail
    //                            {
    //                                appIDs = appId.AsArray(),
    //                                components = new Component []
    //                                {
    //                                    new Component
    //                                    {
    //                                        Root = "/api/*",
    //                                        comment = "Matches any URL whose path starts with /api/",
    //                                    }
    //                                },
    //                            }
    //                        }
    //                    }
    //                };
    //                return onSuccess(content);
    //            },
    //            (why) => onNotFound().AddReason(why));
    //    }

    //    public class Applinks
    //    {
    //        public Detail[] details { get; set; }
    //    }

    //    public class Detail
    //    {
    //        public string[] appIDs { get; set; }
    //        public Component[] components { get; set; }
    //    }

    //    public class Component
    //    {
    //        [JsonProperty(PropertyName = "/")]
    //        public string Root { get; set; }
    //        public string comment { get; set; }
    //    }

    //}
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

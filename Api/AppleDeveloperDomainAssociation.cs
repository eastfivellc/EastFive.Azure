using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Controllers;
using EastFive.Azure.Auth;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Apple
{
    [FunctionViewController(
        Namespace = ".well-known",
        Route = "apple-developer-domain-association.txt",
        ContentType = "x-application/apple-redirect",
        ContentTypeVersion = "0.1")]
    public class AppleDeveloperDomainAssociation
    {
        [Unsecured("Apple Developer Domain Association file - must be publicly accessible for Apple domain verification")]
        [HttpGet(MatchAllParameters = false)]
        public static IHttpResponse Get(
            TextResponse onResponse,
            ConfigurationFailureResponse onConfigurationFailure)
        {
            return EastFive.Azure.AppSettings.Apple.DeveloperSiteAssociation.ConfigurationString(
                (content) =>
                {
                    return onResponse(content);
                },
                (why) => onConfigurationFailure(why, why));
        }
    }
}

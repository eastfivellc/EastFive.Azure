using BlackBarLabs.Api;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Controllers;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace OrderOwl.Auth
{
    [FunctionViewController(
        Namespace = ".well-known",
        Route = "apple-developer-domain-association.txt",
        Resource = typeof(AppleDeveloperDomainAssociation),
        ContentType = "x-application/apple-redirect",
        ContentTypeVersion = "0.1")]
    public class AppleDeveloperDomainAssociation
    {
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

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

namespace EastFive.Azure
{
    //public class AppleDeveloperDomainAssociation : ApiController
    //{
    //    public IHttpActionResult Get()
    //    {
    //        var response = AppSettings.Apple.DeveloperSiteAssociation.ConfigurationString(
    //            (content) =>
    //            {
    //                return this.Request.CreateResponse(HttpStatusCode.OK, 
    //                    content, "text/text");
    //            },
    //            (why) => this.Request.CreateResponse(HttpStatusCode.NotFound)
    //                .AddReason(why));
            
    //        return this.ActionResult(() => response.AsTask());
    //    }
    //}
}


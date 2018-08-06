﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

using BlackBarLabs;
using BlackBarLabs.Api;
using EastFive.Api.Services;
using BlackBarLabs.Extensions;
using EastFive.Security.CredentialProvider.AzureADB2C;
using System.Web.Http.Routing;

namespace EastFive.Security.SessionServer.Api.Controllers
{
    // /api/OAuthResponseLightspeed?code=d6ac033707089b1a727711631c277323b7c7905a&state=331d23d2cc204a1b9a7eeb1420000333
    public class AADB2CResponseController : ResponseController
    {
        public override async Task<IHttpActionResult> Get([FromUri]ResponseResult result)
        {
            result = new ResponseResult()
            {
                method = CredentialValidationMethodTypes.Password,
            };
            return await base.Get(result);
            //return await this.Request.CreateResponse(HttpStatusCode.OK, "Paused").ToActionResult().ToTask();
        }
        
    }
}
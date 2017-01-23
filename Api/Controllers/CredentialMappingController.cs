﻿using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

using BlackBarLabs.Api;

using EastFive.Security.SessionServer.Api.Resources;

namespace EastFive.Security.SessionServer.Api.Controllers
{
    [RoutePrefix("aadb2c")]
    public class CredentialMappingController : BaseController
    {
        public IHttpActionResult Get([FromUri]Resources.Queries.CredentialMappingQuery model)
        {
            return new HttpActionResult(() => model.QueryAsync(this.Request, this.Url));
        }

        public IHttpActionResult Post([FromBody]Resources.CredentialMapping resource)
        {
            return new HttpActionResult(() => resource.CreateAsync(this.Request, this.Url));
        }
    }
}


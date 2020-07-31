using System;
using System.Threading.Tasks;
using System.Web.Http;
using BlackBarLabs.Api;
using System.Net.Http;
using EastFive.Security.SessionServer.Api;

namespace EastFive.Api.Azure.Credentials.Controllers
{
    public class MonitoringController : Azure.Controllers.BaseController
    {
        public IHttpActionResult Get([FromUri]Resources.Queries.MonitoringQuery query)
        {
            return this.ActionResult(() => query.GetAsync(this.Request, this.Url));
        }
    }
}

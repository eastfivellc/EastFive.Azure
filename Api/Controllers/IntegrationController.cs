//using System;
//using System.Web.Http;
//using System.Threading.Tasks;

//using BlackBarLabs.Api;
//using System.Net.Http;
//using System.Net;
//using BlackBarLabs.Extensions;
//using EastFive.Api.Azure.Controllers;

//namespace EastFive.Api.Azure.Credentials.Controllers
//{
//    public class IntegrationController : BaseController
//    {   
//        public IHttpActionResult Post([FromBody]Resources.Integration model)
//        {
//            return new HttpActionResult(() => model.CreateAsync(this.Request, this.Url));
//        }

//        public IHttpActionResult Get([FromUri]Resources.Queries.IntegrationQuery model)
//        {
//            return new HttpActionResult(() => model.QueryAsync(this.Request, this.Url));
//        }
        
//        public IHttpActionResult Put([FromBody]Resources.Integration model)
//        {
//            return this.ActionResult(() => model.UpdateAsync(this.Request));
//        }

//        public IHttpActionResult Delete([FromUri]Resources.Queries.IntegrationQuery query)
//        {
//            return this.ActionResult(() => query.DeleteAsync(this.Request, this.Url));
//        }
//    }
//}


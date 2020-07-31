using System;
using System.Threading.Tasks;
using System.Web.Http;
using BlackBarLabs.Api;
//using System.Web.Mvc;
using System.Net.Http;
using EastFive.Security.SessionServer.Api;

namespace EastFive.Api.Azure.Credentials.Controllers
{
    public class ActAsUserController : Azure.Controllers.BaseController
    {
        public IHttpActionResult Get([FromUri]Resources.Queries.ActAsUserQuery query)
        {
            return this.ActionResult(() => query.GetAsync(this.Request, this.Url, this.Redirect));
        }

        public IHttpActionResult Options(Nullable<Guid> Id = default(Nullable<Guid>))
        {
            return this.ActionResult(
                () =>
                {
                    var request = this.Request;
                    return request.GetActorIdClaimsAsync(
                        (actorId, claims) =>
                        {
                        var responseAllowed = request.CreateResponse(System.Net.HttpStatusCode.OK);
                        responseAllowed.Content = new StringContent(String.Empty);
                        responseAllowed.Content.Headers.Add("Access-Control-Expose-Headers", "Allow");

                        return EastFive.Security.SessionServer.Library.configurationManager.CanActAsUsersAsync(actorId, claims,
                            () =>
                            {
                                responseAllowed.Content.Headers.Allow.Add(HttpMethod.Get.Method);
                                return responseAllowed;
                            },
                            ()=> 
                            {
                                responseAllowed.AddReason($"Actor [{actorId}] is not site admin");
                                return responseAllowed;
                            });
                        });
                });
        }
    }
}

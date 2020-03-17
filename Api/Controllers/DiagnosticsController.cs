using BlackBarLabs.Api;
using Newtonsoft.Json;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using BlackBarLabs.Extensions;
using EastFive.Api.Azure.Controllers;
using EastFive.Api.Azure.Credentials.Controllers;
using EastFive.Api;

namespace EastFive.Security.SessionServer.Api.Controllers
{
    public class AppSetting
    {
        [JsonProperty(PropertyName = "Name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "Value")]
        public string Value { get; set; }
    }

    [FunctionViewController6(
        Prefix = "aadb2c",
        Route = "Diagnostics")]
    public class DiagnosticsController 
    {
        [EastFive.Api.HttpGet]
        public static Task<HttpResponseMessage> Get(EastFive.Api.Security security, HttpRequestMessage request)
        {
            return EastFive.Web.Configuration.Settings.GetGuid(
                EastFive.Api.AppSettings.ActorIdSuperAdmin,
                (actorIdSuperAdmin) =>
                {
                    if (actorIdSuperAdmin == security.performingAsActorId)
                    {
                        var settings = ConfigurationManager.AppSettings.AllKeys
                            .Select(x => new AppSetting { Name = x, Value = ConfigurationManager.AppSettings[x] }).OrderBy(x => x.Name).ToArray();
                        return request.CreateResponse(System.Net.HttpStatusCode.OK, settings, "application/json").ToTask();
                    }
                    return request.CreateResponse(System.Net.HttpStatusCode.NotFound).ToTask();
                },
                (why) => request.CreateResponse(System.Net.HttpStatusCode.InternalServerError, why).ToTask());
        }
    }
}
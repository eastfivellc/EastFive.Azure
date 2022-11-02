using Newtonsoft.Json;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using EastFive.Api;
using EastFive.Extensions;

namespace EastFive.Azure.Diagnostics
{
    public class AppSetting
    {
        [JsonProperty(PropertyName = "Name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "Value")]
        public string Value { get; set; }
    }

    [FunctionViewController(
        Namespace = "aadb2c",
        Route = "Diagnostics")]
    public class DiagnosticsController 
    {
        [EastFive.Api.HttpGet]
        public static Task<IHttpResponse> Get(EastFive.Api.Security security, IHttpRequest request)
        {
            return EastFive.Web.Configuration.Settings.GetGuid(
                EastFive.Api.AppSettings.ActorIdSuperAdmin,
                (actorIdSuperAdmin) =>
                {
                    if (actorIdSuperAdmin == security.performingAsActorId)
                    {
                        var settings = ConfigurationManager.AppSettings.AllKeys
                            .Select(x => new AppSetting { Name = x, Value = ConfigurationManager.AppSettings[x] }).OrderBy(x => x.Name).ToArray();
                        return new JsonHttpResponse(request, System.Net.HttpStatusCode.OK, settings).AsTask<IHttpResponse>();
                    }
                    return request.CreateResponse(System.Net.HttpStatusCode.NotFound).AsTask();
                },
                (why) => request.CreateResponse(System.Net.HttpStatusCode.InternalServerError, why).AsTask());
        }
    }
}
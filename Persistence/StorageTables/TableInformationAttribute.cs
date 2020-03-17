using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Threading;
using System.Reflection;

using Microsoft.ApplicationInsights;

using Newtonsoft.Json;

using EastFive.Serialization;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Azure.Persistence.AzureStorageTables;

namespace EastFive.Api.Azure.Modules
{
    public class TableInformationAttribute : Attribute, IHandleRoutes
    {
        public const string HeaderKey = "StorageTableInformation";

        public Task<HttpResponseMessage> HandleRouteAsync(Type controllerType, 
            IApplication httpApp, HttpRequestMessage request,
            RouteData routeData, MethodInfo [] extensionsMethods,
            RouteHandlingDelegate continueExecution)
        {
            if (!request.Headers.Contains(HeaderKey))
                return continueExecution(controllerType, httpApp, request,
                    routeData, extensionsMethods);
            return EastFive.Azure.AppSettings.TableInformationToken.ConfigurationString(
                async headerToken =>
                {
                    if (request.Headers.GetValues(HeaderKey).First() != headerToken)
                        return request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);

                    if(request.Headers.Contains("X-StorageTableInformation-List"))
                    {
                        var tableData = await controllerType.StorageGetAll().ToArrayAsync();

                        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
                        var converter = new Serialization.ExtrudeConvert(httpApp as HttpApplication, request);
                        var jsonObj = Newtonsoft.Json.JsonConvert.SerializeObject(tableData,
                            new JsonSerializerSettings
                            {
                                Converters = new JsonConverter[] { converter }.ToList(),
                            });
                        response.Content = new StringContent(jsonObj, Encoding.UTF8, "application/json");
                        return response;
                    }

                    var tableInformation = await controllerType.StorageTableInformationAsync();
                    return request.CreateResponse(System.Net.HttpStatusCode.OK, tableInformation);
                },
                why => continueExecution(controllerType, httpApp, request,
                    routeData, extensionsMethods));
        }

    }
}

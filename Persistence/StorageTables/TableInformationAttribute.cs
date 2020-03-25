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

        public Task<IHttpResponse> HandleRouteAsync(Type controllerType, 
            IApplication httpApp, IHttpRequest routeData, 
            RouteHandlingDelegate continueExecution)
        {
            var request = routeData.request;
            if (!request.Headers.ContainsKey(HeaderKey))
                return continueExecution(controllerType, httpApp, routeData);
            return EastFive.Azure.AppSettings.TableInformationToken.ConfigurationString(
                async headerToken =>
                {
                    if (request.Headers[HeaderKey].First() != headerToken)
                        return routeData.CreateResponse(System.Net.HttpStatusCode.Unauthorized);

                    if(request.Headers.ContainsKey("X-StorageTableInformation-List"))
                    {
                        var tableData = await controllerType.StorageGetAll().ToArrayAsync();
                        return routeData.CreateExtrudedResponse(
                            System.Net.HttpStatusCode.OK,  tableData);
                    }

                    var tableInformation = await controllerType.StorageTableInformationAsync();
                    return routeData.CreateResponse(System.Net.HttpStatusCode.OK, tableInformation);
                },
                why => continueExecution(controllerType, httpApp, routeData));
        }

    }
}

using EastFive.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using EastFive.Serialization;
using EastFive.Extensions;
using BlackBarLabs.Web;
using System.Net.Http;
using System.Threading;
using EastFive.Linq;
using EastFive.Web.Configuration;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Api.Azure.Controllers;
using RestSharp.Extensions;
using RestSharp.Serialization.Json;

namespace EastFive.Api.Azure.Modules
{
    public class SpaHandler : EastFive.Api.Modules.ApplicationHandler
    {
        private const string IndexHTMLFileName = "index.html";
        private const string BuildJsonFileName = "content/build.json";
        private static byte[] indexHTML;
        private static ManualResetEvent signal = new ManualResetEvent(false);

        private Dictionary<string, byte[]> lookupSpaFile;
        private string[] firstSegments;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (signal != null)
                {
                    signal.Dispose();
                    signal = default;
                }
            }
        }

        internal static byte[] IndexHTML
        {
            get
            {
                if (indexHTML == null)
                {
                    signal.WaitOne();
                }
                return indexHTML;
            }
        }

        public SpaHandler(AzureApplication httpApp, System.Web.Http.HttpConfiguration config)
            : base(config)
        {
            // TODO: A better job of matching that just grabbing the first segment
            firstSegments = System.Web.Routing.RouteTable.Routes
                .Where(route => route is System.Web.Routing.Route)
                .Select(route => route as System.Web.Routing.Route)
                .Where(route => !route.Url.IsNullOrWhiteSpace())
                .Select(
                    route => route.Url.Split(new char[] { '/' }).First())
                .ToArray();

            ExtractSpaFiles(httpApp);
        }

        public SpaHandler(AzureApplication httpApp, System.Web.Http.HttpConfiguration config,
            HttpMessageHandler handler)
            : base(config, handler)
        {
            // TODO: A better job of matching that just grabbing the first segment
            firstSegments = System.Web.Routing.RouteTable.Routes
                .Where(route => route is System.Web.Routing.Route)
                .Select(route => route as System.Web.Routing.Route)
                .Where(route => !route.Url.IsNullOrWhiteSpace())
                .Select(
                    route => route.Url.Split(new char[] { '/' }).First())
                .ToArray();

            ExtractSpaFiles(httpApp);
        }

        public static int? SpaMinimumVersion = default;
        
        private void ExtractSpaFiles(AzureApplication application)
        {
            try
            {
                bool setup = EastFive.Azure.Persistence.AppSettings.SpaStorage.ConfigurationString(
                    connectionString =>
                    {
                        ZipArchive zipArchive = null;
                        try
                        {
                            var blobClient = AzureTableDriverDynamic.FromStorageString(connectionString).BlobClient;
                            var containerName = EastFive.Azure.Persistence.AppSettings.SpaContainer.ConfigurationString(name => name);
                            var container = blobClient.GetContainerReference(containerName);
                            var blobRef = container.GetBlockBlobReference("spa.zip");
                            var blobStream = blobRef.OpenRead();

                            using (zipArchive = new ZipArchive(blobStream))
                            {
                                indexHTML = zipArchive.Entries
                                    .First(item => string.Compare(item.FullName, IndexHTMLFileName, true) == 0)
                                    .Open()
                                    .ToBytes();

                                var buildJsonEntries = zipArchive.Entries
                                    .Where(item => string.Compare(item.FullName, BuildJsonFileName, true) == 0);
                                if (buildJsonEntries.Any())
                                {
                                    var buildJsonString = buildJsonEntries
                                        .First()
                                        .Open()
                                        .ToBytes()
                                        .AsString();
                                    dynamic buildJson = Newtonsoft.Json.JsonConvert.DeserializeObject(buildJsonString);
                                    SpaMinimumVersion = (int)buildJson.buildTimeInSeconds;
                                }
                                
                                lookupSpaFile = EastFive.Azure.AppSettings.SpaSiteLocation.ConfigurationString(
                                    (siteLocation) =>
                                    {
                                        application.Telemetry.TrackEvent($"SpaHandlerModule - ExtractSpaFiles   siteLocation: {siteLocation}");
                                        return zipArchive.Entries
                                            .Where(item => string.Compare(item.FullName, IndexHTMLFileName, true) != 0)
                                            .Select(
                                                entity =>
                                                {
                                                    if (!entity.FullName.EndsWith(".js"))
                                                        return entity.FullName.PairWithValue(entity.Open().ToBytes());

                                                    var fileBytes = entity.Open()
                                                        .ToBytes()
                                                        .GetString()
                                                        .Replace("8FCC3D6A-9C25-4802-8837-16C51BE9FDBE.example.com", siteLocation)
                                                        .GetBytes();

                                                    return entity.FullName.PairWithValue(fileBytes);
                                                })
                                            .ToDictionary();
                                    },
                                    (why) =>
                                    {
                                        application.Telemetry.TrackException(new ArgumentNullException("Could not find SpaSiteLocation - is this key set in app settings?"));
                                        return new Dictionary<string, byte[]>();
                                    });
                            }
                            return true;
                        }
                        catch
                        {
                            indexHTML = System.Text.Encoding.UTF8.GetBytes("SPA Not Installed");
                            return false;
                        }
                    },
                    why => false);
            }
            finally
            {
                signal.Set();
            }
        }
        
        protected override async Task<HttpResponseMessage> SendAsync(EastFive.Api.HttpApplication httpApp, HttpRequestMessage request, CancellationToken cancellationToken, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> continuation)
        {
            if (!request.RequestUri.IsDefaultOrNull())
            {
                if (request.RequestUri.PathAndQuery.HasBlackSpace())
                {
                    if (request.RequestUri.PathAndQuery.Contains("apple-app-site-association"))
                    {
                        return await continuation(request, cancellationToken);
                    }
                }
            }

            if (lookupSpaFile.IsDefaultNullOrEmpty())
                return await continuation(request, cancellationToken);

            var context = httpApp.Context;
            string filePath = context.Request.FilePath;
            string fileName = VirtualPathUtility.GetFileName(filePath);

            if (!(httpApp is AzureApplication))
                return await continuation(request, cancellationToken);

            if (lookupSpaFile.ContainsKey(fileName))
            {
                var controllerType = typeof(SpaServeController);
                var routeName = "spaservecontroller";
                return await httpApp.GetType()
                    .GetAttributesInterface<IHandleRoutes>(true, true)
                    .Aggregate<IHandleRoutes, RouteHandlingDelegate>(
                        (controllerTypeFinal, httpAppFinal, requestFinal, routeNameFinal) =>
                        {
                            return request.CreateContentResponse(lookupSpaFile[fileName],
                                fileName.EndsWith(".js") ?
                                    "text/javascript"
                                    :
                                    fileName.EndsWith(".css") ?
                                        "text/css"
                                        :
                                        request.Headers.Accept.Any() ?
                                            request.Headers.Accept.First().MediaType
                                            :
                                            string.Empty).AsTask();
                        },
                        (callback, routeHandler) =>
                        {
                            return (controllerTypeCurrent, httpAppCurrent, requestCurrent, routeNameCurrent) =>
                                routeHandler.HandleRouteAsync(controllerTypeCurrent,
                                    httpAppCurrent, requestCurrent, routeNameCurrent,
                                    callback);
                        })
                    .Invoke(controllerType, httpApp, request, routeName);
            }
            var requestStart = request.RequestUri.AbsolutePath.ToLower();
            if (!firstSegments
                    .Where(firstSegment => requestStart.StartsWith($"/{firstSegment}"))
                    .Any())
                return request.CreateHtmlResponse(EastFive.Azure.Properties.Resources.indexPage);

            return await continuation(request, cancellationToken);
        }
    }
}

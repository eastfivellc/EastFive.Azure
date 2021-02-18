using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;
using System.Web;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using RestSharp.Extensions;
using RestSharp.Serialization.Json;

using EastFive;
using EastFive.Collections.Generic;
using EastFive.Serialization;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Web.Configuration;
using EastFive.Analytics;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Azure;
using EastFive.Api.Core;
using EastFive.Linq.Async;
using Newtonsoft.Json.Linq;
using EastFive.Api;
using Newtonsoft.Json;

namespace EastFive.Azure.Spa
{
    public class SpaHandler : IDisposable
    {
        private readonly RequestDelegate continueAsync;
        private IApplication app;

        private static ManualResetEvent signal = new ManualResetEvent(false);

        private static Dictionary<string, byte[]> lookupSpaFile;
        public static int? SpaMinimumVersion = default;
        private static Route[] routes;
        private static Route? defaultRoute;
        private static bool dynamicServe = false;

        private const string BuildJsonFileName = "build.json";

        private string[] firstSegments;

        public void Dispose()
        {
            if (signal != null)
            {
                signal.Dispose();
                signal = default;
            }
        }

        public SpaHandler(RequestDelegate next, IApplication app)
        {
            this.continueAsync = next;
            this.app = app;

            firstSegments = app.Resources
                .Where(route => !route.invokeResourceAttr.Namespace.IsNullOrWhiteSpace())
                .Select(
                    route => route.invokeResourceAttr.Namespace)
                .Distinct()
                .ToArray();

        }

        public static async Task<bool> SetupSpaAsync(IApplication application)
        {
            try
            {
                return await EastFive.Azure.Persistence.AppSettings.SpaStorage.ConfigurationString(
                    connectionString =>
                    {
                        return EastFive.Azure.AppSettings.SPA.IndexHtmlPath.ConfigurationString(
                            async indexHtmlPath =>
                            {
                                dynamicServe = EastFive.Azure.AppSettings.SPA.ServeEnabled.ConfigurationBoolean(
                                    ds => ds,
                                    onFailure: why => false,
                                    onNotSpecified: () => false);
                                bool success;
                                (success, SpaMinimumVersion, lookupSpaFile, routes, defaultRoute) = await LoadSpaAsync(
                                        application, connectionString, indexHtmlPath, dynamicServe);
                                return success;
                            },
                            (why) => false.AsTask());
                    },
                    why => false.AsTask());
            }
            catch(Exception)
            {
                return false;
            }
            finally
            {
                signal.Set();
            }
        }

        private static async Task<(bool, int?, Dictionary<string, byte[]>, Route[], Route?)> LoadSpaAsync(
            IApplication application, string connectionString, string indexHtmlPath, bool dynamicServe)
        {
            try
            {
                var blobClient = AzureTableDriverDynamic.FromStorageString(connectionString).BlobClient;
                var containerName = EastFive.Azure.Persistence.AppSettings.SpaContainer.ConfigurationString(name => name);
                var container = blobClient.GetBlobContainerClient(containerName);
                var blobRef = container.GetBlobClient("spa.zip");
                var blobStream = await blobRef.OpenReadAsync();

                using (var zipArchive = new ZipArchive(blobStream))
                {
                    var indexHTML = await zipArchive.Entries
                       .First(
                           (item, next) =>
                           {
                               if (string.Compare(item.FullName, indexHtmlPath, true) != 0)
                                   return next();
                               return item
                                   .Open()
                                   .ToBytesAsync();
                           },
                           () => new byte[] { }.AsTask());

                    var (minimumVersion, aliasPaths, defaultPath) = await zipArchive.Entries
                        .First(
                            async (item, next) =>
                            {
                                if (string.Compare(item.FullName, BuildJsonFileName, true) != 0)
                                    return await next();
                                var buildJsonEntryBytes = await item
                                    .Open()
                                    .ToBytesAsync();
                                var buildJsonString = buildJsonEntryBytes.GetString();
                                var buildJson = JsonConvert.DeserializeObject<SpaBuild>(buildJsonString);
                                var defaultRoute = buildJson.routes
                                    .Where(route => route.routePrefix == "*")
                                    .First(
                                        (rt, nx) => rt,
                                        () => default(Route?));
                                var routes = defaultRoute.HasValue?
                                    buildJson.routes
                                        .Where(route => route.routePrefix != defaultRoute.Value.routePrefix)
                                        .ToArray()
                                    :
                                    buildJson.routes;

                                var buildTime = (int)buildJson.buildTimeInSeconds;
                                return (buildTime, routes, defaultRoute);
                            },
                            () => (default(int?), default(Route[]), default(Route?)).AsTask());

                    var lookup = await EastFive.Azure.AppSettings.SPA.SiteLocation.ConfigurationString(
                        async (siteLocation) =>
                        {
                            application.Logger.Trace($"SpaHandlerModule - ExtractSpaFiles   siteLocation: {siteLocation}");
                            var spaFiles = await zipArchive.Entries
                                .Where(
                                    item =>
                                    {
                                        if (string.Compare(item.FullName, BuildJsonFileName) == 0)
                                            return false;
                                        if (dynamicServe)
                                            return string.Compare(item.FullName, indexHtmlPath, true) != 0;
                                        return true;
                                    })
                                .Select(
                                    async entity =>
                                    {
                                        var fileBytes = await entity.Open().ToBytesAsync();
                                        var finalBytes = entity.FullName.EndsWith(".js") ?
                                            fileBytes
                                                .GetString()
                                                .Replace("8FCC3D6A-9C25-4802-8837-16C51BE9FDBE.example.com", siteLocation)
                                                .GetBytes()
                                            :
                                            fileBytes;
                                        return entity.FullName.PairWithValue(finalBytes);
                                    })
                                .AsyncEnumerable()
                                .ToArrayAsync();
                            return spaFiles.ToDictionary();
                        },
                        (why) =>
                        {
                            application.Logger.Warning("Could not find SpaSiteLocation - is this key set in app settings?");
                            return new Dictionary<string, byte[]>().AsTask();
                        });
                    return (true, minimumVersion, lookup, aliasPaths, defaultPath);
                }
            }
            catch
            {
                return (true, default(int?), default, default, default);
            }
        }

        public async Task InvokeAsync(HttpContext context,
            Microsoft.AspNetCore.Hosting.IHostingEnvironment environment)
        {
            if (ShouldSkip())
            {
                await this.continueAsync(context);
                return;
            }
            var requestPath = context.Request.Path.Value;

            var taskToProcess = SpaHandler.routes
                .First(
                    (aliasPath, next) =>
                    {
                        if (requestPath.StartsWith(aliasPath.routePrefix, StringComparison.OrdinalIgnoreCase))
                            return ResolveFilePathAsync(aliasPath);

                        if (!context.Request.Headers.ContainsKey("Referer"))
                            return next();

                        var referers = context.Request.Headers["Referer"];
                        if (!referers.Any())
                            return next();

                        var referer = referers.First();
                        if (referer.EndsWith(aliasPath.routePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var referrerBasedFileName = $"{aliasPath.contentPath}{requestPath.TrimStart('/')}";
                            return ProcessAsync(referrerBasedFileName, aliasPath.defaultFile);
                        }

                        return next();
                    },
                    () =>
                    {
                        if(!defaultRoute.HasValue)
                            return this.continueAsync(context);
                        return ResolveFilePathAsync(defaultRoute.Value);
                    });
            await taskToProcess;
            return;

            Task ResolveFilePathAsync(Route route)
            {
                var fileName = ResolvePathFromRoute(route);
                return ProcessAsync(fileName, route.defaultFile);
            }

            string ResolvePathFromRoute(Route route)
            {
                var subPath = requestPath.Substring(route.routePrefix.Length);
                if (subPath.IsDefaultNullOrEmpty())
                    return route.indexFile;
                if (subPath == "/")
                    return route.indexFile;
                return $"{route.contentPath}{subPath}";
            }

            async Task ProcessAsync(string fileName, string defaultFile)
            {
                var request = context.GetHttpRequestMessage();
                if (FileIsInSpa())
                {
                    var response = ServeFromSpaZip(lookupSpaFile[fileName], fileName, request);
                    var immutableDays = EastFive.Azure.AppSettings.SPA.FilesExpirationInDays.ConfigurationDouble(
                        d => d,
                        onNotSpecified: () => 1.0);
                    response.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue()
                    {
                        MaxAge = TimeSpan.FromDays(immutableDays),
                        SharedMaxAge = TimeSpan.FromDays(immutableDays),
                        MustRevalidate = false,
                        NoCache = false,
                        NoStore = false,
                        NoTransform = true,
                        Private = false,
                        Public = true,
                    };

                    await response.WriteToContextAsync(context);
                    return;
                }

                if (dynamicServe)
                {
                    var responseHtml = request.CreateHtmlResponse(EastFive.Azure.Properties.Resources.indexPage);
                    await responseHtml.WriteToContextAsync(context);
                    return;
                }

                await ServerDefaultFile();

                async Task ServerDefaultFile()
                {
                    var response = ServeFromSpaZip(lookupSpaFile[defaultFile], defaultFile, request);
                    response.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue()
                    {
                        MaxAge = TimeSpan.FromSeconds(0.0),
                        SharedMaxAge = TimeSpan.FromSeconds(0.0),
                        MustRevalidate = true,
                        NoCache = true,
                        NoStore = true,
                        NoTransform = true,
                        Private = false,
                        Public = true,
                    };
                    response.Content.Headers.Expires = DateTime.UtcNow.AddDays(-1);
                    response.Headers.Pragma.Add(
                        new System.Net.Http.Headers.NameValueHeaderValue("no-cache"));
                    await response.WriteToContextAsync(context);
                    return;
                }

                bool FileIsInSpa()
                {
                    if (fileName.IsDefaultNullOrEmpty())
                        return false;
                    return lookupSpaFile.ContainsKey(fileName);
                }
            }

            HttpResponseMessage ServeFromSpaZip(byte[] fileData, string spaFileName,
                HttpRequestMessage request)
            {
                return request.CreateContentResponse(fileData,
                    spaFileName.EndsWith(".js") ?
                        "text/javascript"
                        :
                        spaFileName.EndsWith(".css") ?
                            "text/css"
                            :
                            spaFileName.EndsWith(".html") ?
                                "text/html"
                                :
                                request.Headers.Accept.Any() ?
                                    request.Headers.Accept.First().MediaType
                                    :
                                    string.Empty);
            }

            bool ShouldSkip()
            {
                if (lookupSpaFile.IsDefaultNullOrEmpty())
                    return true;
                if (!(this.app is IAzureApplication))
                    return true;

                var requestPath = context.Request.Path.Value;
                var isApiRequest = firstSegments
                    .Where(firstSegment => requestPath.StartsWith($"/{firstSegment}"))
                    .Any();
                return isApiRequest;
            }
        }

    }

    

    

}
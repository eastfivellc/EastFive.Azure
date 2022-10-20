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

using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

using EastFive;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Serialization;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using EastFive.Analytics;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Azure;
using EastFive.Api.Core;
using EastFive.Linq.Async;
using EastFive.Api;
using System.IO;

namespace EastFive.Azure.Spa
{
    public class SpaHandler : IDisposable
    {
        private readonly RequestDelegate continueAsync;
        private IApplication app;

        private static Task loadTask;
        private static ManualResetEvent signal = new ManualResetEvent(false);

        private static Dictionary<string, byte[]> lookupSpaFile;
        public static int? SpaMinimumVersion = default;
        private static Route[] routes;
        private static Route? defaultRoute;
        private static bool dynamicServe = false;

        private static IDictionary<string, string> extensionsMimeTypes =
            new Dictionary<string, string>()
            {
                { ".js", "text/javascript" },
                { ".css", "text/css" },
                { ".html", "text/html" },
                { ".svg", "image/svg+xml" },
                { ".png", "image/png" },
                { ".ico.", "image/x-icon" }
            };

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

        public static bool SetupSpa(IApplication application)
        {
            try
            {
                return EastFive.Azure.AppSettings.SPA.BuildConfigPath.ConfigurationString(
                    buildJsonPath =>
                    {
                        dynamicServe = EastFive.Azure.AppSettings.SPA.ServeEnabled.ConfigurationBoolean(
                            ds => ds,
                            onFailure: why => false,
                            onNotSpecified: () => false);


                        loadTask = Task.Run(
                            async () =>
                            {
                                var indexHtmlPath = EastFive.Azure.AppSettings.SPA.IndexHtmlPath.ConfigurationString(path => path, (why) => string.Empty);
                                return await await LoadSpaFile(
                                    async spaStream =>
                                    {
                                        bool success;
                                        (success, SpaMinimumVersion, lookupSpaFile, routes, defaultRoute) = await LoadSpaAsync(
                                            application, spaStream, indexHtmlPath, buildJsonPath, dynamicServe);
                                        signal.Set();
                                        return success;
                                    },
                                    () => false.AsTask());
                            });
                        return true;
                    },
                    (why) => false);
            }
            catch(Exception)
            {
                return false;
            }
            finally
            {
            }
        }

        public static Task<TResult> LoadSpaFile<TResult>(
            Func<Stream, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return EastFive.Azure.AppSettings.SPA.SpaStorage.ConfigurationString(
                async connectionString =>
                {
                    try
                    {
                        var blobClient = AzureTableDriverDynamic.FromStorageString(connectionString).BlobClient;
                        var containerName = Persistence.AppSettings.SpaContainer.ConfigurationString(name => name);
                        var container = blobClient.GetBlobContainerClient(containerName);
                        var blobRef = container.GetBlobClient("spa.zip");
                        var blobStream = await blobRef.OpenReadAsync();
                        return onFound(blobStream);
                    }
                    catch
                    {
                        return onNotFound();
                    }
                },
                why => onNotFound().AsTask());
        }

        private static async Task<(bool, int?, Dictionary<string, byte[]>, Route[], Route?)> LoadSpaAsync(
            IApplication application, Stream blobStream,
            string indexHtmlPath, string buildJsonPath, 
            bool dynamicServe)
        {
            try
            {
                using (var zipArchive = new ZipArchive(blobStream))
                {
                    var (minimumVersion, aliasPaths, defaultPath, indexFiles) = await zipArchive.Entries
                        .First(
                            async (item, next) =>
                            {
                                if (string.Compare(item.FullName, buildJsonPath, true) != 0)
                                    return await next();
                                var buildJsonEntryBytes = await item
                                    .Open()
                                    .ToBytesAsync();
                                var buildJsonString = buildJsonEntryBytes.GetString();
                                var buildJson = JsonConvert.DeserializeObject<SpaBuild>(buildJsonString);
                                var defaultRoute = buildJson.routes
                                    .NullToEmpty()
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
                                var indexFiles = buildJson.routes
                                    .NullToEmpty()
                                    .Select(route => route.indexFile)
                                    .SelectWhereNotNull()
                                    .ToArray();

                                SpaHandler.extensionsMimeTypes = buildJson.mimeTypes
                                    .NullToEmpty()
                                    .Select(mimeType => mimeType.extension.PairWithValue(mimeType.mimeType))
                                    .Concat(SpaHandler.extensionsMimeTypes)
                                    .Distinct(kvp => kvp.Key)
                                    .ToDictionary();

                                var buildTime = (int)buildJson.buildTimeInSeconds;
                                return (buildTime, routes, defaultRoute, indexFiles);
                            },
                            () => (default(int?), default(Route[]), default(Route?), default(string[])).AsTask());

                    var lookup = await EastFive.Azure.AppSettings.SPA.SiteLocation.ConfigurationString(
                        async (siteLocation) =>
                        {
                            application.Logger.Trace($"SpaHandlerModule - ExtractSpaFiles   siteLocation: {siteLocation}");
                            var spaFiles = await zipArchive.Entries
                                .Where(
                                    item =>
                                    {
                                        if (string.Compare(item.FullName, buildJsonPath) == 0)
                                            return false;
                                        if (dynamicServe)
                                            return string.Compare(item.FullName, indexHtmlPath, true) != 0;
                                        return true;
                                    })
                                .Select(
                                    async entity =>
                                    {
                                        var fileBytes = await entity.Open().ToBytesAsync();
                                        if (!indexFiles.Contains(entity.FullName, StringComparison.OrdinalIgnoreCase))
                                            return entity.FullName.PairWithValue(fileBytes);

                                        var aiInstrumentationKey = EastFive.Azure.AppSettings.ApplicationInsights.InstrumentationKey.ConfigurationString(
                                            (value) => value,
                                            (missingKey) => string.Empty);
                                        var aiConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING".ConfigurationString(
                                            (value) => value,
                                            (missingKey) => string.Empty);
                                        return fileBytes
                                            .GetString()
                                            .Replace("2734e0b8-5801-4b33-86a1-e5ae322399d6", aiInstrumentationKey, StringComparison.OrdinalIgnoreCase)
                                            .Replace("f50751c1-b373-4fa8-8f27-1a7242e1ac79", aiConnectionString, StringComparison.OrdinalIgnoreCase)
                                            .GetBytes()
                                            .PairWithKey(entity.FullName);
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

        public Task InvokeAsync(HttpContext context,
            Microsoft.AspNetCore.Hosting.IHostingEnvironment environment)
        {
            if (ShouldSkip())
                return this.continueAsync(context);

            var requestPath = context.Request.Path.Value;

            return FileFromPath(requestPath,
                onResolved: (prefix, fileData, fileName, cacheControl, expiration) =>
                {
                    context.Response.GetTypedHeaders().CacheControl = cacheControl;
                    context.Response.GetTypedHeaders().Expires = expiration;
                    return ServeFromSpaZipAsync(fileData, fileName, context);
                },
                onDidNotResolve: () =>
                 {
                     return this.continueAsync(context);
                 });

            bool ShouldSkip()
            {
                var requestPath = context.Request.Path.Value;
                var isApiRequest = firstSegments
                    .Where(firstSegment => requestPath.StartsWith($"/{firstSegment}"))
                    .Any();
                if (isApiRequest)
                    return true;

                var systemReady = signal.WaitOne();
                if (!systemReady)
                    return true;

                if (lookupSpaFile.IsDefaultNullOrEmpty())
                    return true;

                var isAzureApp = this.app is IAzureApplication;
                return !isAzureApp;
            }
            
        }

        public static TResult FileFromPath<TResult>(string requestPath,
            Func<
                    string,
                    byte [], 
                    string,
                    Microsoft.Net.Http.Headers.CacheControlHeaderValue, 
                    DateTimeOffset?,
                TResult> onResolved,
            Func<TResult> onDidNotResolve)
        {
            signal.WaitOne();
            return SpaHandler.routes
                .NullToEmpty()
                .First(
                    (aliasPath, next) =>
                    {
                        if (!requestPath.StartsWith(aliasPath.routePrefix, StringComparison.OrdinalIgnoreCase))
                            return next();

                        return LoadFile(aliasPath);
                    },
                    () =>
                    {
                        if (!defaultRoute.HasValue)
                            return onDidNotResolve();

                        return LoadFile(defaultRoute.Value);
                    });

            bool FileIsInSpa(string fileName, out string fileNameSanitized)
            {
                if (fileName.IsDefaultNullOrEmpty())
                {
                    fileNameSanitized = default;
                    return false;
                }
                fileNameSanitized = fileName.Replace("//", "/");
                return lookupSpaFile.ContainsKey(fileNameSanitized);
            }

            TResult LoadFile(Route route)
            {
                var fileName = route.ResolveRoute(requestPath);
                var defaultFileName = route.defaultFile;
                var location = route.ResolveLocation(fileName);
                if (FileIsInSpa(fileName, out string fileNameSanitized))
                {
                    var immutableDays = EastFive.Azure.AppSettings.SPA.FilesExpirationInDays.ConfigurationDouble(
                        d => d,
                        onNotSpecified: () => 1.0);
                    var cacheControl =
                        new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
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
                    
                    return onResolved(location, 
                        lookupSpaFile[fileNameSanitized], fileName.Split('/').Last(), 
                        cacheControl, default);
                }

                var defaultCacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
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

                var expiresDefault = DateTime.UtcNow.AddDays(-1);
                return onResolved(location,
                    lookupSpaFile[defaultFileName], defaultFileName.Split('/').Last(),
                    defaultCacheControl, expiresDefault);
            }

        }

        public static async Task ServeFromSpaZipAsync(byte[] fileData, string spaFileName,
            HttpContext context)
        {
            var request = context.Request;
            var acceptHeaders = request.GetTypedHeaders().Accept;

            var mimeType = acceptHeaders.Any() ?
                extensionsMimeTypes
                    .Where(kvp => spaFileName.EndsWith(kvp.Key))
                    .First(
                        (kvp, next) => kvp.Value,
                        () =>
                        {
                            return acceptHeaders.First().MediaType.ToString();
                        })
                    :
                    string.Empty;
            context.Response.StatusCode = 200;
            if (!mimeType.IsDefaultNullOrEmpty())
                context.Response.ContentType = mimeType;
            context.Response.ContentLength = fileData.Length;
            context.Response.Headers.Add("Content-Disposition", $"filename=\"{spaFileName}\"");
            await context.Response.Body.WriteAsync(fileData);
        }

        public static byte[] GetSpaFile(string path)
        {
            signal.WaitOne();
            return lookupSpaFile[path];
        }
    }
}
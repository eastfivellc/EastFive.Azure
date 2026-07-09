using System;
using System.IO;
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

namespace EastFive.Azure.Spa
{
    public class SpaHandler : IDisposable
    {
        private class SpaPackage
        {
            public string zipName;
            public Dictionary<string, byte[]> files;
            public Route[] routes;
            public Route? defaultRoute;
            public int? minimumVersion;
        }

        public const string PrimaryPackageZipName = "spa.zip";

        private readonly RequestDelegate continueAsync;
        private IApplication app;

        private static Task loadTask;
        private static ManualResetEvent signal = new ManualResetEvent(false);

        private static SpaPackage primaryPackage;
        private static SpaPackage[] additionalPackages = new SpaPackage[] { };
        public static int? SpaMinimumVersion = default;
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
        private HashSet<string> apiNamespaceRoutes;

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

            apiNamespaceRoutes = app.Resources
                .Where(route => !route.invokeResourceAttr.Namespace.IsNullOrWhiteSpace())
                .Where(route => !route.invokeResourceAttr.Route.IsNullOrWhiteSpace())
                .Select(
                    route => $"/{route.invokeResourceAttr.Namespace}/{route.invokeResourceAttr.Route}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

                        var additionalZipNames = EastFive.Azure.AppSettings.SPA.AdditionalPackages.ConfigurationString(
                            packages => packages
                                .Split(',')
                                .Select(package => package.Trim())
                                .Where(package => !package.IsNullOrWhiteSpace())
                                .ToArray(),
                            (why) => new string[] { });

                        loadTask = Task.Run(
                            async () =>
                            {
                                var indexHtmlPath = EastFive.Azure.AppSettings.SPA.IndexHtmlPath.ConfigurationString(path => path, (why) => string.Empty);
                                var packages = await new string[] { PrimaryPackageZipName }
                                    .Concat(additionalZipNames)
                                    .Select(
                                        zipName => LoadPackageAsync(application,
                                            zipName, indexHtmlPath, buildJsonPath, dynamicServe))
                                    .AsyncEnumerable()
                                    .ToArrayAsync();

                                primaryPackage = packages[0];
                                additionalPackages = packages
                                    .Skip(1)
                                    .Where(package => !package.IsDefaultOrNull())
                                    .ToArray();
                                SpaMinimumVersion = primaryPackage?.minimumVersion;

                                foreach (var package in additionalPackages)
                                    if (package.defaultRoute.HasValue)
                                        application.Logger.Warning(
                                            $"SpaHandler - Ignoring catch-all ('*') route in additional package `{package.zipName}`;" +
                                            " only the primary package may declare a default route.");

                                signal.Set();
                                return !primaryPackage.IsDefaultOrNull();
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

        private static async Task<SpaPackage> LoadPackageAsync(IApplication application,
            string zipName, string indexHtmlPath, string buildJsonPath, bool dynamicServe)
        {
            return await await LoadSpaFile(zipName,
                async spaStream =>
                {
                    var (success, minimumVersion, files, routes, defaultRoute) = await LoadSpaAsync(
                        application, spaStream, indexHtmlPath, buildJsonPath, dynamicServe);
                    if (!success)
                        return default(SpaPackage);
                    return new SpaPackage
                    {
                        zipName = zipName,
                        files = files,
                        routes = routes,
                        defaultRoute = defaultRoute,
                        minimumVersion = minimumVersion,
                    };
                },
                () =>
                {
                    application.Logger.Warning($"SpaHandler - Could not load SPA package `{zipName}`.");
                    return default(SpaPackage).AsTask();
                });
        }

        public static Task<TResult> LoadSpaFile<TResult>(
            Func<Stream, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return LoadSpaFile(PrimaryPackageZipName, onFound, onNotFound);
        }

        public static Task<TResult> LoadSpaFile<TResult>(string zipName,
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
                        var blobRef = container.GetBlobClient(zipName);
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

        public static Task<TResult> SaveSpaFile<TResult>(string zipName, byte[] packageBytes,
            Func<TResult> onSaved,
            Func<string, TResult> onFailure)
        {
            return EastFive.Azure.AppSettings.SPA.SpaStorage.ConfigurationString(
                async connectionString =>
                {
                    try
                    {
                        var blobClient = AzureTableDriverDynamic.FromStorageString(connectionString).BlobClient;
                        var containerName = Persistence.AppSettings.SpaContainer.ConfigurationString(name => name);
                        var container = blobClient.GetBlobContainerClient(containerName);
                        await container.CreateIfNotExistsAsync();
                        var blobRef = container.GetBlobClient(zipName);
                        using (var packageStream = new MemoryStream(packageBytes))
                        {
                            await blobRef.UploadAsync(packageStream, overwrite: true);
                        }
                        return onSaved();
                    }
                    catch (Exception ex)
                    {
                        return onFailure(ex.Message);
                    }
                },
                why => onFailure(why).AsTask());
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
                        .Where(entry => string.Compare(entry.FullName, buildJsonPath, true) == 0)
                        .First(
                            async (item, next) =>
                            {
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
                                        var aiConnectionString = EastFive.Azure.AppSettings.ApplicationInsights.ConnectionString.ConfigurationString(
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
            Microsoft.AspNetCore.Hosting.IWebHostEnvironment environment)
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
                if (IsApiRequest(requestPath))
                    return true;

                var systemReady = signal.WaitOne();
                if (!systemReady)
                    return true;

                var anyFilesLoaded = additionalPackages
                    .NullToEmpty()
                    .Append(primaryPackage)
                    .Where(package => !package.IsDefaultOrNull())
                    .Any(package => !package.files.IsDefaultNullOrEmpty());
                if (!anyFilesLoaded)
                    return true;

                var isAzureApp = this.app is IAzureApplication;
                return !isAzureApp;
            }

            // A namespace alone (e.g. /admin) is only an API request when no SPA
            // route claims it; /namespace/Route paths always belong to the API so
            // resources sharing a prefix with a SPA package keep working.
            bool IsApiRequest(string path)
            {
                var spaServesPath = AnyRoutePrefixMatches(path);
                if (!spaServesPath)
                    return firstSegments
                        .Where(firstSegment => path.StartsWith($"/{firstSegment}"))
                        .Any();

                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segments.Length >= 2
                    && apiNamespaceRoutes.Contains($"/{segments[0]}/{segments[1]}");
            }
        }

        private static bool AnyRoutePrefixMatches(string requestPath)
        {
            signal.WaitOne();
            return additionalPackages
                .NullToEmpty()
                .Where(package => !package.IsDefaultOrNull())
                .Concat(new[] { primaryPackage }.Where(package => !package.IsDefaultOrNull()))
                .SelectMany(package => package.routes.NullToEmpty())
                .Any(route => requestPath.StartsWith(route.routePrefix, StringComparison.OrdinalIgnoreCase));
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
            return additionalPackages
                .NullToEmpty()
                .Where(package => !package.IsDefaultOrNull())
                .SelectMany(package => package.routes
                    .NullToEmpty()
                    .Select(route => (package, route)))
                .Concat(primaryPackage.IsDefaultOrNull()?
                    new (SpaPackage package, Route route)[] { }
                    :
                    primaryPackage.routes
                        .NullToEmpty()
                        .Select(route => (package: primaryPackage, route)))
                .First(
                    (packageRoute, next) =>
                    {
                        if (!requestPath.StartsWith(packageRoute.route.routePrefix, StringComparison.OrdinalIgnoreCase))
                            return next();

                        return LoadFile(packageRoute.package, packageRoute.route);
                    },
                    () =>
                    {
                        if (primaryPackage.IsDefaultOrNull())
                            return onDidNotResolve();

                        if (!primaryPackage.defaultRoute.HasValue)
                            return onDidNotResolve();

                        return LoadFile(primaryPackage, primaryPackage.defaultRoute.Value);
                    });

            bool FileIsInSpa(SpaPackage package, string fileName, out string fileNameSanitized)
            {
                if (fileName.IsDefaultNullOrEmpty())
                {
                    fileNameSanitized = default;
                    return false;
                }
                fileNameSanitized = fileName.Replace("//", "/");
                if (package.files.IsDefaultNullOrEmpty())
                    return false;
                return package.files.ContainsKey(fileNameSanitized);
            }

            TResult LoadFile(SpaPackage package, Route route)
            {
                var fileName = route.ResolveRoute(requestPath);
                var defaultFileName = route.defaultFile;
                var location = route.ResolveLocation(fileName);
                if (FileIsInSpa(package, fileName, out string fileNameSanitized))
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
                        package.files[fileNameSanitized], fileName.Split('/').Last(), 
                        cacheControl, default);
                }

                if (defaultFileName.IsDefaultNullOrEmpty())
                    return onDidNotResolve();

                if (package.files.IsDefaultNullOrEmpty() || !package.files.ContainsKey(defaultFileName))
                    return onDidNotResolve();

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
                    package.files[defaultFileName], defaultFileName.Split('/').Last(),
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
            context.Response.Headers["Content-Disposition"] = $"filename=\"{spaFileName}\"";
            await context.Response.Body.WriteAsync(fileData);
        }

        public static byte[] GetSpaFile(string path)
        {
            signal.WaitOne();
            return new SpaPackage[] { primaryPackage }
                .Concat(additionalPackages.NullToEmpty())
                .Where(package => !package.IsDefaultOrNull())
                .Where(package => !package.files.IsDefaultNullOrEmpty())
                .Where(package => package.files.ContainsKey(path))
                .Select(package => package.files[path])
                .First();
        }

        public struct PackageStatus
        {
            public string zipName;
            public bool loaded;
            public int fileCount;
            public string[] routePrefixes;
            public bool hasDefaultRoute;
        }

        public static PackageStatus[] GetPackageStatuses()
        {
            signal.WaitOne();
            return new (string zipName, SpaPackage package)[]
                    { (PrimaryPackageZipName, primaryPackage) }
                .Concat(additionalPackages
                    .NullToEmpty()
                    .Select(package => (package.zipName, package)))
                .Select(
                    item => new PackageStatus
                    {
                        zipName = item.zipName,
                        loaded = !item.package.IsDefaultOrNull(),
                        fileCount = item.package.IsDefaultOrNull() ?
                            0 : item.package.files.NullToEmpty().Count(),
                        routePrefixes = item.package.IsDefaultOrNull() ?
                            new string[] { }
                            :
                            item.package.routes
                                .NullToEmpty()
                                .Select(route => route.routePrefix)
                                .ToArray(),
                        hasDefaultRoute = !item.package.IsDefaultOrNull()
                            && item.package.defaultRoute.HasValue,
                    })
                .ToArray();
        }
    }
}
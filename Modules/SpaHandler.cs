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
using EastFive.Api.Azure.Controllers;
using EastFive.Azure;
using EastFive.Api.Core;

namespace EastFive.Api.Azure.Modules
{
    public class SpaHandler : IDisposable
    {
        private readonly RequestDelegate continueAsync;
        private IApplication app;

        private const string IndexHTMLFileName = "index.html";
        private static byte[] indexHTML;
        private static ManualResetEvent signal = new ManualResetEvent(false);

        private Dictionary<string, byte[]> lookupSpaFile;
        //private string[] firstSegments;

        private const string BuildJsonFileName = "content/build.json";

        private string[] firstSegments;
        private bool dynamicServe = false;

        public void Dispose()
        {
            if (signal != null)
            {
                signal.Dispose();
                signal = default;
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

        public SpaHandler(RequestDelegate next, IApplication app)
        {
            this.continueAsync = next;
            this.app = app;

            dynamicServe = EastFive.Azure.AppSettings.SpaServeEnabled.ConfigurationBoolean(
                ds => ds,
                onFailure: why => false,
                onNotSpecified: () => false);
            // TODO: A better job of matching that just grabbing the first segment
            firstSegments = app.Resources
                .Where(route => !route.invokeResourceAttr.Namespace.IsNullOrWhiteSpace())
                .Select(
                    route => route.invokeResourceAttr.Namespace)
                .Distinct()
                .ToArray();

            ExtractSpaFiles(app);

        }

        public static int? SpaMinimumVersion = default;
        
        private void ExtractSpaFiles(IApplication application)
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
                            var container = blobClient.GetBlobContainerClient(containerName);
                            var blobRef = container.GetBlobClient("spa.zip");
                            var blobStream = blobRef.OpenReadAsync().Result;

                            using (zipArchive = new ZipArchive(blobStream))
                            {
                                indexHTML = zipArchive.Entries
                                    .First(item => string.Compare(item.FullName, IndexHTMLFileName, true) == 0)
                                    .Open()
                                    .ToBytesAsync()
                                    .Result;

                                var buildJsonEntries = zipArchive.Entries
                                    .Where(item => string.Compare(item.FullName, BuildJsonFileName, true) == 0);
                                if (buildJsonEntries.Any())
                                {
                                    var buildJsonString = buildJsonEntries
                                        .First()
                                        .Open()
                                        .ToBytes(b => b)
                                        .GetString();
                                    dynamic buildJson = Newtonsoft.Json.JsonConvert.DeserializeObject(buildJsonString);
                                    SpaMinimumVersion = (int)buildJson.buildTimeInSeconds;
                                }
                                
                                lookupSpaFile = EastFive.Azure.AppSettings.SpaSiteLocation.ConfigurationString(
                                    (siteLocation) =>
                                    {
                                        application.Logger.Trace($"SpaHandlerModule - ExtractSpaFiles   siteLocation: {siteLocation}");
                                        return zipArchive.Entries
                                            .Where(
                                                item =>
                                                {
                                                    if (dynamicServe)
                                                        return string.Compare(item.FullName, IndexHTMLFileName, true) != 0;
                                                    return true;
                                                })
                                            .Select(
                                                entity =>
                                                {
                                                    if (!entity.FullName.EndsWith(".js"))
                                                        return entity.FullName.PairWithValue(entity.Open().ToBytesAsync().Result);

                                                    var fileBytes = entity.Open()
                                                        .ToBytesAsync().Result
                                                        .GetString()
                                                        .Replace("8FCC3D6A-9C25-4802-8837-16C51BE9FDBE.example.com", siteLocation)
                                                        .GetBytes();

                                                    return entity.FullName.PairWithValue(fileBytes);
                                                })
                                            .ToDictionary();
                                    },
                                    (why) =>
                                    {
                                        application.Logger.Warning("Could not find SpaSiteLocation - is this key set in app settings?");
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

        public async Task InvokeAsync(HttpContext context,
            Microsoft.AspNetCore.Hosting.IHostingEnvironment environment)
        {
            if (lookupSpaFile.IsDefaultNullOrEmpty())
            {
                await this.continueAsync(context);
                return;
            }

            if (!(this.app is IAzureApplication))
            {
                await this.continueAsync(context);
                return;
            }

            //string filePath = context.Request.FilePath;
            var fileName = context.Request.Path.Value
                .Split('/'.AsArray())
                .First(
                    (paths, next) => paths,
                    () => string.Empty); ;

            var request = context.GetHttpRequestMessage();
            if (lookupSpaFile.ContainsKey(fileName))
            {
                var response = await ServeFromSpaZip(fileName);
                var immutableDays = EastFive.Azure.AppSettings.SpaFilesExpirationInDays.ConfigurationDouble(
                    d => d,
                    (why) => 1.0);
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
            
            var requestStart = request.RequestUri.AbsolutePath.ToLower();
            if (!firstSegments
                    .Where(firstSegment => requestStart.StartsWith($"/{firstSegment}"))
                    .Any())
            {
                if (dynamicServe)
                {
                    var responseHtml = request.CreateHtmlResponse(EastFive.Azure.Properties.Resources.indexPage);
                    await responseHtml.WriteToContextAsync(context);
                    return;
                }

                var response = await ServeFromSpaZip("index.html");
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

            await this.continueAsync(context);
            return;

            async Task<HttpResponseMessage> ServeFromSpaZip(string spaFileName)
            {
                return await request.CreateContentResponse(lookupSpaFile[spaFileName],
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
                                            string.Empty).AsTask();
            }

        }
    }

    public static class ModulesExtensions
    {
        public static IApplicationBuilder UseSpaHandler(
            this IApplicationBuilder builder, IApplication app)
        {
            return builder.UseMiddleware<EastFive.Api.Azure.Modules.SpaHandler>(app);
        }
    }
}
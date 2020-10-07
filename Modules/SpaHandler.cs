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
            ExtractSpaFiles(app);
        }

        //public SpaHandler(AzureApplication httpApp, System.Web.Http.HttpConfiguration config)
        //{
        //    // TODO: A better job of matching that just grabbing the first segment
        //    firstSegments = System.Web.Routing.RouteTable.Routes
        //        .Where(route => route is System.Web.Routing.Route)
        //        .Select(route => route as System.Web.Routing.Route)
        //        .Where(route => !route.Url.IsNullOrWhiteSpace())
        //        .Select(
        //            route => route.Url.Split(new char[] { '/' }).First())
        //        .ToArray();

        //    ExtractSpaFiles(httpApp);
        //}

        //public SpaHandler(AzureApplication httpApp, System.Web.Http.HttpConfiguration config,
        //    HttpMessageHandler handler)
        //    : base(config, handler)
        //{
        //    // TODO: A better job of matching that just grabbing the first segment
        //    firstSegments = System.Web.Routing.RouteTable.Routes
        //        .Where(route => route is System.Web.Routing.Route)
        //        .Select(route => route as System.Web.Routing.Route)
        //        .Where(route => !route.Url.IsNullOrWhiteSpace())
        //        .Select(
        //            route => route.Url.Split(new char[] { '/' }).First())
        //        .ToArray();

        //    ExtractSpaFiles(httpApp);
        //}

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
                            var container = blobClient.GetContainerReference(containerName);
                            var blobRef = container.GetBlockBlobReference("spa.zip");
                            var blobStream = blobRef.OpenReadAsync().Result;

                            using (zipArchive = new ZipArchive(blobStream))
                            {
                                indexHTML = zipArchive.Entries
                                    .First(item => string.Compare(item.FullName, IndexHTMLFileName, true) == 0)
                                    .Open()
                                    .ToBytesAsync()
                                    .Result;

                                lookupSpaFile = EastFive.Azure.AppSettings.SpaSiteLocation.ConfigurationString(
                                    (siteLocation) =>
                                    {
                                        application.Logger.Trace($"SpaHandlerModule - ExtractSpaFiles   siteLocation: {siteLocation}");
                                        return zipArchive.Entries
                                            .Where(item => string.Compare(item.FullName, IndexHTMLFileName, true) != 0)
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
                var response = request
                    .CreateContentResponse(lookupSpaFile[fileName],
                        fileName.EndsWith(".js") ?
                            "text/javascript"
                            :
                            fileName.EndsWith(".css") ?
                                "text/css"
                                :
                                request.Headers.Accept.Any() ?
                                    request.Headers.Accept.First().MediaType
                                    :
                                    string.Empty);
                await response.WriteToContextAsync(context);
                return;
            }

            await continueAsync(context);
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
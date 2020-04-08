using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Net.Http;
using System.Threading;

using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using EastFive.Serialization;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Api.Core;
using BlackBarLabs.Web;
using EastFive.Api;
using EastFive.Linq;
using EastFive.Web.Configuration;
using EastFive.Analytics;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Azure;

namespace EastFive.Api.Azure.Modules
{
    public class SpaHandler
    {
        private readonly RequestDelegate continueAsync;
        internal const string IndexHTMLFileName = "index.html";

        private Dictionary<string, byte[]> lookupSpaFile;
        static internal byte[] indexHTML;
        private string[] firstSegments;
        private IApplication app;

        public SpaHandler(RequestDelegate next, IApplication app)
        {
            this.continueAsync = next;
            this.app = app;
            ExtractSpaFiles(app);
        }
        
        private void ExtractSpaFiles(IApplication application)
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

                        zipArchive = new ZipArchive(blobStream);
                    }
                    catch
                    {
                        indexHTML = System.Text.Encoding.UTF8.GetBytes("SPA Not Installed");
                        return false;
                    }
                    
                    using (zipArchive)
                    {
                        
                        indexHTML = zipArchive.Entries
                            .First(item => string.Compare(item.FullName, IndexHTMLFileName, true) == 0)
                            .Open()
                            .ToBytesAsync().Result;
                        
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

                },
                why => false);
        }

        public async Task InvokeAsync(HttpContext context,
            Microsoft.AspNetCore.Hosting.IHostingEnvironment environment)
        {
            //if (!request.RequestUri.IsDefaultOrNull())
            //{
            //    if (request.RequestUri.PathAndQuery.HasBlackSpace())
            //    {
            //        if (request.RequestUri.PathAndQuery.Contains("apple-app-site-association"))
            //        {
            //            return await continuation(request, cancellationToken);
            //        }
            //    }
            //}

            if (lookupSpaFile.IsDefaultNullOrEmpty())
            {
                await this.continueAsync(context);
                return;
            }

            var fileName = context.Request.Path.Value
                .Split('/'.AsArray())
                .First(
                    (paths, next) => paths,
                    () => string.Empty); ;

            if (!(this.app is IAzureApplication))
            {
                await this.continueAsync(context);
                return;
            }
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

            var requestStart = request.RequestUri.AbsolutePath.ToLower();
            if (!firstSegments
                    .Where(firstSegment => requestStart.StartsWith($"/{firstSegment}"))
                    .Any())
            {
                var response = request.CreateHtmlResponse(EastFive.Azure.Properties.Resources.indexPage);
                await response.WriteToContextAsync(context);
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

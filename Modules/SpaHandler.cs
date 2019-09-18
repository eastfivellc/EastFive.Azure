﻿using BlackBarLabs.Extensions;
using EastFive.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using EastFive.Serialization;
using System.Net.NetworkInformation;
using EastFive.Extensions;
using BlackBarLabs.Web;
using Microsoft.ApplicationInsights;
using EastFive.Api;
using System.Net.Http;
using System.Threading;
using BlackBarLabs.Api;
using EastFive.Linq;
using EastFive.Web.Configuration;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Api.Azure.Modules
{
    public class SpaHandler : EastFive.Api.Modules.ApplicationHandler
    {
        internal const string IndexHTMLFileName = "index.html";

        private Dictionary<string, byte[]> lookupSpaFile;
        static internal byte[] indexHTML;
        private string[] firstSegments;

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
        
        private void ExtractSpaFiles(AzureApplication application)
        {
            ZipArchive zipArchive = null;
            try
            {
                var blobClient = EastFive.Azure.Persistence.AppSettings.Storage.ConfigurationString(connectionString => AzureTableDriverDynamic.FromStorageString(connectionString).BlobClient);
                var container = blobClient.GetContainerReference("spa");
                var blobRef = container.GetBlockBlobReference("spa.zip");
                var blobStream = blobRef.OpenRead();

                zipArchive = new ZipArchive(blobStream);
            }
            catch
            {
                indexHTML = System.Text.Encoding.UTF8.GetBytes("SPA Not Installed");
                return;
            }

            using (zipArchive)
            {

                indexHTML = zipArchive.Entries
                    .First(item => string.Compare(item.FullName, IndexHTMLFileName, true) == 0)
                    .Open()
                    .ToBytes();

                lookupSpaFile = ConfigurationContext.Instance.GetSettingValue(EastFive.Azure.AppSettings.SpaSiteLocation,
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
                    () =>
                    {
                        application.Telemetry.TrackException(new ArgumentNullException("Could not find SpaSiteLocation - is this key set in app settings?"));
                        return new Dictionary<string, byte[]>();
                    });
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
                return request.CreateContentResponse(lookupSpaFile[fileName],
                    fileName.EndsWith(".js")?
                        "text/javascript"
                        :
                        fileName.EndsWith(".css")?
                            "text/css"
                            :
                            request.Headers.Accept.Any()?
                                request.Headers.Accept.First().MediaType
                                :
                                string.Empty);

            var requestStart = request.RequestUri.AbsolutePath.ToLower();
            if (!firstSegments
                    .Where(firstSegment => requestStart.StartsWith($"/{firstSegment}"))
                    .Any())
                return request.CreateHtmlResponse(EastFive.Azure.Properties.Resources.indexPage);

            return await continuation(request, cancellationToken);
        }
    }
}

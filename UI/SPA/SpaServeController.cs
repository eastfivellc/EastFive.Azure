using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

using EastFive.Extensions;
using EastFive.Api.Azure.Modules;
using EastFive.Api;
using EastFive.Web.Configuration;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Azure.Auth;

namespace EastFive.Azure.Spa
{
    [FunctionViewController(
        Route = "Spa",
        Namespace = "Publish")]
    public class SpaServeController
    {
        public const string BoundAction = "bounce";

        [HttpAction(method: BoundAction)]
        [SuperAdminClaim]
        public static IHttpResponse RedeemAsync(
                IApplication app,
                EastFive.Api.Security security,
            NoContentResponse onBounced)
        {
            SpaHandler.SetupSpa(app);
            return onBounced();
        }

        public const string DownloadAction = "download";

        [HttpAction(method: DownloadAction)]
        [SuperAdminClaim]
        public static Task<IHttpResponse> DownloadAsync(
                [OptionalQueryParameter(Name = PackageParameterName)] string package,
                EastFive.Api.Security security,
            StreamResponse onFound,
            NotFoundResponse onNotFound)
        {
            var zipName = package.HasBlackSpace() ? package : SpaHandler.PrimaryPackageZipName;
            return SpaHandler.LoadSpaFile(zipName,
                (blobStream) => onFound(blobStream),
                () => onNotFound());
        }

        public const string PackagesAction = "packages";

        /// <summary>Diagnostic: report which SPA packages are loaded and their routes.</summary>
        [HttpAction(method: PackagesAction)]
        [SuperAdminClaim(AllowLocalHost = true)]
        public static IHttpResponse Packages(
                IApplication app,
            ContentTypeResponse<SpaHandler.PackageStatus[]> onStatus)
        {
            return onStatus(SpaHandler.GetPackageStatuses());
        }

        public const string UploadAction = "upload";
        public const string PackageParameterName = "package";
        public const string ContentParameterName = "content";

        private static readonly Regex ValidPackageName =
            new Regex(@"^[A-Za-z0-9._-]+\.zip$", RegexOptions.Compiled);

        /// <summary>
        /// Upload a SPA package zip directly into the configured SPA blob container
        /// and hot-reload the SPA packages. Intended for local development and
        /// test/postman driven deployment of packages such as <c>admin.zip</c>.
        /// POST multipart/form-data with a `content` file field;
        /// `?package=admin.zip` selects the target blob (default spa.zip).
        /// </summary>
        [HttpAction("POST", UploadAction)]
        [SuperAdminClaim(AllowLocalHost = true)]
        public static async Task<IHttpResponse> UploadAsync(
                [Property(Name = ContentParameterName)] byte[] packageBytes,
                [OptionalQueryParameter(Name = PackageParameterName)] string package,
                IApplication app,
            CreatedResponse onUploaded,
            GeneralConflictResponse onFailure)
        {
            var zipName = package.HasBlackSpace() ? package : SpaHandler.PrimaryPackageZipName;
            if (!ValidPackageName.IsMatch(zipName))
                return onFailure($"Invalid package name `{zipName}`; expected a flat name ending in .zip");

            if (packageBytes.IsDefaultNullOrEmpty())
                return onFailure("No package content was provided.");

            var buildConfigPath = EastFive.Azure.AppSettings.SPA.BuildConfigPath.ConfigurationString(
                path => path,
                (why) => "build.json");
            try
            {
                using (var archive = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read))
                {
                    var containsBuildConfig = archive.Entries
                        .Any(entry => string.Compare(entry.FullName, buildConfigPath, true) == 0);
                    if (!containsBuildConfig)
                        return onFailure($"Package does not contain the build config file `{buildConfigPath}`.");
                }
            }
            catch (InvalidDataException)
            {
                return onFailure("Uploaded content is not a valid zip archive.");
            }

            return await SpaHandler.SaveSpaFile(zipName, packageBytes,
                onSaved: () =>
                {
                    SpaHandler.SetupSpa(app);
                    return onUploaded();
                },
                onFailure: why => onFailure(why));
        }

        //[HttpGet]
        //public static IHttpResponse Get(
        //    [QueryId]string id,
        //    IHttpRequest request,
        //    HtmlResponse onNoIndexFile)
        //{
        //    //var indexFile = SpaHandlerModule.indexHTML;
        //    var indexFile = Modules.SpaHandler.IndexHTML;

        //    var doc = new HtmlDocument();
        //    //doc.LoadHtml(indexFile.ToString());

        //    if (indexFile.IsDefaultOrNull())
        //        return onNoIndexFile("<html><body>No Index File</body></html>");

        //    try
        //    {
        //        using (var fileStream = new MemoryStream(indexFile))
        //        {
        //            doc.Load(fileStream);
        //            var head = doc.DocumentNode.SelectSingleNode("//head").InnerHtml;
        //            var body = doc.DocumentNode.SelectSingleNode("//body").ChildNodes
        //                .AsHtmlNodes()
        //                .Where(node => node.Name.ToLower() != "script")
        //                .Select(node => node.OuterHtml)
        //                .Join(" ");

        //            var scripts = doc.DocumentNode.SelectNodes("//script");

        //            var scriptList = scripts
        //                .Select(
        //                    script =>
        //                    {
        //                        var attrs = script.Attributes
        //                            .Select(attr => attr.OriginalName.PairWithValue(attr.Value))
        //                            .ToArray();
        //                        return attrs;
        //                    })
        //                .ToArray();

        //            //var content = Properties.Resources.spahead + "|" + Properties.Resources.spabody;

        //            //var content = $"{head}|{body}";

        //            var response = request.CreateResponse(HttpStatusCode.OK,
        //                new
        //                {
        //                    head = head,
        //                    scripts = scriptList,
        //                    body = body
        //                });
        //            //response.Content = new StringContent(content);
        //            return response;
        //        }
        //    } catch (Exception ex)
        //    {
        //        return request.CreateResponse(HttpStatusCode.InternalServerError);
        //    }
        //}
    }
}

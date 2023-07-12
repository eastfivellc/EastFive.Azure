using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Net.Http;
using System.Net;
using System.IO;
using System.Collections.Generic;
using System.Linq;

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
                EastFive.Api.Security security,
            StreamResponse onFound,
            NotFoundResponse onNotFound)
        {
            return SpaHandler.LoadSpaFile(
                (blobStream) => onFound(blobStream),
                () => onNotFound());
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

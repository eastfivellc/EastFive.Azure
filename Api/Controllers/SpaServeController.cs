using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Net.Http;
using System.Net;
using HtmlAgilityPack;
using System.IO;
using System.Collections.Generic;
using EastFive.Security.SessionServer.Exceptions;
using System.Linq;
using EastFive.Extensions;

namespace EastFive.Api.Azure.Controllers
{
    [FunctionViewController(ExcludeNamespaces = "api,admin,aadb2c")]
    public class SpaServeController
    { 
        [HttpGet]
        public static IHttpResponse Get(
            [QueryId]string id,
            IHttpRequest request,
            HtmlResponse onNoIndexFile)
        {

            //var indexFile = SpaHandlerModule.indexHTML;
            var indexFile = Modules.SpaHandler.IndexHTML;

            var doc = new HtmlDocument();
            //doc.LoadHtml(indexFile.ToString());

            if (indexFile.IsDefaultOrNull())
                return onNoIndexFile("<html><body>No Index File</body></html>");

            try
            {
                using (var fileStream = new MemoryStream(indexFile))
                {
                    doc.Load(fileStream);
                    var head = doc.DocumentNode.SelectSingleNode("//head").InnerHtml;
                    var body = doc.DocumentNode.SelectSingleNode("//body").ChildNodes
                        .AsHtmlNodes()
                        .Where(node => node.Name.ToLower() != "script")
                        .Select(node => node.OuterHtml)
                        .Join(" ");

                    var scripts = doc.DocumentNode.SelectNodes("//script");

                    var scriptList = scripts
                        .Select(
                            script =>
                            {
                                var attrs = script.Attributes
                                    .Select(attr => attr.OriginalName.PairWithValue(attr.Value))
                                    .ToArray();
                                return attrs;
                            })
                        .ToArray();

                    //var content = Properties.Resources.spahead + "|" + Properties.Resources.spabody;

                    //var content = $"{head}|{body}";

                    var response = request.CreateResponse(HttpStatusCode.OK,
                        new
                        {
                            head = head,
                            scripts = scriptList,
                            body = body
                        });
                    //response.Content = new StringContent(content);
                    return response;
                }
            } catch (Exception ex)
            {
                return request.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }
    }
}

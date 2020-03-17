using BlackBarLabs.Api;
using BlackBarLabs.Extensions;
using EastFive.Api;
using EastFive.Collections.Generic;
using EastFive.Security.SessionServer;
using EastFive.Serialization;
using EastFive.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Api.Controllers
{
    [FunctionViewController(Route = "SheetIntegration")]
    public static class SheetIntegrationController
    {
        [HttpGet]
        public async static Task<HttpResponseMessage> IntegrationUploadAsync(
                [QueryId()]Guid integration,
            ViewFileResponse onLoadUploadPage)
        {
            return await onLoadUploadPage("SheetIntegration/UploadSheet.cshtml", null).ToTask();
        }

        [HttpPost]
        public async static Task<HttpResponseMessage> XlsPostAsync(EastFive.Security.SessionServer.Context context,
                ContentBytes sheet, [QueryParameter]Guid integration, IDictionary<string, bool> resourceTypes,
                HttpRequestMessage request,
            RedirectResponse onSuccess,
            NotFoundResponse onNotFound,
            GeneralConflictResponse onError)
        {
            var sheetId = Guid.NewGuid();
            return await await context.Integrations.UpdateAsync(integration,
                sheet.content.MD5HashGuid().ToString("N"),
                new Dictionary<string, string>()
                {
                    { "resource_types",  resourceTypes.SelectKeys().Join(",") },
                    { "sheet_id", sheetId.ToString("N") },
                },
                (redirectUrl) =>
                {
                    return EastFive.Api.Azure.Credentials.Sheets.SaveAsync(sheetId, sheet.contentType.MediaType,  sheet.content, integration,
                            context.DataContext,
                        () => onSuccess(redirectUrl),
                        "Guid not unique".AsFunctionException<HttpResponseMessage>());
                },
                () => onNotFound().ToTask(),
                () => onError("The provided integration ID has not been connected to an authorization.").ToTask());
        }
    }
}

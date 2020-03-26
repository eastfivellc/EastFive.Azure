using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Security.SessionServer;
using EastFive.Serialization;
using EastFive.Sheets;

namespace EastFive.Api.Controllers
{
    [FunctionViewController(Route = "SheetIntegration")]
    public static class SheetIntegrationController
    {
        [HttpGet]
        public async static Task<IHttpResponse> IntegrationUploadAsync(
                [QueryId()]Guid integration,
            ViewFileResponse onLoadUploadPage)
        {
            return await onLoadUploadPage("SheetIntegration/UploadSheet.cshtml", null).AsTask();
        }

        [HttpPost]
        public async static Task<IHttpResponse> XlsPostAsync(EastFive.Security.SessionServer.Context context,
                ContentBytes sheet, [QueryParameter]Guid integration, IDictionary<string, bool> resourceTypes,
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
                        "Guid not unique".AsFunctionException<IHttpResponse>());
                },
                () => onNotFound().AsTask(),
                () => onError("The provided integration ID has not been connected to an authorization.").AsTask());
        }
    }
}

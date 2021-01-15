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
            ViewFileResponse<int> onLoadUploadPage)
        {
            return await onLoadUploadPage("SheetIntegration/UploadSheet.cshtml", default).AsTask();
        }

        //[HttpPost]
        //public async static Task<IHttpResponse> XlsPostAsync(EastFive.Security.SessionServer.Context context,
        //        ContentBytes sheet, [QueryParameter]IRef<EastFive.Azure.Integration> integrationRef, IDictionary<string, bool> resourceTypes,
        //    RedirectResponse onSuccess,
        //    NotFoundResponse onNotFound,
        //    GeneralConflictResponse onError)
        //{
        //    var sheetId = Guid.NewGuid();
        //    return await await integrationRef.UpdateAsync(
        //        sheet.content.MD5HashGuid().ToString("N"),
        //        new Dictionary<string, string>()
        //        {
        //            { "resource_types",  resourceTypes.SelectKeys().Join(",") },
        //            { "sheet_id", sheetId.ToString("N") },
        //        },
        //        (integration, saveAsync) =>
        //        {
        //            if (!integration.authorizationId.HasValue)
        //                return onUnauthenticatedAuthenticationRequest();

        //            await saveAsync(authRequestStorage.authorizationId.Value, authRequestStorage.name, token, updatedUserParameters);
        //        },
        //        () => onNotFound().AsTask(),
        //        () => onError("The provided integration ID has not been connected to an authorization.").AsTask());
        //}
    }
}

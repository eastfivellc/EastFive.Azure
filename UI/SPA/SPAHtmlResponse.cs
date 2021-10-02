using EastFive.Api;
using EastFive.Azure.Spa;
using EastFive.Extensions;
using EastFive.Serialization;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.SPA
{
    [SPAHtmlResponse]
    public delegate IHttpResponse SPAHtmlResponse(string filename,
        Func<byte[], Task<string>> manipulateHtml = default);

    public class SPAHtmlResponseAttribute : HttpFuncDelegateAttribute
    {
        public override HttpStatusCode StatusCode => HttpStatusCode.OK;

        public override string Example => "<html></html>";

        public override Task<IHttpResponse> InstigateInternal(IApplication httpApp,
                IHttpRequest request, ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            SPAHtmlResponse responseDelegate =
                (filename, manipulateHtml) =>
                {
                    var httpApiApp = httpApp as IApiApplication;

                    var response = new CallbackResponse(request, this.StatusCode,
                            httpApiApp, filename, manipulateHtml);
                    return UpdateResponse(parameterInfo, httpApp, request, response);
                };
            return onSuccess(responseDelegate);
        }

        class CallbackResponse : EastFive.Api.HttpResponse
        {
            private string fileName;
            private Func<byte[], Task<string>> onFound;

            public CallbackResponse(IHttpRequest request, HttpStatusCode statusCode,
                IApiApplication httpApiApp,
                string filename, Func<byte[], Task<string>> onFound)
                : base(request, statusCode)
            {
                this.fileName = filename;
                this.onFound = onFound;
            }

            public override Task WriteResponseAsync(HttpContext context)
            {
                return SpaHandler.FileFromPath(fileName,
                    async (location, fileData, fileName, cacheControl, expiration) =>
                    {
                        //if (!context.Request.Path.Value.EndsWith(location))
                        //    context.Response.GetTypedHeaders().Location = new Uri(location, UriKind.Relative);
                        context.Response.GetTypedHeaders().CacheControl = cacheControl;
                        context.Response.GetTypedHeaders().Expires = expiration;
                        var outstream = context.Response.Body;
                        if (onFound.IsDefaultOrNull())
                        {
                            await SpaHandler.ServeFromSpaZipAsync(fileData, fileName, context);
                            return;
                        }

                        var resultHtml = await onFound(fileData);
                        var responseBytes = resultHtml.GetBytes();
                        await SpaHandler.ServeFromSpaZipAsync(responseBytes, fileName, context);
                    },
                    () =>
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        WriteReason(context, $"Could not find {fileName} in SPA.Zip");
                        return false.AsTask();
                    });
            }
        }
    }
}

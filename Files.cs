using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Meta
{
    [FunctionViewController(
        Route = "Files",
        Namespace = "admin")]
    public class Files
    {
        [EastFive.Api.HttpGet]
        [RequiredClaim(ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static IHttpResponse List(
            IApiApplication httpApp,
            IHttpRequest request)
        {
            return new ListFilesResponse(httpApp, request);
        }

        protected class ListFilesResponse : EastFive.Api.HttpResponse
        {
            IApiApplication httpApiApp;

            public ListFilesResponse(
                    IApiApplication httpApiApp,
                    IHttpRequest request)
                : base(request, HttpStatusCode.OK)
            {
                this.httpApiApp = httpApiApp;
            }

            public override async Task WriteResponseAsync(Stream responseStream)
            {
                using (var output = new StreamWriter(responseStream))
                {
                    await output.WriteAsync($"<html><head><title>Files</title></head>");
                    await output.WriteAsync($"<body>");

                    await output.WriteAsync($"<div>Current assembly is:{Assembly.GetExecutingAssembly().Location}</div>");

                    await DumpDirectory(httpApiApp.HostEnvironment.ContentRootPath);

                    async Task DumpDirectory(string directoryPath)
                    {
                        await output.WriteAsync($"<div><span>{directoryPath}</span>");

                        await output.WriteAsync($"<div>FILES<ul>");
                        foreach (var fileInfo in Directory.EnumerateFiles(directoryPath))
                            await output.WriteAsync($"<li>FILE:{fileInfo}</li>");
                        await output.WriteAsync($"</ul></div>");

                        await output.WriteAsync($"<ul>");
                        foreach (var dirInfo in Directory.EnumerateDirectories(directoryPath))
                        {
                            await output.WriteAsync($"<li>");
                            await DumpDirectory(dirInfo);
                            await output.WriteAsync($"</li>");
                        }
                        await output.WriteAsync($"</ul></div>");

                    }
                    await output.WriteAsync($"</body></html>");

                    await output.FlushAsync();
                }
            }
        }
    }
}

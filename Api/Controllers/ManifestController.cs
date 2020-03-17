using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System;

using BlackBarLabs.Api;
using EastFive.Api;
using EastFive;
using Microsoft.AspNetCore.Mvc.Routing;
using EastFive.Api.Controllers;
using EastFive.Linq;
using EastFive.Extensions;
using System.Collections.Generic;
using System.Reflection;

namespace EastFive.Api.Azure.Controllers
{
    [FunctionViewController(Route = "Manifest")]
    public static class ManifestController
    {
        [EastFive.Api.HttpGet]
        public static HttpResponseMessage FindAsync(
                HttpApplication application, HttpRequestMessage request, UrlHelper url,
            ContentResponse onFound,
            ContentTypeResponse<Api.Resources.Manifest> onContent,
            ViewFileResponse onHtml)
        {
            if (request.Headers.Accept.Where(accept => accept.MediaType.ToLower().Contains("html")).Any())
                return HtmlContent(application, request, url, onHtml);

            LocateControllers();
            var endpoints = ManifestController.lookup
                .Select(
                    type =>
                    {
                        var endpoint = url.GetWebId(type, "x-com.orderowl:ordering");
                        return endpoint;
                    })
                .ToArray();

            var manifest = new Resources.Manifest()
            {
                Id = Guid.NewGuid(),
                Endpoints = endpoints,
            };

            return request.CreateResponse(System.Net.HttpStatusCode.OK, manifest);
        }

        public static HttpResponseMessage HtmlContent(
                HttpApplication httpApp, HttpRequestMessage request, UrlHelper url,
            ViewFileResponse onHtml)
        {
            var lookups = httpApp.GetResources();
            var manifest = new EastFive.Api.Resources.Manifest(lookups, httpApp);
            return onHtml("Manifest/Manifest.cshtml", manifest);
        }

        public static HttpResponseMessage ManifestContent(
                HttpApplication httpApp, HttpRequestMessage request, UrlHelper url,
            ContentTypeResponse<Api.Resources.Manifest> onContent)
        {
            var lookups = httpApp.GetResources();
            var manifest = new EastFive.Api.Resources.Manifest(lookups, httpApp);
            return onContent(manifest);
        }

        public static string GetRouteHtml(string route, KeyValuePair<HttpMethod, MethodInfo[]>[] methods)
        {
            var html = methods
                .Select(methodKvp => $"<div><h4>{methodKvp.Key}</h4>{GetMethodHtml(methodKvp.Key.Method, methodKvp.Value)}</div>")
                .Join("");
            return html;
        }

        public static string GetMethodHtml(string httpVerb, MethodInfo[] methods)
        {
            var html = methods
                .Select(
                    method =>
                    {
                        var parameterHtml = method
                            .GetParameters()
                            .Where(methodParam => methodParam.ContainsAttributeInterface<IBindApiValue>(true))
                            .Select(
                                methodParam =>
                                {
                                    var validator = methodParam.GetAttributeInterface<IBindApiValue>();
                                    var lookupName = validator.GetKey(methodParam);
                                    var required = methodParam.ContainsCustomAttribute<PropertyAttribute>() ||
                                        methodParam.ContainsCustomAttribute<QueryParameterAttribute>();

                                    return CSharpInvocationHtml(lookupName, required, methodParam.ParameterType);
                                    
                                })
                            .Join(",");
                        return $"<span class=\"method,csharp\">{method.Name}({parameterHtml})</span>";
                    })
                .Join("");
            return html;
        }

        public static string CSharpInvocationHtml(string name, bool required, Type parameterType)
        {
            var requiredString = required ? "[Required]" : "[Optional]";
            return $"<span>[{requiredString}]{parameterType.Name} <span>{name}</span></span>";
        }
        
        #region Load Controllers

        private static object lookupLock = new object();
        private static Type[] lookup;

        private static void LocateControllers()
        {
            lock (lookupLock)
            {
                if (!ManifestController.lookup.IsDefaultNullOrEmpty())
                    return;

                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(assembly => (!assembly.GlobalAssemblyCache))
                    .ToArray();

                AppDomain.CurrentDomain.AssemblyLoad += (object sender, AssemblyLoadEventArgs args) =>
                {
                    lock (lookupLock)
                    {
                        AddControllersFromAssembly(args.LoadedAssembly);
                    }
                };

                foreach (var assembly in loadedAssemblies)
                {
                    AddControllersFromAssembly(assembly);
                }
            }
        }
        

        private static void AddControllersFromAssembly(System.Reflection.Assembly assembly)
        {
            try
            {
                var types = assembly
                    .GetTypes();
                var results = types
                    .Where(type =>
                        type.GetCustomAttribute<FunctionViewControllerAttribute, bool>((attrs) => true, () => false))
                    .ToArray();

                ManifestController.lookup = ManifestController.lookup.NullToEmpty().Concat(results).ToArray();
            }
            catch (Exception ex)
            {
                ex.GetType();
            }
        }

        #endregion
    }
}

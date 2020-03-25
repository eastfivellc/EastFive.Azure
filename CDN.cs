using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

using EastFive.Linq;
using EastFive.Security.SessionServer;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Collections.Generic;
using EastFive.Azure.Auth;
using EastFive.Azure.Monitoring;
using EastFive.Web.Configuration;
using EastFive.Api;
using Microsoft.AspNetCore.Http;

namespace EastFive.Azure
{


    [CDN]
    public class CDN : IInvokeApplication
    {
        private InvokeApplicationRemote _;
        public CDN()
        {
            var endpointHostname = AppSettings.CDNEndpointHostname.ConfigurationUri(
                eph => eph,
                (why) => new Uri("http://example.com"));
            var apiRoutePrefix = AppSettings.CDNEndpointHostname.ConfigurationString(
                apiRoutePrefix => apiRoutePrefix,
                (why) => "api");
            _ = new InvokeApplicationRemote(endpointHostname, apiRoutePrefix);
        }

        public Uri ServerLocation => ((IInvokeApplication)_).ServerLocation;

        public string ApiRouteName => ((IInvokeApplication)_).ApiRouteName;

        public IDictionary<string, string> Headers => ((IInvokeApplication)_).Headers;

        public IApplication Application => ((IInvokeApplication)_).Application;

        public IHttpRequest GetHttpRequest()
        {
            return ((IInvokeApplication)_).GetHttpRequest();
        }

        public RequestMessage<TResource> GetRequest<TResource>()
        {
            return ((IInvokeApplication)_).GetRequest<TResource>();
        }

        public Task<IHttpResponse> SendAsync(IHttpRequest httpRequest)
        {
            return ((IInvokeApplication)_).SendAsync(httpRequest);
        }
    }

    public class CDNAttribute : System.Attribute, IInstigate
    {
        public bool CanInstigate(ParameterInfo parameterInfo)
        {
            return parameterInfo.ParameterType.IsAssignableFrom(typeof(CDN));
        }

        public Task<IHttpResponse> Instigate(
            IApplication httpApp, IHttpRequest request,
            ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            var cdn = new CDN();
            return onSuccess(cdn);
        }
    }
}

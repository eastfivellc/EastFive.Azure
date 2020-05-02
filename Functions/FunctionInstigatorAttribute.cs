using EastFive.Api;
using EastFive.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EastFive.Azure.Functions
{
    public class FunctionInstigatorAttribute : IInvokeApplicationAttribute, IInstigate
    {
        public bool CanInstigate(ParameterInfo parameterInfo)
        {
            return parameterInfo.ParameterType
                .IsAssignableFrom(typeof(InvokeApplicationFromFunction));
        }

        public override Task<IHttpResponse> Instigate(IApplication httpApp,
                IHttpRequest request,
                ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            var apiPrefix = GetApiPrefix(request);
            var serverLocation = GetServerLocation(request);
            var instance = new InvokeApplicationFromFunction(httpApp, request, serverLocation, apiPrefix);
            return onSuccess(instance);
        }

        protected class InvokeApplicationFromFunction : InvokeApplicationFromRequest
        {
            public InvokeApplicationFromFunction(IApplication httpApp,
                    IHttpRequest request, 
                    Uri serverLocation, string apiPrefix) :
                base(httpApp as HttpApplication, request, serverLocation, apiPrefix)
            {

            }

            public override IHttpRequest GetHttpRequest()
            {
                var request = base.GetHttpRequest();
                if(this.Application is IFunctionApplication)
                {
                    var funcApp = (IFunctionApplication)this.Application;
                    request.UpdateHeader(InvocationMessage.InvocationMessageSourceHeaderKey,
                        headers => funcApp.InvocationMessageRef.id.ToString().AsArray());
                }
                return request;
            }
        }
    }

}

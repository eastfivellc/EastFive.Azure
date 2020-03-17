using EastFive.Api;
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

        public override Task<HttpResponseMessage> Instigate(IApplication httpApp,
                HttpRequestMessage request, CancellationToken cancellationToken,
                ParameterInfo parameterInfo,
            Func<object, Task<HttpResponseMessage>> onSuccess)
        {
            var apiPrefix = GetApiPrefix(request);
            var serverLocation = GetServerLocation(request);
            var instance = new InvokeApplicationFromFunction(httpApp, request, serverLocation, apiPrefix);
            return onSuccess(instance);
        }

        protected class InvokeApplicationFromFunction : InvokeApplicationFromRequest
        {
            public InvokeApplicationFromFunction(IApplication httpApp,
                    HttpRequestMessage request, 
                    Uri serverLocation, string apiPrefix) :
                base(httpApp, request, serverLocation, apiPrefix)
            {

            }

            public override HttpRequestMessage GetHttpRequest()
            {
                var request = base.GetHttpRequest();
                if(this.Application is IFunctionApplication)
                {
                    var funcApp = (IFunctionApplication)this.Application;
                    if (request.Headers.Contains(InvocationMessage.InvocationMessageSourceHeaderKey))
                        request.Headers.Remove(InvocationMessage.InvocationMessageSourceHeaderKey);
                    request.Headers.Add(
                        InvocationMessage.InvocationMessageSourceHeaderKey,
                        funcApp.InvocationMessageRef.id.ToString());
                }
                return request;
            }
        }
    }

}

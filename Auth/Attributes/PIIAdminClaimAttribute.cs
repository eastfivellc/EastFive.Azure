using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Extensions;

namespace EastFive.Azure.Auth
{
    //[ApiVoucherQueryDefinition]
    public class PIIAdminClaimAttribute : AuthorizationTokenAttribute, IHandleMethodInvocation
    {
        private const string ClaimValue = ClaimValues.Roles.PIIAdminRole;
        public bool AllowLocalHost { get; set; } = false;

        public Task<IHttpResponse> HandleMethodInvocationAsync(
            KeyValuePair<ParameterInfo, object>[] parameters,
            IReadOnlyDictionary<ParameterInfo, object> bindingContexts,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            InvokeMethodDelegate continueInvocation)
        {
            if (AllowLocalHost)
                if (request.IsLocalHostRequest())
                    return continueInvocation(parameters, bindingContexts, method, httpApp, request);

            if (!request.IsAuthorizedForRole(ClaimValue))
                return request
                    .CreateResponse(System.Net.HttpStatusCode.Forbidden)
                    .AddReason($"{method.DeclaringType.FullName}..{method.Name} requires roll claim `{ClaimValue}`")
                    .AsTask();

            return continueInvocation(parameters, bindingContexts, method, httpApp, request);
        }
    }
}

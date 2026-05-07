using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Extensions;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth
{
    public class SuperAdminClaimAttribute : AuthorizationTokenAttribute, IHandleMethodInvocation
    {
        private const string ClaimType = System.Security.Claims.ClaimTypes.Role;
        private const string ClaimValue = ClaimValues.Roles.SuperAdmin;

        public bool AllowLocalHost { get; set; } = false;

        private static bool allowLocalHostGlobal = EastFive.Azure.AppSettings.Auth.AllowLocalHostGlobalSuperAdmin
            .ConfigurationBoolean(
                allow => allow,
                onFailure: (why) => false,
                onNotSpecified: () => false);

        public Task<IHttpResponse> HandleMethodInvocationAsync(
            KeyValuePair<ParameterInfo, object>[] parameters,
            IReadOnlyDictionary<ParameterInfo, object> bindingContexts,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            InvokeMethodDelegate continueInvocation)
        {
            if (request.IsAuthorizedFor(new Uri(ClaimType), ClaimValue))
                return continueInvocation(parameters, bindingContexts, method, httpApp, request);

            if(AllowLocalHost || allowLocalHostGlobal)
                if(request.IsLocalHostRequest())
                    return continueInvocation(parameters, bindingContexts, method, httpApp, request);

            return request
                    .CreateResponse(System.Net.HttpStatusCode.Forbidden)
                    .AddReason($"{method.DeclaringType.FullName}..{method.Name} requires claim `{ClaimType}`=`{ClaimValue}`")
                    .AsTask();
        }
    }
}


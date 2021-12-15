using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Extensions;

namespace EastFive.Azure.Auth
{
    [ApiVoucherQueryDefinition]
    public class SuperAdminClaimAttribute : Attribute, IValidateHttpRequest
    {
        private const string ClaimType = System.Security.Claims.ClaimTypes.Role;
        private const string ClaimValue = ClaimValues.Roles.SuperAdmin;

        public Task<IHttpResponse> ValidateRequest(
            KeyValuePair<ParameterInfo, object>[] parameterSelection,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            ValidateHttpDelegate boundCallback)
        {
            if (!request.IsAuthorizedFor(new Uri(ClaimType), ClaimValue))
                return request
                    .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                    .AddReason($"{method.DeclaringType.FullName}..{method.Name} requires claim `{ClaimType}`=`{ClaimValue}`")
                    .AsTask();
            return boundCallback(parameterSelection, method, httpApp, request);
        }
    }
}


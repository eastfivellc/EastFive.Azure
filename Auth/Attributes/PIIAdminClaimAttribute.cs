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
    public class PIIAdminClaimAttribute : AuthorizationTokenAttribute, IValidateHttpRequest
    {
        private const string ClaimValue = ClaimValues.Roles.PIIAdminRole;
        public bool AllowLocalHost { get; set; } = false;

        public Task<IHttpResponse> ValidateRequest(
            KeyValuePair<ParameterInfo, object>[] parameterSelection,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            ValidateHttpDelegate boundCallback)
        {
            if (AllowLocalHost)
                if (request.IsLocalHostRequest())
                    return boundCallback(parameterSelection, method, httpApp, request);

            if (!request.IsAuthorizedForRole(ClaimValue))
                return request
                    .CreateResponse(System.Net.HttpStatusCode.Forbidden)
                    .AddReason($"{method.DeclaringType.FullName}..{method.Name} requires roll claim `{ClaimValue}`")
                    .AsTask();

            return boundCallback(parameterSelection, method, httpApp, request);
        }
    }
}

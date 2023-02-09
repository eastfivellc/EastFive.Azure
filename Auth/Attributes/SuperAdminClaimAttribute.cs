﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Extensions;

namespace EastFive.Azure.Auth
{
    public class SuperAdminClaimAttribute : Attribute, IValidateHttpRequest
    {
        private const string ClaimType = System.Security.Claims.ClaimTypes.Role;
        private const string ClaimValue = ClaimValues.Roles.SuperAdmin;

        public bool AllowLocalHost { get; set; } = false;

        public Task<IHttpResponse> ValidateRequest(
            KeyValuePair<ParameterInfo, object>[] parameterSelection,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            ValidateHttpDelegate boundCallback)
        {
            if (request.IsAuthorizedFor(new Uri(ClaimType), ClaimValue))
                return boundCallback(parameterSelection, method, httpApp, request);

            if(AllowLocalHost)
                if ("localhost".Equals(request.ServerLocation.Host, StringComparison.OrdinalIgnoreCase))
                    if ("localhost".Equals(request.RequestUri.Host, StringComparison.OrdinalIgnoreCase))
                        return boundCallback(parameterSelection, method, httpApp, request);

            return request
                    .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                    .AddReason($"{method.DeclaringType.FullName}..{method.Name} requires claim `{ClaimType}`=`{ClaimValue}`")
                    .AsTask();
        }
    }
}


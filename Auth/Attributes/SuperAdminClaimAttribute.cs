﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Extensions;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth
{
    public class SuperAdminClaimAttribute : AuthorizationTokenAttribute, IValidateHttpRequest
    {
        private const string ClaimType = System.Security.Claims.ClaimTypes.Role;
        private const string ClaimValue = ClaimValues.Roles.SuperAdmin;

        public bool AllowLocalHost { get; set; } = false;

        private static bool allowLocalHostGlobal = EastFive.Azure.AppSettings.Auth.AllowLocalHostGlobalSuperAdmin
            .ConfigurationBoolean(
                allow => allow,
                onFailure: (why) => false,
                onNotSpecified: () => false);

        public Task<IHttpResponse> ValidateRequest(
            KeyValuePair<ParameterInfo, object>[] parameterSelection,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            ValidateHttpDelegate boundCallback)
        {
            if (request.IsAuthorizedFor(new Uri(ClaimType), ClaimValue))
                return boundCallback(parameterSelection, method, httpApp, request);

            if(AllowLocalHost || allowLocalHostGlobal)
                if(request.IsLocalHostRequest())
                    return boundCallback(parameterSelection, method, httpApp, request);

            return request
                    .CreateResponse(System.Net.HttpStatusCode.Forbidden)
                    .AddReason($"{method.DeclaringType.FullName}..{method.Name} requires claim `{ClaimType}`=`{ClaimValue}`")
                    .AsTask();
        }
    }
}


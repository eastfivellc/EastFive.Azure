using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Api.Meta.Flows;
using EastFive.Api.Meta.Postman.Resources.Collection;
using EastFive.Azure.Auth;
using EastFive.Extensions;

namespace EastFive.Azure.Meta
{
    [ApiKeyAccessWorkflow]
    public class ApiKeyClaimAttribute : RequiredClaimAttribute
    {
        public ApiKeyClaimAttribute(string requiredClaimType, string requiredClaimValue)
            : base(requiredClaimType, requiredClaimValue)
        {
            this.ClaimType = new Uri(requiredClaimType);
            this.ClaimValue = requiredClaimValue;
        }
    }

    public class ApiKeyAccessWorkflowAttribute : Attribute, IDefineQueryItem
    {
        public QueryItem[] GetQueryItems(EastFive.Api.Resources.Method method)
        {
            return new QueryItem()
            {
                key = ApiKeyAccessAttribute.ParameterName,
                value = $"{{{{{EastFive.Azure.Workflows.AuthorizationFlow.Variables.ApiVoucher}}}}}",
            }
                .AsArray();
        }

        public QueryItem? GetQueryItem(EastFive.Api.Resources.Method method, ParameterInfo parameter)
        {
            return default;
        }
    }
}


using System;
using System.Reflection;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Api.Meta.Flows;
using EastFive.Api.Meta.Postman.Resources.Collection;

namespace EastFive.Azure.Auth
{
    public class ApiVoucherQueryDefinitionAttribute : Attribute, IDefineQueryItem
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


using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using EastFive;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Api.Meta.Flows;
using EastFive.Api.Meta.Postman.Resources.Collection;
using EastFive.Api.Resources;

namespace EastFive.Azure.Auth
{
    public class AuthorizationTokenAttribute
        : System.Attribute, IDefineHeader
    {
        public Header GetHeader(Api.Resources.Method method, ParameterInfo parameter)
        {
            return GetTokenHeader(method,parameter);
        }

        public static Header GetTokenHeader(Api.Resources.Method method, ParameterInfo parameter)
        {
            if (!method.MethodPoco.TryGetAttributeInterface(out IValidateHttpRequest requestValidator))
                return new Header()
                {
                    key = $"{{{{{EastFive.Azure.Workflows.AuthorizationFlow.Variables.AuthHeaderName}}}}}",
                    value = $"{{{{{EastFive.Azure.Workflows.AuthorizationFlow.Variables.TokenName}}}}}",
                    type = "text",
                };

            return new Header()
            {
                key = EastFive.Azure.Auth.ApiKeyAccessAttribute.ParameterName,
                value = $"{{{{{EastFive.Azure.Workflows.AuthorizationFlow.Variables.ApiVoucher}}}}}",
                type = "text",
            };
        }
    }
}

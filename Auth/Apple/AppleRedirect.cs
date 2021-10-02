using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure;
using EastFive.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Routing;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [FunctionViewController(
        Route = "AppleRedirect",
        ContentType = "x-application/apple-redirect",
        ContentTypeVersion = "0.1")]
    public class AppleRedirect : EastFive.Azure.Auth.Redirection
    {
        public const string StatePropertyName = AppleProvider.responseParamState;
        [ApiProperty(PropertyName = StatePropertyName)]
        [JsonProperty(PropertyName = StatePropertyName)]
        public string state { get; set; }

        public const string CodePropertyName = AppleProvider.responseParamCode;
        [ApiProperty(PropertyName = CodePropertyName)]
        [JsonProperty(PropertyName = CodePropertyName)]
        public string code { get; set; }

        public const string TokenPropertyName = AppleProvider.responseParamIdToken;
        [ApiProperty(PropertyName = TokenPropertyName)]
        [JsonProperty(PropertyName = TokenPropertyName)]
        public string token { get; set; }

        public const string UserPropertyName = AppleProvider.responseParamUser;
        [ApiProperty(PropertyName = UserPropertyName)]
        [JsonProperty(PropertyName = UserPropertyName)]
        public string user { get; set; }

        [HttpPost(MatchAllParameters = false)]
        public static async Task<IHttpResponse> Post(
                [PropertyOptional(Name = StatePropertyName)]string state,
                [PropertyOptional(Name = CodePropertyName)]string code,
                [PropertyOptional(Name = TokenPropertyName)]string token,
                [PropertyOptional(Name = UserPropertyName)]string user, 
                IAzureApplication application,
                IHttpRequest request,
                IProvideUrl urlHelper,
                IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            BadRequestResponse onBadCredentials,
            HtmlResponse onCouldNotConnect,
            HtmlResponse onGeneralFailure)
        {
            var method = EastFive.Azure.Auth.Method.ByMethodName(
                AppleProvider.IntegrationName, application);
            var requestParams = new Dictionary<string, string>();
            if(state.HasBlackSpace())
                requestParams.Add(AppleProvider.responseParamState, state);
            if (code.HasBlackSpace())
                requestParams.Add(AppleProvider.responseParamCode, code);
            if (token.HasBlackSpace())
                requestParams.Add(AppleProvider.responseParamIdToken, token);
            if (user.HasBlackSpace())
                requestParams.Add(AppleProvider.responseParamUser, user);

            return await ProcessRequestAsync(method,
                    requestParams,
                    application, request, endpoints, urlHelper,
                (redirect, accountIdMaybe) =>
                {
                    return onRedirectResponse(redirect);
                },
                (why) => onBadCredentials().AddReason(why),
                (why) =>
                {
                    return onCouldNotConnect(why);
                },
                (why) =>
                {
                    return onGeneralFailure(why);
                });
        }
    }
}

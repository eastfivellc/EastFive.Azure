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

namespace EastFive.Azure.Auth.Google
{
    [FunctionViewController(
        Namespace = "auth",
        Route = "GoogleRedirect",
        ContentType = "x-application/google-redirect",
        ContentTypeVersion = "0.1")]
    public class GoogleRedirect : EastFive.Azure.Auth.Redirection
    {
        public const string StatePropertyName = GoogleProvider.responseParamState;
        [ApiProperty(PropertyName = StatePropertyName)]
        [JsonProperty(PropertyName = StatePropertyName)]
        public string state { get; set; }

        public const string CodePropertyName = GoogleProvider.responseParamCode;
        [ApiProperty(PropertyName = CodePropertyName)]
        [JsonProperty(PropertyName = CodePropertyName)]
        public string code { get; set; }

        public const string ScopePropertyName = GoogleProvider.responseParamScope;
        [ApiProperty(PropertyName = ScopePropertyName)]
        [JsonProperty(PropertyName = ScopePropertyName)]
        public string scope { get; set; }

        public const string AuthUserPropertyName = "authuser";
        public const string PromptPropertyName = "prompt";


        [HttpGet]
        public static async Task<IHttpResponse> Redirected(
                [QueryParameter(Name = StatePropertyName)]string state,
                [QueryParameter(Name = CodePropertyName)]string code,
                [QueryParameter(Name = ScopePropertyName)]string scope,
                [OptionalQueryParameter(Name = AuthUserPropertyName)] string authUser,
                [OptionalQueryParameter(Name = PromptPropertyName)] string prompt,
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
                GoogleProvider.IntegrationName, application);
            var requestParams = new Dictionary<string, string>();
            if(state.HasBlackSpace())
                requestParams.Add(GoogleProvider.responseParamState, state);
            if (code.HasBlackSpace())
                requestParams.Add(GoogleProvider.responseParamCode, code);
            if (scope.HasBlackSpace())
                requestParams.Add(GoogleProvider.responseParamScope, scope);
            if (authUser.HasBlackSpace())
                requestParams.Add(AuthUserPropertyName, authUser);
            if (prompt.HasBlackSpace())
                requestParams.Add(PromptPropertyName, prompt);

            var builder = new UriBuilder(request.RequestUri);
            builder.Query = string.Empty;
            requestParams.Add(GoogleProvider.responseParamRedirectUri, builder.Uri.AbsoluteUri);

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

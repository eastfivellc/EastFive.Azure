using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure;
using EastFive.Azure.Auth;
using EastFive.Collections.Generic;

namespace EastFive.Azure.Auth.Salesforce
{
    [FunctionViewController(
        Namespace = "auth",
        Route = "SalesforceRedirect",
        ContentType = "x-application/google-redirect",
        ContentTypeVersion = "0.1")]
    public class SalesforceRedirect : EastFive.Azure.Auth.Redirection
    {
        public const string StatePropertyName = SalesforceProvider.responseParamState;
        [ApiProperty(PropertyName = StatePropertyName)]
        [JsonProperty(PropertyName = StatePropertyName)]
        public string state { get; set; }

        public const string CodePropertyName = SalesforceProvider.responseParamCode;
        [ApiProperty(PropertyName = CodePropertyName)]
        [JsonProperty(PropertyName = CodePropertyName)]
        public string code { get; set; }

        public const string ScopePropertyName = SalesforceProvider.responseParamScope;
        [ApiProperty(PropertyName = ScopePropertyName)]
        [JsonProperty(PropertyName = ScopePropertyName)]
        public string scope { get; set; }

        public const string AuthUserPropertyName = "authuser";
        public const string PromptPropertyName = "prompt";
        public const string HdPropertyName = "hd";

        [Unsecured("OAuth callback endpoint - receives authorization code from Salesforce OAuth flow, no bearer token available during OAuth callback")]
        [HttpGet]
        public static async Task<IHttpResponse> Redirected(
                [QueryParameter(Name = StatePropertyName)]string state,
                [QueryParameter(Name = CodePropertyName)]string code,
                [OptionalQueryParameter(Name = ScopePropertyName)]string scope,
                [OptionalQueryParameter(Name = AuthUserPropertyName)] string authUser,
                [OptionalQueryParameter(Name = PromptPropertyName)] string prompt,
                [OptionalQueryParameter(Name = HdPropertyName)] string hd,
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
                SalesforceProvider.IntegrationName, application);
            var requestParams = new Dictionary<string, string>();
            if(state.HasBlackSpace())
                requestParams.Add(SalesforceProvider.responseParamState, state);
            if (code.HasBlackSpace())
                requestParams.Add(SalesforceProvider.responseParamCode, code);
            if (scope.HasBlackSpace())
                requestParams.Add(SalesforceProvider.responseParamScope, scope);
            if (authUser.HasBlackSpace())
                requestParams.Add(AuthUserPropertyName, authUser);
            if (prompt.HasBlackSpace())
                requestParams.Add(PromptPropertyName, prompt);
            if(hd.HasBlackSpace())
                requestParams.Add(HdPropertyName, hd);

            var builder = new UriBuilder(request.RequestUri);
            builder.Query = string.Empty;
            requestParams.Add(SalesforceProvider.responseParamRedirectUri, builder.Uri.AbsoluteUri);

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

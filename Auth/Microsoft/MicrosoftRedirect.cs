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
using EastFive.Azure.Auth;
using EastFive.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Routing;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth.Microsoft
{
    [FunctionViewController(
        Namespace = "auth",
        Route = "MicrosoftRedirect",
        ContentType = "x-application/Microsoft-redirect",
        ContentTypeVersion = "0.1")]
    public class MicrosoftRedirect : EastFive.Azure.Auth.Redirection
    {
        public const string StatePropertyName = MicrosoftProvider.responseParamState;
        [ApiProperty(PropertyName = StatePropertyName)]
        [JsonProperty(PropertyName = StatePropertyName)]
        public string state { get; set; }

        public const string CodePropertyName = MicrosoftProvider.responseParamCode;
        [ApiProperty(PropertyName = CodePropertyName)]
        [JsonProperty(PropertyName = CodePropertyName)]
        public string code { get; set; }

        public const string IdTokenPropertyName = MicrosoftProvider.responseParamIdToken;
        [ApiProperty(PropertyName = IdTokenPropertyName)]
        [JsonProperty(PropertyName = IdTokenPropertyName)]
        public string idToken { get; set; }

        public const string ScopePropertyName = MicrosoftProvider.responseParamScope;
        [ApiProperty(PropertyName = ScopePropertyName)]
        [JsonProperty(PropertyName = ScopePropertyName)]
        public string scope { get; set; }

        public const string SessionStatePropertyName = MicrosoftProvider.responseParamSessionState;
        [ApiProperty(PropertyName = SessionStatePropertyName)]
        [JsonProperty(PropertyName = SessionStatePropertyName)]
        public string sessionState { get; set; }

        public const string AuthUserPropertyName = "authuser";
        public const string PromptPropertyName = "prompt";
        public const string HdPropertyName = "hd";

        [Unsecured("OAuth callback endpoint - receives authorization code from Microsoft OAuth flow, no bearer token available during OAuth callback")]
        [HttpGet]
        public static async Task<IHttpResponse> Redirected(
                [QueryParameter(Name = StatePropertyName)]string state,
                [QueryParameter(Name = CodePropertyName)]string code,
                [QueryParameter(Name = ScopePropertyName)]string scope,
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
                MicrosoftProvider.IntegrationName, application);
            var requestParams = new Dictionary<string, string>();
            if(state.HasBlackSpace())
                requestParams.Add(MicrosoftProvider.responseParamState, state);
            if (code.HasBlackSpace())
                requestParams.Add(MicrosoftProvider.responseParamCode, code);
            if (scope.HasBlackSpace())
                requestParams.Add(MicrosoftProvider.responseParamScope, scope);
            if (authUser.HasBlackSpace())
                requestParams.Add(AuthUserPropertyName, authUser);
            if (prompt.HasBlackSpace())
                requestParams.Add(PromptPropertyName, prompt);
            if(hd.HasBlackSpace())
                requestParams.Add(HdPropertyName, hd);

            var builder = new UriBuilder(request.RequestUri);
            builder.Query = string.Empty;
            requestParams.Add(MicrosoftProvider.responseParamRedirectUri, builder.Uri.AbsoluteUri);

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

        [Unsecured("OAuth callback endpoint - receives authorization code from Microsoft OAuth flow via POST, no bearer token available during OAuth callback")]
        [HttpPost]
        public static async Task<IHttpResponse> RedirectedPost(
                [Property(Name = StatePropertyName)] string state,
                [Property(Name = CodePropertyName)] string code,
                [PropertyOptional(Name = IdTokenPropertyName)] string idToken,
                [PropertyOptional(Name = SessionStatePropertyName)] string scope,
                [PropertyOptional(Name = AuthUserPropertyName)] string authUser,
                [PropertyOptional(Name = PromptPropertyName)] string prompt,
                [PropertyOptional(Name = HdPropertyName)] string hd,
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
                MicrosoftProvider.IntegrationName, application);
            var requestParams = new Dictionary<string, string>();
            if (state.HasBlackSpace())
                requestParams.Add(MicrosoftProvider.responseParamState, state);
            if (code.HasBlackSpace())
                requestParams.Add(MicrosoftProvider.responseParamCode, code);
            if (scope.HasBlackSpace())
                requestParams.Add(MicrosoftProvider.responseParamScope, scope);
            if (idToken.HasBlackSpace())
                requestParams.Add(MicrosoftProvider.responseParamIdToken, idToken);
            if (authUser.HasBlackSpace())
                requestParams.Add(AuthUserPropertyName, authUser);
            if (prompt.HasBlackSpace())
                requestParams.Add(PromptPropertyName, prompt);
            if (hd.HasBlackSpace())
                requestParams.Add(HdPropertyName, hd);

            var builder = new UriBuilder(request.RequestUri);
            builder.Query = string.Empty;
            requestParams.Add(MicrosoftProvider.responseParamRedirectUri, builder.Uri.AbsoluteUri);

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

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
using EastFive.Extensions;
using Microsoft.AspNetCore.Mvc.Routing;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth.Superadmin
{
    [FunctionViewController(
        Namespace = "auth",
        Route = nameof(SuperadminRedirect),
        ContentType = "x-application/superadmin-redirect",
        ContentTypeVersion = "0.1")]
    public class SuperadminRedirect : EastFive.Azure.Auth.Redirection
    {
        public const string CodePropertyName = SuperadminProvider.responseParamCode;
        [ApiProperty(PropertyName = CodePropertyName)]
        [JsonProperty(PropertyName = CodePropertyName)]
        public string code { get; set; }

        public const string AuthUserPropertyName = "authuser";
        public const string PromptPropertyName = "prompt";
        public const string HdPropertyName = "hd";

        [Unsecured("OAuth callback endpoint - receives authorization code from Superadmin OAuth flow, no bearer token available during OAuth callback")]
        [HttpGet]
        public static Task<IHttpResponse> Redirected(
                [QueryParameter(Name = CodePropertyName)]string code,
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
            return onBadCredentials().AsTask();
            //var method = EastFive.Azure.Auth.Method.ByMethodName(
            //    GoogleProvider.IntegrationName, application);
            //var requestParams = new Dictionary<string, string>();
            //if(state.HasBlackSpace())
            //    requestParams.Add(GoogleProvider.responseParamState, state);
            //if (code.HasBlackSpace())
            //    requestParams.Add(GoogleProvider.responseParamCode, code);
            //if (scope.HasBlackSpace())
            //    requestParams.Add(GoogleProvider.responseParamScope, scope);
            //if (authUser.HasBlackSpace())
            //    requestParams.Add(AuthUserPropertyName, authUser);
            //if (prompt.HasBlackSpace())
            //    requestParams.Add(PromptPropertyName, prompt);
            //if(hd.HasBlackSpace())
            //    requestParams.Add(HdPropertyName, hd);

            //var builder = new UriBuilder(request.RequestUri);
            //builder.Query = string.Empty;
            //requestParams.Add(GoogleProvider.responseParamRedirectUri, builder.Uri.AbsoluteUri);

            //return await ProcessRequestAsync(method,
            //        requestParams,
            //        application, request, endpoints, urlHelper,
            //    (redirect, accountIdMaybe) =>
            //    {
            //        return onRedirectResponse(redirect);
            //    },
            //    (why) => onBadCredentials().AddReason(why),
            //    (why) =>
            //    {
            //        return onCouldNotConnect(why);
            //    },
            //    (why) =>
            //    {
            //        return onGeneralFailure(why);
            //    });
        }
    }
}

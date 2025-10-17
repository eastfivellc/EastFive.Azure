using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Routing;

using EastFive;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Auth;
using EastFive.Collections.Generic;

using Newtonsoft.Json;
using EastFive.Azure.Auth.CredentialProviders;

namespace EastFive.Azure.Auth
{
    [FunctionViewController(
        Route = "OpenIdResponse",
        ContentType = "x-application/open-id-response",
        ContentTypeVersion = "0.1")]
    public class OpenIdResponse : EastFive.Azure.Auth.Redirection
    {
        public const string StatePropertyName = "state";
        [ApiProperty(PropertyName = StatePropertyName)]
        [JsonProperty(PropertyName = StatePropertyName)]
        public string state { get; set; }

        public const string CodePropertyName = "code";
        [ApiProperty(PropertyName = CodePropertyName)]
        [JsonProperty(PropertyName = CodePropertyName)]
        public string code { get; set; }

        public const string TokenPropertyName = "id_token";
        [ApiProperty(PropertyName = TokenPropertyName)]
        [JsonProperty(PropertyName = TokenPropertyName)]
        public string token { get; set; }

        public const string ErrorPropertyName = "error";
        [ApiProperty(PropertyName = ErrorPropertyName)]
        [JsonProperty(PropertyName = ErrorPropertyName)]
        public string error { get; set; }

        public const string ErrorDescriptionPropertyName = "error_description";
        [ApiProperty(PropertyName = ErrorDescriptionPropertyName)]
        [JsonProperty(PropertyName = ErrorDescriptionPropertyName)]
        public string errorDescription { get; set; }

        [Unsecured("OpenID Connect OAuth callback endpoint - receives authorization code and ID token from OAuth provider, no bearer token available during OAuth callback")]
        [HttpPost(MatchAllParameters = false)]
        public static async Task<IHttpResponse> Post(
                [Property(Name = StatePropertyName)]string state,
                [PropertyOptional(Name = CodePropertyName)]string code,
                [Property(Name = TokenPropertyName)]string token,
                AzureApplication application,
                IHttpRequest request,
                IProvideUrl urlHelper,
                IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            BadRequestResponse onBadCredentials,
            HtmlResponse onCouldNotConnect,
            HtmlResponse onGeneralFailure)
        {
            var method = EastFive.Azure.Auth.Method.ByMethodName(
                AzureADB2CProvider.IntegrationName, application);
            var requestParams = request.RequestUri
                .ParseQuery()
                .Distinct(kvp => kvp.Key)
                .ToDictionary();
            if (state.HasBlackSpace())
                requestParams.Add(StatePropertyName, state);
            if (code.HasBlackSpace())
                requestParams.Add(CodePropertyName, code);
            if (token.HasBlackSpace())
                requestParams.Add(TokenPropertyName, token);

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


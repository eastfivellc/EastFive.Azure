using System.Threading.Tasks;
using Newtonsoft.Json;
using EastFive.Api;

namespace EastFive.Azure.Login
{
    [FunctionViewController(
        Route = "LoginRedirection",
        ContentType = "x-application/login-redirection",
        ContentTypeVersion = "0.1")]
    public class Redirection
    {
        public const string StatePropertyName = "state";
        [ApiProperty(PropertyName = StatePropertyName)]
        [JsonProperty(PropertyName = StatePropertyName)]
        public string state;

        [HttpGet(MatchAllParameters = false)]
        [Unsecured("OAuth login redirection endpoint - receives authentication response from credential provider, no bearer token available during OAuth flow")]
        public static async Task<IHttpResponse> Get(
                IAzureApplication application, IProvideUrl urlHelper,
                IHttpRequest request,
                IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            var parameters = request.RequestUri.ParseQuery();
            parameters.Add(CredentialProvider.referrerKey, request.RequestUri.AbsoluteUri);
            var method = EastFive.Azure.Auth.Method.ByMethodName(
                CredentialProvider.IntegrationName, application);

            return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, 
                    parameters,
                    application,
                    request, endpoints, urlHelper,
                (redirect, accountIdMaybe) => onRedirectResponse(redirect).AddReason("success"),
                (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                (why) => onNoServiceResponse().AddReason(why),
                (why) => onFailure(why));
        }

        [HttpPatch(MatchAllParameters = false)]
        [Unsecured("OAuth login redirection endpoint - receives authentication response from credential provider via PATCH, no bearer token available during OAuth flow")]
        public static async Task<IHttpResponse> PatchAsync(
                IAzureApplication application, IProvideUrl urlHelper,
                IHttpRequest request,
                IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            var parameters = request.RequestUri.ParseQuery();
            parameters.Add(CredentialProvider.referrerKey, request.RequestUri.AbsoluteUri);
            var method = EastFive.Azure.Auth.Method.ByMethodName(
                CredentialProvider.IntegrationName, application);

            return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method,
                    parameters,
                    application,
                    request, endpoints, urlHelper,
                (redirect, accountIdMaybe) => onRedirectResponse(redirect).AddReason("success"),
                (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                (why) => onNoServiceResponse().AddReason(why),
                (why) => onFailure(why));
        }
    }
}

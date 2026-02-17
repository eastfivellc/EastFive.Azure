using System.Collections.Generic;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Controllers;
using EastFive.Azure.Auth.CredentialProviders;

namespace EastFive.Azure.Auth
{
    [FunctionViewController(
        Route = "SAMLRedirect",
        ContentType = "x-application/auth-redirection.saml",
        ContentTypeVersion = "0.1")]
    public class SAMLRedirect : EastFive.Azure.Auth.Redirection
    {
        public const string SamlResponseParameter = "SAMLResponse";
        public const string RelayStateParameter = "RelayState";

        [HttpGet(MatchAllParameters = false)]
        [Unsecured("SAML callback endpoint - receives SAML response via query string, no bearer token available during callback")]
        public static async Task<IHttpResponse> Get(
                IAzureApplication application, IProvideUrl urlHelper,
                IInvokeApplication endpoints,
                IHttpRequest request,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            var parameters = request.RequestUri.ParseQuery();
            var method = EastFive.Azure.Auth.Method.ByMethodName(
                SAMLProvider.IntegrationName, application);

            return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, parameters,
                    application, request, endpoints, urlHelper,
                (redirect, accountIdMaybe) => onRedirectResponse(redirect),
                (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                (why) => onNoServiceResponse().AddReason(why),
                (why) => onFailure(why));
        }

        [HttpPost(MatchAllParameters = false)]
        [Unsecured("SAML callback endpoint - receives SAML response via POST, no bearer token available during callback")]
        public static async Task<IHttpResponse> PostAsync(
                [Property(Name = SamlResponseParameter)]string samlResponse,
                [Property(Name = RelayStateParameter)]Property<string> relayStateMaybe,
                IAzureApplication application, IProvideUrl urlHelper,
                IHttpRequest request, IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            var parameters = request.RequestUri.ParseQuery();
            parameters[SamlResponseParameter] = samlResponse;
            if (relayStateMaybe.specified)
                parameters[RelayStateParameter] = relayStateMaybe.value;

            var method = EastFive.Azure.Auth.Method.ByMethodName(
                SAMLProvider.IntegrationName, application);

            return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, parameters,
                    application, request, endpoints, urlHelper,
                (redirect, accountIdMaybe) => onRedirectResponse(redirect),
                (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                (why) => onNoServiceResponse().AddReason(why),
                (why) => onFailure(why));
        }

        [HttpPost(MatchAllParameters = false)]
        [HttpAction("logout")]
        [Unsecured("SAML logout endpoint - receives logout requests/responses, no bearer token available during callback")]
        public static async Task<IHttpResponse> LogoutAsync(
                [Property(Name = SamlResponseParameter)]Property<string> samlResponseMaybe,
                [Property(Name = RelayStateParameter)]Property<string> relayStateMaybe,
                IAzureApplication application, IProvideUrl urlHelper,
                IHttpRequest request, IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            var parameters = request.RequestUri.ParseQuery();
            if (samlResponseMaybe.specified)
                parameters[SamlResponseParameter] = samlResponseMaybe.value;
            if (relayStateMaybe.specified)
                parameters[RelayStateParameter] = relayStateMaybe.value;

            var method = EastFive.Azure.Auth.Method.ByMethodName(
                SAMLProvider.IntegrationName, application);

            return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, parameters,
                    application, request, endpoints, urlHelper,
                (redirect, accountIdMaybe) => onRedirectResponse(redirect),
                (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                (why) => onNoServiceResponse().AddReason(why),
                (why) => onFailure(why));
        }
    }
}

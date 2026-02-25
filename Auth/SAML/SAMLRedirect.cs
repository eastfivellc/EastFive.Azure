using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Controllers;
using EastFive.Azure.Auth.CredentialProviders;
using EastFive.Extensions;

namespace EastFive.Azure.Auth
{
    [FunctionViewController(
        Route = "SAMLRedirect",
        Namespace = "auth",
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
                [QueryParameter(Name = "tag", CheckFileName = true)]string tag,
                [PropertyOptional(Name = SamlResponseParameter)]string samlResponse,
                [PropertyOptional(Name = RelayStateParameter)]Property<string> relayStateMaybe,
                IAzureApplication application, IProvideUrl urlHelper,
                IHttpRequest request, IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            if("logout".Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                var pathSegments = request.RequestUri.ParsePath();
                if(pathSegments.Length > 3)
                    tag = pathSegments.Last().TrimEnd('/');
            }
            
            if (tag.IsNullOrWhiteSpace())
                tag = "ACPTool";

            var methodName = SAMLProvider.IntegrationName;
            var method = EastFive.Azure.Auth.Method.ByMethodName(methodName, application);

            var failureHtml = "<html><title>{0}</title><body>{1} Please report:<code>{2}</code> to Affirm Health if the issue persists.</body></html>";

            return await EastFive.Web.Configuration.Settings.GetString($"AffirmHealth.PDMS.PingRedirect.{tag}.PingAuthName",
                async pingAuthName =>
                {
                        return await EastFive.Web.Configuration.Settings.GetGuid($"AffirmHealth.PDMS.PingRedirect.{tag}.PingReportSetId",
                            async reportSetId =>
                            {
                                var queryParameters = request.RequestUri.ParseQuery();
                                var formParameters = request.Form
                                    .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value))
                                    .ToDictionary();
                                var parameters = queryParameters.Concat(formParameters)
                                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                                    .Append("PingAuthName".PairWithValue(pingAuthName))
                                    .Append("ReportSetId".PairWithValue(reportSetId.ToString()))
                                    .ToDictionary();

                                return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, parameters,
                                        application, request, endpoints, urlHelper,
                                    (redirect, accountIdMaybe) => onRedirectResponse(redirect),
                                    (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                                    (why) => onNoServiceResponse().AddReason(why),
                                    (why) => onFailure(why));
                            },
                            why =>
                            {
                                return onFailure(why).AsTask();
                            });
                    },
                    why =>
                    {
                        return onFailure(why).AsTask();
                    });

            
        }

        [HttpPost(MatchAllParameters = false)]
        [HttpAction("logout")]
        [Unsecured("SAML logout endpoint - receives logout requests/responses, no bearer token available during callback")]
        public static async Task<IHttpResponse> LogoutAsync(
                [QueryParameter(Name = "tag", CheckFileName = true)]string tag,
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Azure.Auth.CredentialProviders;
using EastFive.Extensions;
using EastFive.Web.Configuration;

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
        public const string MetadataLocationParameter = "MetadataLocation";

        [HttpGet(MatchAllParameters = false)]
        [Unsecured("SAML callback endpoint - receives SAML response via query string, no bearer token available during callback")]
        public static async Task<IHttpResponse> Get(
                [OptionalQueryParameter(Name = "tag", CheckFileName = true)]string tag,
                IAzureApplication application, IProvideUrl urlHelper,
                IInvokeApplication endpoints,
                IHttpRequest request,
            RedirectResponse onRedirectResponse,
            ServiceUnavailableResponse onNoServiceResponse,
            BadRequestResponse onBadCredentials,
            GeneralConflictResponse onFailure)
        {
            var parameters = request.RequestUri.ParseQuery();

            if (tag.IsNullOrWhiteSpace())
                tag = "ACPTool";

            // A GET without a SAMLResponse is not a callback — it is the tag's launch
            // URL being opened directly (e.g. athenaNet launching an embedded app:
            // GET {launchUrl}?iss=...&launch=...). Login happens exclusively via the
            // IdP's POST to the ACS. When the launch carries SMART-on-FHIR context
            // (iss + launch) and the tag is configured (has a launch page), forward to
            // the host's SMART launch endpoint, which decides — via its issuer allow
            // mechanism — whether to run the SMART auth sequence or fall back to the
            // tag's launch page. Launches without SMART context go straight to the
            // launch page. Unconfigured tags fall through to redemption, which fails
            // with "SAMLResponse parameter was not provided" — the same answer
            // AffirmHealth gives this shape.
            if (!parameters.ContainsKey(SamlResponseParameter))
            {
                parameters.TryGetValue("iss", out var iss);
                parameters.TryGetValue("launch", out var launch);
                var hasSmartContext = iss.HasBlackSpace() && launch.HasBlackSpace();

                var launchResponse = EastFive.Azure.AppSettings.SAML.GetLaunchPage(tag).ConfigurationString(
                    launchPage =>
                    {
                        if (hasSmartContext)
                        {
                            var smartLaunchUri = urlHelper
                                .Link("DefaultApi", "SmartLaunch")
                                .SetQueryParam("iss", iss)
                                .SetQueryParam("launch", launch)
                                .SetQueryParam("tag", tag);
                            return onRedirectResponse(smartLaunchUri);
                        }
                        return Uri.TryCreate(launchPage, UriKind.RelativeOrAbsolute, out var launchUri)
                            ? onRedirectResponse(launchUri)
                            : onFailure($"Configured launch page for SAML tag `{tag}` is not a valid URI");
                    },
                    onUnspecified: why => default(IHttpResponse));
                if (launchResponse != null)
                    return launchResponse;
            }

            var method = EastFive.Azure.Auth.Method.ByMethodName(
                SAMLProvider.IntegrationName, application);

            return await EastFive.Azure.AppSettings.SAML.GetMetadataLocation(tag).ConfigurationUri(
                metadataLocation =>
                {
                    parameters[MetadataLocationParameter] = metadataLocation.AbsoluteUri;
                    return EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, parameters,
                            application, request, endpoints, urlHelper,
                        (redirect, accountIdMaybe) => onRedirectResponse(redirect),
                        (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                        (why) => onNoServiceResponse().AddReason(why),
                        (why) => onFailure(why));
                },
                why => onFailure(why).AsTask());
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

            return await EastFive.Web.Configuration.Settings.GetString($"AffirmHealth.PDMS.PingRedirect.{tag}.PingAuthName",
                async pingAuthName =>
                {
                    return await EastFive.Web.Configuration.Settings.GetGuid($"AffirmHealth.PDMS.PingRedirect.{tag}.PingReportSetId",
                        async reportSetId =>
                        {
                            return await EastFive.Azure.AppSettings.SAML.GetMetadataLocation(tag).ConfigurationUri(
                                async metadataLocation =>
                                {
                                    var queryParameters = request.RequestUri.ParseQuery();
                                    var formParameters = request.Form
                                        .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value))
                                        .ToDictionary();
                                    var parameters = queryParameters.Concat(formParameters)
                                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                                        .Append("PingAuthName".PairWithValue(pingAuthName))
                                        .Append("ReportSetId".PairWithValue(reportSetId.ToString()))
                                        .Append(MetadataLocationParameter.PairWithValue(metadataLocation.AbsoluteUri))
                                        .ToDictionary();

                                    return await EastFive.Azure.Auth.Redirection.ProcessRequestAsync(method, parameters,
                                            application, request, endpoints, urlHelper,
                                        (redirect, accountIdMaybe) => onRedirectResponse(redirect),
                                        (why) => onBadCredentials().AddReason($"Bad credentials:{why}"),
                                        (why) => onNoServiceResponse().AddReason(why),
                                        (why) => onFailure(why));
                                },
                                (why) => onFailure(why).AsTask());
                        },
                        (why) => onFailure(why).AsTask());
                },
                (why) =>onFailure(why).AsTask());
        }

        // Single-logout callback. The SP metadata advertises the SLO service with the
        // HTTP-Redirect binding (GET). Single [HttpAction] only: stacking [HttpPost]
        // alongside it also registered this method on the plain ACS route and made
        // every POST /auth/SAMLRedirect ambiguous with PostAsync.
        [HttpAction("logout", MatchAllParameters = false)]
        [Unsecured("SAML logout endpoint - receives logout requests/responses, no bearer token available during callback")]
        public static async Task<IHttpResponse> LogoutAsync(
                [QueryParameter(Name = "tag", CheckFileName = true)]string tag,
                [PropertyOptional(Name = SamlResponseParameter)]Property<string> samlResponseMaybe,
                [PropertyOptional(Name = RelayStateParameter)]Property<string> relayStateMaybe,
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

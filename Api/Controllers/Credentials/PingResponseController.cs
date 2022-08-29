using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Mvc.Routing;

using Newtonsoft.Json;

using EastFive.Api.Services;
using EastFive.Extensions;
using EastFive.Api.Controllers;
using EastFive.Collections.Generic;
using EastFive.Azure.Auth;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth.CredentialProviders
{
    [FunctionViewController(
        Namespace = "aadb2c",
        Route="PingResponse",
        ContentType = "x-application/ping-response",
        ContentTypeVersion = "0.1")]
    public class PingResponse : EastFive.Azure.Auth.Redirection
    {
        public const string TokenIdPropertyName = PingProvider.TokenId;
        [ApiProperty(PropertyName = TokenIdPropertyName)]
        [JsonProperty(PropertyName = TokenIdPropertyName)]
        public string tokenid { get; set; }

        public const string AgentIdPropertyName = PingProvider.AgentId;
        [ApiProperty(PropertyName = AgentIdPropertyName)]
        [JsonProperty(PropertyName = AgentIdPropertyName)]
        public string agentid { get; set; }

        [HttpGet(MatchAllParameters = false)]
        public static Task<IHttpResponse> Get(
                [OptionalQueryParameter(CheckFileName = true)]string tag,
                [QueryParameter(Name = TokenIdPropertyName)]string tokenId,
                [QueryParameter(Name = AgentIdPropertyName)]string agentId,
                AzureApplication application,
                IHttpRequest request,
                IProvideUrl urlHelper,
                IInvokeApplication endpoints,
            RedirectResponse onRedirectResponse,
            BadRequestResponse onBadCredentials,
            HtmlResponse onCouldNotConnect,
            HtmlResponse onGeneralFailure)
        {
            //The way this works...
            //1.  User clicks Third Party Applications\AffirmHealth over in Athena.
            //2.  Athena calls Ping
            //3.  Ping redirects to /PingResponseController with a token.
            //4.  This code validates the token, parses it out, and redirects to the interactive report matching the patient id.

            //To debug, you have to grab the token from Ping that comes in here.  If you don't, the token will get used and it won't work again
            //To do this, uncomment the commented line and comment out the call to ParsePingResponseAsync.  That way the token won't be used.
            //After the uncomment/comment, publish to dev and then click third party apps\Affirm Health in Athena.
            //Grab the token from the browser.
            //Then, switch the uncommented/commented lines back and run the server in debug.
            //Send the token via Postman to debug and see any errors that might come back from Ping.

            return EastFive.Azure.AppSettings.Auth.DebugLogin.ConfigurationUri(
                debugRedirect =>
                {
                    var populatedUri = debugRedirect.CopyPathAndQuery(request.RequestUri);
                    return onRedirectResponse(populatedUri).AsTask();
                },
                onFailure: (why) => ContinueAsync(),
                onNotSpecified: () => ContinueAsync());

            //return onRedirectResponse(new Uri("https://www.google.com"));

            async Task<IHttpResponse> ContinueAsync()
            {
                if (tag.IsNullOrWhiteSpace())
                    tag = "OpioidTool";

                var methodName = PingProvider.IntegrationName;
                var method = EastFive.Azure.Auth.Method.ByMethodName(methodName, application);

                var failureHtml = "<html><title>{0}</title><body>{1} Please report:<code>{2}</code> to Affirm Health if the issue persists.</body></html>";

                return await EastFive.Web.Configuration.Settings.GetString($"AffirmHealth.PDMS.PingRedirect.{tag}.PingAuthName",
                    async pingAuthName =>
                    {
                        return await EastFive.Web.Configuration.Settings.GetGuid($"AffirmHealth.PDMS.PingRedirect.{tag}.PingReportSetId",
                            async reportSetId =>
                            {
                                var requestParams = request.RequestUri
                                    .ParseQuery()
                                    .Append("PingAuthName".PairWithValue(pingAuthName))
                                    .Append("ReportSetId".PairWithValue(reportSetId.ToString()))
                                    .ToDictionary();

                                return await Redirection.ProcessRequestAsync(method,
                                        requestParams,
                                        application, request, endpoints, urlHelper,
                                    (redirect, accountIdMaybe) =>
                                    {
                                        return onRedirectResponse(redirect);
                                    },
                                    (why) => onBadCredentials().AddReason(why),
                                    (why) =>
                                    {
                                        var failureText = String.Format(failureHtml,
                                            "PING/ATHENA credential service offline",
                                            "Could not connect to PING (the authorization service used by Athena) to verify the provided link. Affirm Health will work with Athena/Ping to resolve this issue.",
                                            why);
                                        return onCouldNotConnect(why);
                                    },
                                    (why) =>
                                    {
                                        var failureText = String.Format(failureHtml,
                                            "Failed to authenticate",
                                            "You could not be authenticated.",
                                            why);
                                        return onGeneralFailure(why);
                                    });
                            },
                            why =>
                            {
                                return onGeneralFailure(why).AsTask();
                            });
                    },
                    why =>
                    {
                        return onGeneralFailure(why).AsTask();
                    });
            }

            
        }
    }
}
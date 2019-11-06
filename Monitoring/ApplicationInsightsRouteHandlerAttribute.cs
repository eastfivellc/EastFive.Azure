using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Modules;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using Microsoft.ApplicationInsights.DataContracts;

namespace EastFive.Azure.Monitoring
{
    public class ApplicationInsightsRouteHandlerAttribute : Attribute, IHandleRoutes, IHandleMethods, IHandleExceptions
    {
        public const string TelemetryStatusName = "StatusName";
        public const string TelemetryStatusInstance = "StatusInstance";

        internal const string HttpRequestMessagePropertyRequestTelemetryKey = "e5_monitoring_requesttelemetry_key";

        public async Task<HttpResponseMessage> HandleRouteAsync(Type controllerType,
                IApplication httpApp, HttpRequestMessage request, string routeName,
            RouteHandlingDelegate continueExecution)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N");
            var telemetry = new RequestTelemetry()
            {
                Id = requestId,
                Source = "EastFive.Api",
                Timestamp = DateTimeOffset.UtcNow,
                Url = request.RequestUri,
            };

            #region User / Session

            var claims = request.GetClaims(
                claimsEnumerable => claimsEnumerable.ToArray(),
                () => new Claim[] { },
                (why) => new Claim[] { });
            var sessionIdClaimType = BlackBarLabs.Security.ClaimIds.Session;
            var sessionIdMaybe = SessionToken.GetClaimIdMaybe(claims, sessionIdClaimType);
            if (sessionIdMaybe.HasValue)
                telemetry.Context.Session.Id = sessionIdMaybe.Value.ToString().ToUpper();
            
            var accountIdClaimType = EastFive.Api.AppSettings.ActorIdClaimType.ConfigurationString(
                (accIdCT) => accIdCT,
                (why) => default);
            if (accountIdClaimType.HasBlackSpace())
            {
                var accountIdMaybe = SessionToken.GetClaimIdMaybe(claims, accountIdClaimType);
                if (accountIdMaybe.HasValue)
                {
                    var accountIdStr = accountIdMaybe.Value.ToString().ToUpper();
                    telemetry.Context.User.AccountId = accountIdStr;
                    telemetry.Context.User.AuthenticatedUserId = accountIdStr;
                }
            }

            foreach (var claim in claims)
                telemetry.Properties.Add($"claim[{claim.Type}]", claim.Value);

            #endregion

            request.Properties.Add(HttpRequestMessagePropertyRequestTelemetryKey, telemetry);
            var response = await continueExecution(controllerType, httpApp, request, routeName);

            telemetry.ResponseCode = response.StatusCode.ToString();
            if (response.ReasonPhrase.HasBlackSpace())
                telemetry.Properties.AddOrReplace("reason_phrase", response.ReasonPhrase);
            telemetry.Success = response.IsSuccessStatusCode;

            #region Method result identfiers

            if (response.Headers.TryGetValues(ControllerHandler.HeaderStatusName, out IEnumerable<string> statusNames))
            {
                if (statusNames.Any())
                    telemetry.Properties.Add(TelemetryStatusName, statusNames.First());
            }
            if (response.Headers.TryGetValues(ControllerHandler.HeaderStatusInstance, out IEnumerable<string> statusInstances))
            {
                if(statusInstances.Any())
                    telemetry.Properties.Add(TelemetryStatusInstance, statusInstances.First());
            }

            #endregion

            var telemetryClient = AppSettings.ApplicationInsights.InstrumentationKey.LoadTelemetryClient();
            telemetry.Duration = stopwatch.Elapsed;
            telemetryClient.TrackRequest(telemetry);

            return response;
        }
 
        public async Task<HttpResponseMessage> HandleMethodAsync(MethodInfo method,
            KeyValuePair<ParameterInfo, object>[] queryParameters, 
            IApplication httpApp, HttpRequestMessage request, 
            MethodHandlingDelegate continueExecution)
        {
            var telemetry = request.GetRequestTelemetry();
            telemetry.Name = $"{request.Method} - {method.DeclaringType.FullName}..{method.Name}";
            var response = await continueExecution(method, queryParameters, httpApp, request);

            return response;

        }

        public async Task<HttpResponseMessage> HandleExceptionAsync(Exception ex, 
            MethodInfo method, KeyValuePair<ParameterInfo, object>[] queryParameters, 
            IApplication httpApp, HttpRequestMessage request,
            HandleExceptionDelegate continueExecution)
        {
            var telemetry = request.GetRequestTelemetry();
            var telemetryEx = new ExceptionTelemetry()
            {
                ProblemId = telemetry.Id,
                Message = telemetry.Name,
                Timestamp = telemetry.Timestamp,
            };

            telemetryEx.Properties.Add("url", request.RequestUri.OriginalString);
            telemetryEx.Properties.Add("method", request.Method.ToString());

            foreach (var header in request.Headers)
                telemetryEx.Properties.Add($"header[{header.Key}]", header.Value.Join(","));

            var boundParameters = queryParameters.Where(
                queryParameter => queryParameter.Key.ParameterType.ContainsAttributeInterface<IBindApiValue>());
            foreach (var queryParameter in queryParameters)
                telemetryEx.Properties.Add($"parameter[{queryParameter.Key.Name}]", queryParameter.Value.ToString());

            if (!request.Content.IsDefaultOrNull())
            {
                foreach (var header in request.Content.Headers)
                    telemetryEx.Properties.Add($"header_content[{header.Key}]", header.Value.Join(","));
                try
                {
                    var contentData = await request.Content.ReadAsByteArrayAsync();
                    telemetryEx.Properties.Add("content", contentData.ToBase64String());
                }
                catch (Exception)
                {
                }
            }
            var telemetryClient = AppSettings.ApplicationInsights.InstrumentationKey.LoadTelemetryClient();
            telemetryClient.TrackException(telemetryEx);
            return await continueExecution(ex, method, queryParameters, httpApp, request);
        }
    }

    public static class ApplicationInsightsRouteHandlerExtensions
    {

        public static RequestTelemetry GetRequestTelemetry(this HttpRequestMessage message)
        {
            return (RequestTelemetry)message.Properties[
                ApplicationInsightsRouteHandlerAttribute.HttpRequestMessagePropertyRequestTelemetryKey];
        }
    }
}

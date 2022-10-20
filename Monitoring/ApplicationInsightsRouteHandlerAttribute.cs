using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using Microsoft.ApplicationInsights.DataContracts;

using EastFive.Api;
using EastFive.Api.Core;
using EastFive.Api.Modules;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using EastFive.Linq;

namespace EastFive.Azure.Monitoring
{
    public class ApplicationInsightsRouteHandlerAttribute : Attribute, IHandleRoutes, IHandleMethods, IHandleExceptions
    {
        public const string TelemetryStatusType = "StatusType";
        public const string TelemetryStatusName = "StatusName";

        internal const string HttpRequestMessagePropertyRequestTelemetryKey = "e5_monitoring_requesttelemetry_key";

        public async Task<IHttpResponse> HandleRouteAsync(Type controllerType, IInvokeResource resourceInvoker,
                IApplication httpApp, IHttpRequest routeData,
            RouteHandlingDelegate continueExecution)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N");
            var telemetry = new RequestTelemetry()
            {
                Id = requestId,
                Source = controllerType.Assembly.FullName,
                Timestamp = DateTimeOffset.UtcNow,
                Url = routeData.GetAbsoluteUri(), // request.RequestUri,
            };

            #region User / Session

            var claims = routeData.GetClaims(
                claimsEnumerable => claimsEnumerable.ToArray(),
                () => new Claim[] { },
                (why) => new Claim[] { });
            var sessionIdClaimType = Api.Auth.ClaimEnableSessionAttribute.Type;
            var sessionIdMaybe = EastFive.Azure.Auth.SessionToken.GetClaimIdMaybe(claims, sessionIdClaimType);
            if (sessionIdMaybe.HasValue)
                telemetry.Context.Session.Id = sessionIdMaybe.Value.ToString().ToUpper();
            
            var accountIdClaimType = EastFive.Api.AppSettings.ActorIdClaimType.ConfigurationString(
                (accIdCT) => accIdCT,
                (why) => default);
            if (accountIdClaimType.HasBlackSpace())
            {
                var accountIdMaybe = EastFive.Azure.Auth.SessionToken.GetClaimIdMaybe(claims, accountIdClaimType);
                if (accountIdMaybe.HasValue)
                {
                    var accountIdStr = accountIdMaybe.Value.ToString().ToUpper();
                    telemetry.Context.User.AccountId = accountIdStr;
                    telemetry.Context.User.AuthenticatedUserId = accountIdStr;
                }
            }

            foreach (var claim in claims.Distinct(claim => claim.Type))
                telemetry.Properties.Add($"claim[{claim.Type}]", claim.Value);

            #endregion

            routeData.Properties.Add(HttpRequestMessagePropertyRequestTelemetryKey, telemetry);
            var response = await continueExecution(controllerType, httpApp, routeData);

            telemetry.ResponseCode = response.StatusCode.ToString();
            if (response.ReasonPhrase.HasBlackSpace())
                telemetry.Properties.AddOrReplace("reason_phrase", response.ReasonPhrase);
            telemetry.Success = response.StatusCode.IsSuccess();

            #region Method result identfiers

            if (response.Headers.TryGetValue(Middleware.HeaderStatusType, out string[] statusNames))
            {
                if (statusNames.Any())
                    telemetry.Properties.Add(TelemetryStatusType, statusNames.First());
            }
            if (response.Headers.TryGetValue(Middleware.HeaderStatusName, out string[] statusInstances))
            {
                if(statusInstances.Any())
                    telemetry.Properties.Add(TelemetryStatusName, statusInstances.First());
            }

            #endregion

            var telemetryClient = AppSettings.ApplicationInsights.InstrumentationKey.LoadTelemetryClient();
            telemetry.Duration = stopwatch.Elapsed;
            telemetryClient.TrackRequest(telemetry);

            return response;
        }
 
        public async Task<IHttpResponse> HandleMethodAsync(MethodInfo method,
            KeyValuePair<ParameterInfo, object>[] queryParameters, 
            IApplication httpApp, IHttpRequest request, 
            MethodHandlingDelegate continueExecution)
        {
            var telemetry = request.GetRequestTelemetry();
            telemetry.Name = $"{request.Method} - {method.DeclaringType.FullName}..{method.Name}";
            var response = await continueExecution(method, queryParameters, httpApp, request);

            return response;

        }

        public async Task<IHttpResponse> HandleExceptionAsync(Exception ex, 
            MethodInfo method, KeyValuePair<ParameterInfo, object>[] queryParameters, 
            IApplication httpApp, IHttpRequest request,
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
            {
                if (queryParameter.Key == null)
                    continue;
                if (queryParameter.Value.IsNull())
                {
                    telemetryEx.Properties.Add($"parameter[{queryParameter.Key.Name}]", "--null--");
                    continue;
                }
                telemetryEx.Properties.Add($"parameter[{queryParameter.Key.Name}]", queryParameter.Value.ToString());
            }

            if (request.HasBody)
            {
                try
                {
                    var contentData = await request.ReadContentAsync();
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

        public static RequestTelemetry GetRequestTelemetry(this IHttpRequest message)
        {
            return (RequestTelemetry)message.Properties[
                ApplicationInsightsRouteHandlerAttribute.HttpRequestMessagePropertyRequestTelemetryKey];
        }
    }
}

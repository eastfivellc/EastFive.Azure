using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth
{
    public class AccessTokenAccessAttribute : Attribute, IHandleRoutes
    {
        public Task<HttpResponseMessage> HandleRouteAsync(Type controllerType,
            IApplication httpApp, HttpRequestMessage request, string routeName,
            RouteHandlingDelegate continueExecution)
        {
            if (!request.RequestUri.TryGetQueryParam(
                    AccessTokenAccountExtensions.QueryParameter,
                    out string accessToken))
                return continueExecution(controllerType, httpApp, request, routeName);

            if (!request.Headers.Authorization.IsDefaultOrNull())
                if (request.Headers.Authorization.Scheme.HasBlackSpace())
                    return continueExecution(controllerType, httpApp, request, routeName);

            return request.RequestUri.ValidateAccessTokenAccount(
                accessTokenInfo =>
                {
                    return EastFive.Security.AppSettings.TokenScope.ConfigurationUri(
                        scope =>
                        {
                            var tokenExpiration = TimeSpan.FromMinutes(1.0);
                            request.RequestUri = request.RequestUri.RemoveQueryParameter(
                                AccessTokenAccountExtensions.QueryParameter);
                            var sessionId = accessTokenInfo.sessionId;
                            var authId = accessTokenInfo.accountId;
                            var duration = accessTokenInfo.expirationUtc - DateTime.UtcNow;
                            return JwtTools.CreateToken(sessionId, authId, scope, duration,
                                tokenCreated:
                                    (tokenNew) =>
                                    {
                                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(tokenNew);
                                        return continueExecution(controllerType, httpApp, request, routeName);
                                    },
                                missingConfigurationSetting:
                                    (configName) => continueExecution(controllerType, httpApp, request, routeName),
                                invalidConfigurationSetting:
                                    (configName, issue) => continueExecution(controllerType, httpApp, request, routeName));
                        },
                        (why) => continueExecution(controllerType, httpApp, request, routeName),
                        () => continueExecution(controllerType, httpApp, request, routeName));
                },
                () => continueExecution(controllerType, httpApp, request, routeName),
                () => continueExecution(controllerType, httpApp, request, routeName),
                () => continueExecution(controllerType, httpApp, request, routeName),
                () => continueExecution(controllerType, httpApp, request, routeName),
                () => continueExecution(controllerType, httpApp, request, routeName));
        }
    }
}

using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
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
        public bool ShouldSkipValidationForLocalhost { get; set; }

        public async Task<IHttpResponse> HandleRouteAsync(Type controllerType, IInvokeResource resourceInvoker,
            IApplication httpApp, IHttpRequest request,
            RouteHandlingDelegate continueExecution)
        {
            if (!request.RequestUri.TryGetQueryParam(
                    AccessTokenAccountExtensions.QueryParameter,
                    out string accessToken))
                return await continueExecution(controllerType, httpApp, request);
            
            if (request.GetAuthorization().HasBlackSpace())
                return await continueExecution(controllerType, httpApp, request);

            return await request.ValidateAccessTokenAccount(this.ShouldSkipValidationForLocalhost,
                accessTokenInfo =>
                {
                    return EastFive.Security.AppSettings.TokenScope.ConfigurationUri(
                        async (scope) =>
                        {
                            var tokenExpiration = TimeSpan.FromMinutes(1.0);
                            request.RequestUri = request.RequestUri.RemoveQueryParameter(
                                AccessTokenAccountExtensions.QueryParameter);
                            var sessionId = accessTokenInfo.sessionId;
                            var authId = accessTokenInfo.accountId;
                            var claims = await await sessionId.AsRef<Session>().StorageGetAsync(
                                (session) => session.authorization.StorageGetAsync(
                                    (auth) => auth.claims,
                                    () => new Dictionary<string, string>() { }),
                                () => ((IDictionary<string, string>)new Dictionary<string, string>() { }).AsTask());
                            var duration = accessTokenInfo.expirationUtc - DateTime.UtcNow;
                            return await JwtTools.CreateToken(sessionId, authId, scope, duration,
                                claims,
                                tokenCreated:
                                    (tokenNew) =>
                                    {
                                        request.SetAuthorization(tokenNew);
                                        return continueExecution(controllerType, httpApp, request);
                                    },
                                missingConfigurationSetting:
                                    (configName) => continueExecution(controllerType, httpApp, request),
                                invalidConfigurationSetting:
                                    (configName, issue) => continueExecution(controllerType, httpApp, request));
                        },
                        (why) => continueExecution(controllerType, httpApp, request),
                        () => continueExecution(controllerType, httpApp, request));
                },
                onAccessTokenNotProvided: () => continueExecution(controllerType, httpApp, request),
                onAccessTokenInvalid:
                    () =>
                    {
                        return request
                            .CreateResponse(System.Net.HttpStatusCode.Forbidden)
                            .AddReason("Access token is invalid")
                            .AsTask();
                    },
                onAccessTokenExpired:
                    () =>
                    {
                        return request
                            .CreateResponse(System.Net.HttpStatusCode.Forbidden)
                            .AddReason("Access token is expired")
                            .AsTask();
                    },
                onInvalidSignature:
                    () =>
                    {
                        return request
                            .CreateResponse(System.Net.HttpStatusCode.Forbidden)
                            .AddReason("Access token has an invalid signature")
                            .AsTask();
                    },
                onSystemNotConfigured: () => continueExecution(controllerType, httpApp, request));
        }
    }
}

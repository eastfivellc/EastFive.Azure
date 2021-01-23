using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Security.SessionServer;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth.Instigations
{
    [ImplicitStoredSession]
    public struct ImplicitStoredSession
    {
        public Guid sessionId;
        public Guid? performingAsActorId;
        public System.Security.Claims.Claim[] claims;
    }

    public class ImplicitStoredSessionAttribute : Attribute, IInstigatable
    {
        public Task<IHttpResponse> Instigate(IApplication httpApp,
                IHttpRequest request, ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            return request.GetClaims(
                (claimsEnumerable) =>
                {
                    return ResponseFromClaimsAsync(claimsEnumerable);
                },
                () => CreateSessionAsync(),
                (why) => CreateSessionAsync());

            Task<IHttpResponse> ResponseFromClaimsAsync(IEnumerable<System.Security.Claims.Claim> claimsEnumerable)
            {
                var claims = claimsEnumerable.ToArray();
                var sessionIdClaimType = EastFive.Api.Auth.ClaimEnableSessionAttribute.Type;
                return claims
                    .Where(claim => String.Compare(claim.Type, sessionIdClaimType) == 0)
                    .First(
                        (sessionClaim, next) =>
                        {
                            var sessionId = Guid.Parse(sessionClaim.Value);
                            var security = new ImplicitStoredSession
                            {
                                claims = claims,
                                sessionId = sessionId,
                                performingAsActorId = claims
                                    .Where(claim => claim.Type == EastFive.Api.Auth.ClaimEnableActorAttribute.Type)
                                    .First(
                                        (claim, next) =>
                                        {
                                            if (Guid.TryParse(claim.Value, out Guid sId))
                                                return sId;
                                            return default(Guid?);
                                        },
                                        () => default(Guid?)),
                            };
                            return onSuccess(security);
                        },
                        () => request.CreateResponse(HttpStatusCode.Unauthorized).AsTask());
            }

            Task<IHttpResponse> CreateSessionAsync()
            {
                return request.ReadCookie("e5-session",
                    sessionTokenStr =>
                    {
                        return sessionTokenStr.GetClaimsFromJwtToken(
                            claims =>
                            {
                                return ResponseFromClaimsAsync(claims);
                            },
                            () => CreateAsync(),
                            (why) => CreateAsync());
                    },
                    () =>
                    {
                        return CreateAsync();
                    });

                Task<IHttpResponse> CreateAsync()
                {
                    var sessionId = Security.SecureGuid.Generate();
                    return Security.AppSettings.TokenScope.ConfigurationUri(
                        scope =>
                        {
                            var claims = new Dictionary<string, string>
                            {
                                { Api.Auth.ClaimEnableSessionAttribute.Type, sessionId.ToString("N") }
                            };
                            return Api.Auth.JwtTools.CreateToken(sessionId,
                                    scope, TimeSpan.FromDays(365),
                                    claims,
                                async (tokenNew) =>
                                {
                                    var security = new ImplicitStoredSession
                                    {
                                            //claims = claims,
                                            sessionId = sessionId,
                                        performingAsActorId = default(Guid?),
                                    };
                                    var result = await onSuccess(security);
                                    result.WriteCookie("e5-session", tokenNew, TimeSpan.FromDays(365));
                                    return result;
                                },
                                (missingConfig) => request
                                    .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                                    .AddReason(missingConfig).AsTask(),
                                (configName, issue) => request
                                    .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                                    .AddReason($"{configName}:{issue}").AsTask());
                        });
                }
            }
        }
    }
}

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
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Security.SessionServer;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth.Instigations
{
    [ImplicitStoredSession]
    public struct ImplicitStoredSession
    {
        public IRef<Session> session;
        public Guid? performingAsActorId;
        public System.Security.Claims.Claim[] claims;
    }

    public class ImplicitStoredSessionAttribute : Attribute, IInstigatable, IConfigureAuthorization
    {
        public bool IsAnonymousSessionAllowed => true;

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
                        async (sessionClaim, next) =>
                        {
                            if (!Ref<Session>.TryParse(sessionClaim.Value, out IRef<Session> sessionRef))
                                return request.CreateResponse(HttpStatusCode.Unauthorized);

                            return await await sessionRef.StorageGetAsync(
                                session =>
                                {
                                    var security = new ImplicitStoredSession
                                    {
                                        claims = claims,
                                        session = sessionRef,
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
                                () => CreateAsync());
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
            }

            Task<IHttpResponse> CreateAsync()
            {
                var sessionRef = Ref<Session>.SecureRef();
                return Security.AppSettings.TokenScope.ConfigurationUri(
                    scope =>
                    {
                        var claims = new Dictionary<string, string>
                        {
                            { 
                                Api.Auth.ClaimEnableSessionAttribute.Type, 
                                sessionRef.id.ToString("N")
                            }
                        };
                        return Api.Auth.JwtTools.CreateToken(sessionRef.id,
                                scope, TimeSpan.FromDays(365), claims,
                            async (tokenNew) =>
                            {
                                var session = new Session
                                {
                                    sessionId = sessionRef,
                                    account = default,
                                    authorization = RefOptional<Authorization>.Empty(),
                                    authorized = false,
                                    refreshToken = Security.SecureGuid.Generate().ToString("N"),
                                    token = tokenNew,
                                    created = DateTime.UtcNow,
                                };
                                var storagedSession = await session.StorageCreateAsync(
                                    discard => session);
                                var security = new ImplicitStoredSession
                                {
                                        //claims = claims,
                                        session = sessionRef,
                                    performingAsActorId = default(Guid?),
                                };
                                var result = await onSuccess(security);
                                result.AddCookie("e5-session", tokenNew, TimeSpan.FromDays(365));
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

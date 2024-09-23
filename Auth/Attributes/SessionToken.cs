using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;
using EastFive.Api.Meta.Flows;
using EastFive.Api.Meta.Postman.Resources.Collection;
using EastFive.Api.Auth;
using EastFive.Api;
using EastFive.Azure.Login;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Auth.CredentialProviders.Voucher;

namespace EastFive.Azure.Auth
{
    [SessionToken]
    [AuthorizationToken]
    public struct SessionToken
    {
        public Guid sessionId;
        public System.Security.Claims.Claim[] claims;
        public Guid? accountIdMaybe;

        public static Guid? GetClaimIdMaybe(IEnumerable<System.Security.Claims.Claim> claims,
            string claimType)
        {
            return claims.First(
                (claim, next) =>
                {
                    if (String.Compare(claim.Type, claimType) != 0)
                        return next();
                    if(Guid.TryParse(claim.Value, out Guid accountId))
                        return accountId;

                    return default(Guid?);
                },
                () => default(Guid?));
        }

        public async Task<string> GetDescriptionOrOtherLabel<TAccount>(
            Func<TAccount, string> extractDescriptionOrOtherLabel)
            where TAccount : IReferenceable
        {
            var logon = this;
            return logon.accountIdMaybe.HasValue ?
                await await logon.accountIdMaybe.Value.AsRef<TAccount>().StorageGetAsync(
                    (x) => extractDescriptionOrOtherLabel(x).AsTask(),
                    () => VoucherToken.FindByAuthId(logon.accountIdMaybe.Value)
                        .FirstAsync(
                            (item) => item.DescriptionOrOtherLabel,
                            () => "unknown"))
                :
                "unknown";
        }
    }

    public class SessionTokenAttribute : Attribute, IInstigatable
    {
        public Task<IHttpResponse> Instigate(IApplication httpApp,
                IHttpRequest request, ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            return request.GetClaims(
                (claimsEnumerable) =>
                {
                    var claims = claimsEnumerable.ToArray();
                    return claims.GetAccountIdMaybe(
                            request, ClaimEnableActorAttribute.Type,
                        (accountIdMaybe) =>
                        {
                            var sessionIdClaimType = ClaimEnableSessionAttribute.Type;
                            return claims.GetSessionIdAsync(
                                            request, sessionIdClaimType,
                                        (sessionId) =>
                                        {
                                            var token = new SessionToken
                                            {
                                                accountIdMaybe = accountIdMaybe,
                                                sessionId = sessionId,
                                                claims = claims,
                                            };
                                            return onSuccess(token);
                                        });
                        });
                },
                () => request
                    .CreateResponse(HttpStatusCode.Unauthorized)
                    .AddReason("Authorization header not set.")
                    .AsTask(),
                (why) => request
                    .CreateResponse(HttpStatusCode.Unauthorized)
                    .AddReason(why)
                    .AsTask());
        }
    }

    [SessionTokenMaybe]
    [AuthorizationToken]
    public struct SessionTokenMaybe
    {
        public Guid? sessionId;
        public System.Security.Claims.Claim[] claims;
        public Guid? accountIdMaybe;

        public static Guid? GetClaimIdMaybe(IEnumerable<System.Security.Claims.Claim> claims,
            string claimType)
        {
            return claims.First(
                (claim, next) =>
                {
                    if (String.Compare(claim.Type, claimType) != 0)
                        return next();
                    var accountId = Guid.Parse(claim.Value);
                    return accountId;
                },
                () => default(Guid?));
        }
    }

    public class SessionTokenMaybeAttribute : Attribute, IInstigatable
    {
        public Task<IHttpResponse> Instigate(IApplication httpApp,
                IHttpRequest request, ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            return request.GetClaims(
                (claimsEnumerable) =>
                {
                    var claims = claimsEnumerable.ToArray();
                    return claims.GetAccountIdMaybe(
                            request, ClaimEnableActorAttribute.Type,
                        (accountIdMaybe) =>
                        {
                            var sessionIdClaimType = ClaimEnableSessionAttribute.Type;
                            return claims.GetSessionIdAsync(
                                            request, sessionIdClaimType,
                                        (sessionId) =>
                                        {
                                            var token = new SessionTokenMaybe
                                            {
                                                accountIdMaybe = accountIdMaybe,
                                                sessionId = sessionId,
                                                claims = claims,
                                            };
                                            return onSuccess(token);
                                        });
                        });
                },
                () =>
                {
                    var token = new SessionTokenMaybe
                    {
                        accountIdMaybe = default,
                        sessionId = default,
                        claims = new System.Security.Claims.Claim[] { },
                    };
                    return onSuccess(token);
                },
                (why) => request
                    .CreateResponse(HttpStatusCode.Unauthorized)
                    .AddReason(why)
                    .AsTask());
        }
    }

}

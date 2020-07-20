using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI;
using EastFive.Azure.Auth.CredentialProviders.Voucher;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Serialization;
using EastFive.Web.Configuration;

namespace EastFive.Api.Auth
{
    public class ApiKeyAccessAttribute : Attribute, IHandleRoutes
    {
        public Task<HttpResponseMessage> HandleRouteAsync(Type controllerType, 
            IApplication httpApp, HttpRequestMessage request, string routeName,
            RouteHandlingDelegate continueExecution)
        {
            if (!request.RequestUri.TryGetQueryParam("api-voucher", out string apiVoucher))
                return continueExecution(controllerType, httpApp, request, routeName);

            if(!request.Headers.Authorization.IsDefaultOrNull())
                if (request.Headers.Authorization.Scheme.HasBlackSpace())
                    return continueExecution(controllerType, httpApp, request, routeName);

            return EastFive.Security.VoucherTools.ValidateUrlToken(apiVoucher,
                async (voucherTokenId) =>
                {
                    return await await voucherTokenId.AsRef<VoucherToken>()
                        .StorageGetAsync(
                            voucherToken =>
                            {
                                return EastFive.Security.AppSettings.TokenScope.ConfigurationUri(
                                    scope =>
                                    {
                                        var tokenExpiration = TimeSpan.FromMinutes(1.0);
                                        request.RequestUri = request.RequestUri.RemoveQueryParameter("api-voucher");
                                        var sessionId = apiVoucher.MD5HashGuid();
                                        var claims = voucherToken.claims;
                                        return JwtTools.CreateToken(sessionId,
                                                scope, tokenExpiration, claims,
                                            (tokenNew) =>
                                            {
                                                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(tokenNew);
                                                return continueExecution(controllerType, httpApp, request, routeName);
                                            },
                                            (missingConfig) => continueExecution(controllerType, httpApp, request, routeName),
                                            (configName, issue) => continueExecution(controllerType, httpApp, request, routeName));
                                    },
                                    (why) => continueExecution(controllerType, httpApp, request, routeName));
                            },
                            () => request
                                .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                                .AddReason("Voucher token does not exist.")
                                .AsTask());
                },
                why => request
                    .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                    .AddReason(why)
                    .AsTask(),
                why => request
                        .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                        .AddReason(why)
                        .AsTask(),
                    why => request
                        .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                        .AddReason(why)
                        .AsTask(),
                    why => request
                        .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                        .AddReason(why)
                        .AsTask(),
                    (name, why) => request
                        .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                        .AddReason(why)
                        .AsTask());
        }
    }
}

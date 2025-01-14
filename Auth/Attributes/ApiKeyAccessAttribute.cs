using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Auth.CredentialProviders.Voucher;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Serialization;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth
{
    public class ApiKeyAccessAttribute : Attribute, IHandleRoutes
    {
        public const string ParameterName = "api-voucher";

        public Task<IHttpResponse> HandleRouteAsync(Type controllerType, IInvokeResource resourceInvoker,
            IApplication httpApp, IHttpRequest request,
            RouteHandlingDelegate continueExecution)
        {
            if (!request.RequestUri.TryGetQueryParam(ParameterName, out string apiVoucher))
                if (!request.TryGetHeader(ParameterName, out apiVoucher))
                    return continueExecution(controllerType, httpApp, request);

            if(request.TryGetAuthorization(out string auth))
                return continueExecution(controllerType, httpApp, request);

            return EastFive.Security.VoucherTools.ValidateUrlToken(apiVoucher,
                async (voucherTokenId) =>
                {
                    return await await voucherTokenId.AsRef<VoucherToken>()
                        .StorageGetAsync(
                            async voucherToken =>
                            {
                                if (voucherToken.expiration <= DateTime.UtcNow)
                                    return request
                                        .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                                        .AddReason("Token has expired.");

                                return await EastFive.Security.AppSettings.TokenScope.ConfigurationUri(
                                    scope =>
                                    {
                                        var tokenExpiration = TimeSpan.FromMinutes(1.0);
                                        request.RequestUri = request.RequestUri.RemoveQueryParameter(ParameterName);
                                        var sessionId = apiVoucher.MD5HashGuid();
                                        var claims = voucherToken.claims;
                                        return JwtTools.CreateToken(sessionId,
                                                scope, tokenExpiration, claims,
                                            (tokenNew, whenIssued) =>
                                            {
                                                request.SetAuthorization(tokenNew);
                                                return continueExecution(controllerType, httpApp, request);
                                            },
                                            (missingConfig) => continueExecution(controllerType, httpApp, request),
                                            (configName, issue) => continueExecution(controllerType, httpApp, request));
                                    },
                                    (why) => continueExecution(controllerType, httpApp, request));
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

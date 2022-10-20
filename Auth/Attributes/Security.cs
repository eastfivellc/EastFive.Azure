using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using EastFive;
using EastFive.Linq;
using EastFive.Api.Auth;
using EastFive.Api.Meta.Flows;
using EastFive.Api.Meta.Postman.Resources.Collection;
using EastFive.Api.Resources;
using EastFive.Azure.Auth;

namespace EastFive.Api // TODO: Move to EastFive.Azure.Auth
{
    [Security]
    [AuthorizationToken]
    public struct Security
    {
        public Guid performingAsActorId;
        public Guid sessionId;
        public System.Security.Claims.Claim[] claims;
    }

    public class SecurityAttribute : Attribute, IInstigatable
    {
        public Task<IHttpResponse> Instigate(IApplication httpApp,
                IHttpRequest request, ParameterInfo parameterInfo, 
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            return request
                .GetActorIdClaimsAsync(
                    (actorId, claims) =>
                    {
                        var security = new Security
                        {
                            performingAsActorId = actorId,
                            claims = claims,
                            sessionId = claims
                                .Where(claim => claim.Type == Auth.ClaimEnableSessionAttribute.Type)
                                .First(
                                    (claim, next) =>
                                    {
                                        if (Guid.TryParse(claim.Value, out Guid sId))
                                            return sId;
                                        return default;
                                    },
                                    () => default(Guid)),
                        };
                        return onSuccess(security);
                    });
        }
    }
}

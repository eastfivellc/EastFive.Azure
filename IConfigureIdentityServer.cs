﻿using BlackBarLabs.Api.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Routing;
using System.Security.Claims;

namespace EastFive.Security.SessionServer
{
    public interface IConfigureIdentityServer
    {
        WebId GetActorLink(Guid actorId, UrlHelper urlHelper);

        Task<bool> CanAdministerCredentialAsync(Guid actorInQuestion, Guid actorTakingAction, System.Security.Claims.Claim[] claims);

        TResult GetRedirectUri<TResult>(CredentialValidationMethodTypes validationType,
                Guid? authorizationId,
                string token, string refreshToken,
                IDictionary<string, string> authParams,
            Func<Uri, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure);
    }
}

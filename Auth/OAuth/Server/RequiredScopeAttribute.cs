using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using EastFive.Extensions;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// Requires the bearer token to carry the given OAuth scope in its
    /// <see cref="OAuthServer.ScopeClaimType"/> ("scp") claim (space delimited).
    /// Session tokens (no scp claim) are rejected unless
    /// <see cref="AllowTokensWithoutScopes"/> is set (first-party sessions).
    /// </summary>
    public class RequiredScopeAttribute : AuthorizationTokenAttribute, IHandleMethodInvocation
    {
        public string Scope { get; }

        /// <summary>
        /// When true, tokens WITHOUT any scp claim (i.e. first-party session tokens,
        /// which are not scope-limited) also pass. Default true.
        /// </summary>
        public bool AllowTokensWithoutScopes { get; set; } = true;

        public RequiredScopeAttribute(string scope)
        {
            this.Scope = scope;
        }

        public Task<IHttpResponse> HandleMethodInvocationAsync(
            KeyValuePair<ParameterInfo, object>[] parameters,
            IReadOnlyDictionary<ParameterInfo, object> bindingContexts,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            InvokeMethodDelegate continueInvocation)
        {
            var scopeRequired = this.Scope;
            return request.GetClaims(
                claimsEnumerable =>
                {
                    var scopeClaim = claimsEnumerable
                        .Where(claim => String.Equals(claim.Type, OAuthServer.ScopeClaimType, StringComparison.Ordinal))
                        .Select(claim => claim.Value)
                        .FirstOrDefault();

                    if (scopeClaim.IsNullOrWhiteSpace())
                    {
                        if (AllowTokensWithoutScopes)
                            return continueInvocation(parameters, bindingContexts, method, httpApp, request);
                        return Forbidden("The token does not carry any OAuth scopes.");
                    }

                    var granted = scopeClaim
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Any(scopeGranted => String.Equals(scopeGranted, scopeRequired, StringComparison.Ordinal));
                    if (granted)
                        return continueInvocation(parameters, bindingContexts, method, httpApp, request);

                    return Forbidden($"The token does not include the required scope `{scopeRequired}`.");
                },
                () => Forbidden("Authentication required."),
                why => Forbidden($"Authentication failed: {why}"));

            Task<IHttpResponse> Forbidden(string why) =>
                request
                    .CreateResponse(System.Net.HttpStatusCode.Forbidden)
                    .AddReason(why)
                    .AsTask();
        }
    }
}

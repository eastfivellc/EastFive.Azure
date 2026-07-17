using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using EastFive.Extensions;
using EastFive.Linq;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// Requires the bearer token to carry the given OAuth scope in its
    /// <see cref="OAuthServer.ScopeClaimType"/> ("scp") claim (space delimited).
    /// Session tokens (no scp claim) are rejected unless
    /// <see cref="AllowTokensWithoutScopes"/> is set (first-party sessions).
    /// Challenge semantics follow RFC 6750 §3 + RFC 9728 §5.1 — the 401
    /// WWW-Authenticate header carries resource_metadata="…" which is how MCP
    /// clients discover the authorization server and begin the OAuth flow.
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
                        .Where(claim =>
                            String.Equals(claim.Type, OAuthServer.ScopeClaimType, StringComparison.Ordinal)
                            || String.Equals(claim.Type, OAuthServer.ScopeClaimTypeMapped, StringComparison.Ordinal))
                        .Select(claim => claim.Value)
                        .FirstOrDefault();

                    if (scopeClaim.IsNullOrWhiteSpace())
                    {
                        if (AllowTokensWithoutScopes)
                            return continueInvocation(parameters, bindingContexts, method, httpApp, request);
                        return InsufficientScope("The token does not carry any OAuth scopes.");
                    }

                    var granted = scopeClaim
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Any(scopeGranted => String.Equals(scopeGranted, scopeRequired, StringComparison.Ordinal));
                    if (granted)
                        return continueInvocation(parameters, bindingContexts, method, httpApp, request);

                    return InsufficientScope($"The token does not include the required scope `{scopeRequired}`.");
                },
                // No credentials presented — the bare challenge tells MCP clients where
                // to find the protected-resource metadata (RFC 9728 §5.1).
                () => Challenge(System.Net.HttpStatusCode.Unauthorized,
                    "Authentication required.", errorCode: default),
                why => Challenge(System.Net.HttpStatusCode.Unauthorized,
                    $"Authentication failed: {why}", errorCode: "invalid_token"));

            // RFC 6750 §3.1: valid token lacking the needed scope → 403 insufficient_scope.
            Task<IHttpResponse> InsufficientScope(string why) =>
                Challenge(System.Net.HttpStatusCode.Forbidden, why,
                    errorCode: "insufficient_scope", includeScope: true);

            Task<IHttpResponse> Challenge(System.Net.HttpStatusCode statusCode, string why,
                string errorCode, bool includeScope = false)
            {
                var challengeParams = new List<string>
                {
                    $"resource_metadata=\"{OAuthServer.ProtectedResourceMetadataUrl(request.RequestUri)}\"",
                };
                if (errorCode.HasBlackSpace())
                    challengeParams.Insert(0, $"error=\"{errorCode}\"");
                if (includeScope)
                    challengeParams.Add($"scope=\"{scopeRequired}\"");
                var response = request
                    .CreateResponse(statusCode)
                    .AddReason(why);
                response.Headers["WWW-Authenticate"] =
                    new[] { $"Bearer {challengeParams.Join(", ")}" };
                return response.AsTask();
            }
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Extensions;
using EastFive.Linq;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// GET /oauth/tokeninfo — inspect the presented bearer token. Serves two purposes:
    /// (1) lets OAuth clients (and the MCP server's developers) verify a token
    /// end-to-end, and (2) is the reference pattern for MCP endpoints: gate with
    /// [RequiredScope(OAuthServer.McpScope, AllowTokensWithoutScopes = false)] and the
    /// 401/403 challenges carry the RFC 9728 resource_metadata pointer automatically.
    /// </summary>
    [FunctionViewController(
        Namespace = "oauth",
        Route = "tokeninfo",
        ContentType = "application/json")]
    public class OAuthTokenInfo
    {
        [RequiredScope(OAuthServer.McpScope, AllowTokensWithoutScopes = false)]
        [HttpGet(MatchAllParameters = false)]
        public static IHttpResponse Get(
                SessionTokenMaybe security,
            ContentTypeResponse<TokenInfoResponse> onFound)
        {
            string ClaimValue(string type) =>
                security.claims
                    .NullToEmpty()
                    .Where(claim => String.Equals(claim.Type, type, StringComparison.Ordinal))
                    .Select(claim => claim.Value)
                    .FirstOrDefault();

            var response = new TokenInfoResponse
            {
                ClientId = ClaimValue(OAuthServer.ClientIdClaimType),
                Scope = ClaimValue(OAuthServer.ScopeClaimType)
                    ?? ClaimValue(OAuthServer.ScopeClaimTypeMapped),
                Account = security.accountIdMaybe,
                SessionId = security.sessionId ?? default,
            };
            return onFound(response, "application/json");
        }
    }

    public class TokenInfoResponse
    {
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }

        [JsonProperty("account", NullValueHandling = NullValueHandling.Ignore)]
        public Guid? Account { get; set; }

        [JsonProperty("session_id")]
        public Guid SessionId { get; set; }
    }
}

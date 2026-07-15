using System;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// POST /oauth/revoke — OAuth 2.0 Token Revocation (RFC 7009).
    /// Only refresh tokens are revocable (access tokens are short-lived stateless JWTs);
    /// revoking a refresh token revokes its whole rotation family.
    /// Per RFC 7009 §2.2 the endpoint returns 200 even for unknown tokens.
    /// </summary>
    [FunctionViewController(
        Namespace = "oauth",
        Route = "revoke",
        ContentType = "application/json")]
    public class OAuthRevocation
    {
        [Unsecured("RFC 7009 token revocation - authenticated by possession of the token being revoked; responds 200 regardless of token validity per spec.")]
        [HttpPost(MatchAllParameters = false)]
        public static async Task<IHttpResponse> RevokeAsync(
                IHttpRequest request,
            NoContentResponse onRevoked)
        {
            var form = request.Form;
            if (form.IsDefaultOrNull())
                return onRevoked();

            var token = (string)form["token"];
            if (token.IsNullOrWhiteSpace())
                return onRevoked();

            var tokenRef = OAuthServer.ComputeLookupGuid(token).AsRef<OAuthRefreshTokenRecord>();
            await await tokenRef.StorageGetAsync(
                async tokenRecord =>
                {
                    if (!OAuthServer.SecretMatchesHash(token, tokenRecord.tokenHash))
                        return 0;
                    return await OAuthToken.RevokeFamilyAsync(tokenRecord.familyId);
                },
                () => 0.AsTask());

            return onRevoked();
        }
    }
}

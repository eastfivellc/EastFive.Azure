using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Web.Configuration;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// POST /oauth/token — OAuth 2.1 token endpoint (RFC 6749 §3.2).
    /// Grants: authorization_code (+ mandatory PKCE), refresh_token (rotating,
    /// with family reuse-detection). Access tokens are RSA JWTs signed with the
    /// same key/claims as session tokens, so the existing SessionToken instigator
    /// and role gates accept them unchanged.
    /// </summary>
    [FunctionViewController(
        Namespace = "oauth",
        Route = "token",
        ContentType = "application/json")]
    public class OAuthToken
    {
        [Unsecured("RFC 6749 token endpoint - clients authenticate via the request itself (PKCE code_verifier for public clients, client_secret for confidential clients); no bearer token exists yet.")]
        [HttpPost(MatchAllParameters = false)]
        public static async Task<IHttpResponse> TokenAsync(
                IHttpRequest request,
                IApplication application,
            ContentTypeResponse<TokenSuccessResponse> onIssued,
            BadRequestBodyResponse<OAuthTokenError> onError)
        {
            var form = request.Form;
            if (form.IsDefaultOrNull())
                return Error(onError, "invalid_request",
                    "The token request must be application/x-www-form-urlencoded (RFC 6749 s3.2).");

            var grantType = (string)form["grant_type"];
            var (clientId, clientSecret) = ReadClientAuthentication(request);

            if (clientId.IsNullOrWhiteSpace())
                return Error(onError, "invalid_client", "client_id is required.");

            return await await OAuthAuthorize.LoadClientAsync(clientId,
                async client =>
                {
                    if (!client.isActive)
                        return Error(onError, "invalid_client", "This client has been deactivated.");
                    if (!AuthenticateClient(client, clientSecret))
                        return Error(onError, "invalid_client", "Client authentication failed.");

                    if (grantType == ClientCredential.GrantTypeValues.AuthorizationCode)
                        return await AuthorizationCodeGrantAsync(form, client, request, application, onIssued, onError);
                    if (grantType == ClientCredential.GrantTypeValues.RefreshToken)
                        return await RefreshTokenGrantAsync(form, client, request, application, onIssued, onError);
                    if (grantType == ClientCredential.GrantTypeValues.ClientCredentials)
                        return await ClientCredentialsGrantAsync(form, client, clientSecret, onIssued, onError);

                    return Error(onError, "unsupported_grant_type",
                        "Supported grant types: authorization_code, refresh_token, client_credentials.");
                },
                onNotFound: () => Error(onError, "invalid_client", "Unknown client_id.").AsTask());
        }

        #region grant_type=authorization_code

        private static async Task<IHttpResponse> AuthorizationCodeGrantAsync(
            Microsoft.AspNetCore.Http.IFormCollection form, ClientCredential client,
            IHttpRequest request, IApplication application,
            ContentTypeResponse<TokenSuccessResponse> onIssued,
            BadRequestBodyResponse<OAuthTokenError> onError)
        {
            var code = (string)form["code"];
            var codeVerifier = (string)form["code_verifier"];
            var redirectUri = (string)form["redirect_uri"];

            if (code.IsNullOrWhiteSpace())
                return Error(onError, "invalid_request", "code is required.");
            if (codeVerifier.IsNullOrWhiteSpace())
                return Error(onError, "invalid_request", "code_verifier is required (PKCE, RFC 7636).");

            var codeRef = OAuthServer.ComputeLookupGuid(code).AsRef<OAuthAuthorizationCode>();
            return await await codeRef.StorageGetAsync(
                async codeRecord =>
                {
                    // single-use: consume atomically before any validation response
                    var freshlyConsumed = await codeRef.StorageUpdateAsync2(
                        toUpdate =>
                        {
                            if (toUpdate.consumed)
                                return toUpdate;
                            toUpdate.consumed = true;
                            return toUpdate;
                        },
                        updated => !codeRecord.consumed,
                        () => false);
                    if (!freshlyConsumed)
                        return Error(onError, "invalid_grant", "The authorization code has already been used.");

                    if (!OAuthServer.SecretMatchesHash(code, codeRecord.codeHash))
                        return Error(onError, "invalid_grant", "Invalid authorization code.");
                    if (codeRecord.expiresOn < DateTime.UtcNow)
                        return Error(onError, "invalid_grant", "The authorization code has expired.");
                    if (!String.Equals(codeRecord.clientId, client.clientId, StringComparison.Ordinal))
                        return Error(onError, "invalid_grant", "The authorization code was issued to a different client.");
                    if (redirectUri.HasBlackSpace()
                            && !String.Equals(redirectUri, codeRecord.redirectUri, StringComparison.Ordinal))
                        return Error(onError, "invalid_grant", "redirect_uri does not match the authorization request.");
                    if (!OAuthServer.VerifyCodeChallenge(codeVerifier, codeRecord.codeChallenge))
                        return Error(onError, "invalid_grant", "PKCE verification failed.");

                    return await IssueTokensAsync(
                        client, codeRecord.accountId, codeRecord.authorization,
                        codeRecord.scope, codeRecord.resource,
                        familyId: Guid.NewGuid(),
                        sessionId: default,
                        request, application,
                        onIssued, onError);
                },
                () => Error(onError, "invalid_grant", "Invalid authorization code.").AsTask());
        }

        #endregion

        #region grant_type=refresh_token

        private static async Task<IHttpResponse> RefreshTokenGrantAsync(
            Microsoft.AspNetCore.Http.IFormCollection form, ClientCredential client,
            IHttpRequest request, IApplication application,
            ContentTypeResponse<TokenSuccessResponse> onIssued,
            BadRequestBodyResponse<OAuthTokenError> onError)
        {
            var refreshToken = (string)form["refresh_token"];
            if (refreshToken.IsNullOrWhiteSpace())
                return Error(onError, "invalid_request", "refresh_token is required.");

            var tokenRef = OAuthServer.ComputeLookupGuid(refreshToken).AsRef<OAuthRefreshTokenRecord>();
            return await await tokenRef.StorageGetAsync(
                async tokenRecord =>
                {
                    if (!OAuthServer.SecretMatchesHash(refreshToken, tokenRecord.tokenHash))
                        return Error(onError, "invalid_grant", "Invalid refresh token.");
                    if (!String.Equals(tokenRecord.clientId, client.clientId, StringComparison.Ordinal))
                        return Error(onError, "invalid_grant", "The refresh token was issued to a different client.");

                    if (tokenRecord.revoked)
                    {
                        // OAuth 2.1 s4.3.1 reuse detection: a rotated/revoked token was replayed —
                        // revoke every descendant in the family.
                        await RevokeFamilyAsync(tokenRecord.familyId);
                        return Error(onError, "invalid_grant",
                            "The refresh token has been revoked (reuse detected).");
                    }
                    if (tokenRecord.expiresOn < DateTime.UtcNow)
                        return Error(onError, "invalid_grant", "The refresh token has expired.");

                    return await IssueTokensAsync(
                        client, tokenRecord.accountId, tokenRecord.authorization,
                        tokenRecord.scope, tokenRecord.resource,
                        familyId: tokenRecord.familyId,
                        sessionId: tokenRecord.sessionId,
                        request, application,
                        onIssued, onError,
                        rotateFrom: tokenRef.Optional());
                },
                () => Error(onError, "invalid_grant", "Invalid refresh token.").AsTask());
        }

        internal static async Task<int> RevokeFamilyAsync(Guid familyId)
        {
            var revokedCount = 0;
            var family = await typeof(OAuthRefreshTokenRecord)
                .StorageGetAll()
                .CastObjsAs<OAuthRefreshTokenRecord>()
                .Where(record => record.familyId == familyId)
                .ToArrayAsync();
            foreach (var record in family)
            {
                var revokedNow = await record.@ref.StorageUpdateAsync2(
                    toUpdate =>
                    {
                        toUpdate.revoked = true;
                        return toUpdate;
                    },
                    updated => true,
                    () => false);
                if (revokedNow)
                    revokedCount++;
            }
            return revokedCount;
        }

        #endregion

        #region grant_type=client_credentials

        /// <summary>
        /// RFC 6749 §4.4 — service-to-service tokens for admin-provisioned confidential
        /// clients. No user account is involved: the token carries only scp + client_id
        /// claims, so it passes [RequiredScope] gates but NOT account/role-based gates.
        /// Per OAuth 2.1 no refresh token is issued (the client can just re-authenticate).
        /// </summary>
        private static async Task<IHttpResponse> ClientCredentialsGrantAsync(
            Microsoft.AspNetCore.Http.IFormCollection form, ClientCredential client, string clientSecret,
            ContentTypeResponse<TokenSuccessResponse> onIssued,
            BadRequestBodyResponse<OAuthTokenError> onError)
        {
            if (client.clientType != ClientCredential.ClientTypes.Confidential
                    || clientSecret.IsNullOrWhiteSpace())
                return Error(onError, "unauthorized_client",
                    "client_credentials is only available to confidential clients authenticating with a secret.");

            var registeredGrants = (client.grantTypes ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim())
                .ToArray();
            if (!registeredGrants.Contains(ClientCredential.GrantTypeValues.ClientCredentials))
                return Error(onError, "unauthorized_client",
                    "This client is not registered for the client_credentials grant.");

            // RFC 6749 §3.3: requested scope must stay within the client's registered scope;
            // no request → the registered scope.
            var registeredScopes = (client.scope ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var requestedScope = (string)form["scope"];
            var grantedScope = client.scope;
            if (requestedScope.HasBlackSpace())
            {
                var requestedScopes = requestedScope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var outOfRegistration = requestedScopes
                    .Where(s => !registeredScopes.Contains(s, StringComparer.Ordinal))
                    .ToArray();
                if (outOfRegistration.Any())
                    return Error(onError, "invalid_scope",
                        $"Scope(s) not registered for this client: {outOfRegistration.Join(" ")}");
                grantedScope = requestedScope;
            }

            var oauthClaims = new Dictionary<string, string>
            {
                [OAuthServer.ClientIdClaimType] = client.clientId,
            };
            if (grantedScope.HasBlackSpace())
                oauthClaims[OAuthServer.ScopeClaimType] = grantedScope;

            var duration = OAuthServer.AccessTokenDuration();
            return await EastFive.Security.AppSettings.TokenScope.ConfigurationUri(
                tokenScope =>
                {
                    return EastFive.Api.Auth.JwtTools.CreateToken(
                            Guid.NewGuid(), tokenScope, duration, oauthClaims,
                        (accessToken, whenIssued) =>
                        {
                            var response = new TokenSuccessResponse
                            {
                                AccessToken = accessToken,
                                TokenType = "Bearer",
                                ExpiresIn = (long)duration.TotalSeconds,
                                Scope = grantedScope,
                            };
                            return NoStore(onIssued(response, "application/json")).AsTask();
                        },
                        missingConfig => Error(onError, "server_error",
                            "Token signing is not configured.").AsTask(),
                        (configName, issue) => Error(onError, "server_error",
                            "Token signing configuration is invalid.").AsTask());
                },
                why => Error(onError, "server_error",
                    "Token scope is not configured.").AsTask());
        }

        #endregion

        #region Issuance

        private static async Task<IHttpResponse> IssueTokensAsync(
            ClientCredential client, Guid accountId,
            IRefOptional<EastFive.Azure.Auth.Authorization> loginAuthorization,
            string scope, string resource, Guid familyId, Guid sessionId,
            IHttpRequest request, IApplication application,
            ContentTypeResponse<TokenSuccessResponse> onIssued,
            BadRequestBodyResponse<OAuthTokenError> onError,
            IRefOptional<OAuthRefreshTokenRecord> rotateFrom = default)
        {
            // Claims resolve from the login authorization the same way session tokens do
            // (account claim + stored role claims), so role gates work identically.
            return await await Session.GetClaimsAsync(application, loginAuthorization,
                async (claims, accountIdMaybe, authorized) =>
                {
                    if (!accountIdMaybe.HasValue || accountIdMaybe.Value != accountId)
                        return Error(onError, "invalid_grant",
                            "The underlying sign-in is no longer valid.");

                    var oauthClaims = claims
                        .NullToEmpty()
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    if (scope.HasBlackSpace())
                        oauthClaims[OAuthServer.ScopeClaimType] = scope;
                    oauthClaims[OAuthServer.ClientIdClaimType] = client.clientId;

                    var duration = OAuthServer.AccessTokenDuration();

                    // Anchor the grant with a persisted Session row (created once per grant,
                    // reused across refresh rotations) so session-aware endpoints work.
                    if (sessionId.IsDefault())
                    {
                        sessionId = Guid.NewGuid();
                        var session = new Session
                        {
                            sessionId = sessionId.AsRef<Session>(),
                            created = DateTime.UtcNow,
                            authorization = loginAuthorization,
                            account = accountId,
                            authorized = true,
                            expires = DateTime.UtcNow + OAuthServer.RefreshTokenDuration(),
                        };
                        var sessionStored = await session.StorageCreateAsync(
                            created => true,
                            onAlreadyExists: () => false);
                        if (!sessionStored)
                            return Error(onError, "server_error", "Please retry the request.");
                    }

                    return await EastFive.Security.AppSettings.TokenScope.ConfigurationUri(
                        async tokenScope =>
                        {
                            return await EastFive.Api.Auth.JwtTools.CreateToken(
                                    sessionId, tokenScope, duration, oauthClaims,
                                async (accessToken, whenIssued) =>
                                {
                                    // rotate: issue new refresh token, then revoke the old one
                                    var newRefreshToken = OAuthServer.GenerateSecret();
                                    var refreshRecordRef = OAuthServer
                                        .ComputeLookupGuid(newRefreshToken)
                                        .AsRef<OAuthRefreshTokenRecord>();
                                    var refreshRecord = new OAuthRefreshTokenRecord
                                    {
                                        @ref = refreshRecordRef,
                                        tokenHash = OAuthServer.ComputeSecretHash(newRefreshToken),
                                        clientId = client.clientId,
                                        scope = scope,
                                        resource = resource,
                                        authorization = loginAuthorization,
                                        accountId = accountId,
                                        familyId = familyId,
                                        sessionId = sessionId,
                                        expiresOn = DateTime.UtcNow + OAuthServer.RefreshTokenDuration(),
                                        revoked = false,
                                    };
                                    var stored = await refreshRecord.StorageCreateAsync(
                                        created => true,
                                        onAlreadyExists: () => false);
                                    if (!stored)
                                        return Error(onError, "server_error", "Please retry the request.");

                                    if (rotateFrom.HasValueNotNull())
                                        await rotateFrom.Ref.StorageUpdateAsync2(
                                            toUpdate =>
                                            {
                                                toUpdate.revoked = true;
                                                toUpdate.replacedBy = refreshRecordRef.Optional();
                                                return toUpdate;
                                            },
                                            updated => true,
                                            () => false);

                                    var response = new TokenSuccessResponse
                                    {
                                        AccessToken = accessToken,
                                        TokenType = "Bearer",
                                        ExpiresIn = (long)duration.TotalSeconds,
                                        RefreshToken = newRefreshToken,
                                        Scope = scope,
                                    };
                                    return NoStore(onIssued(response, "application/json"));
                                },
                                missingConfig => Error(onError, "server_error",
                                    "Token signing is not configured.").AsTask(),
                                (configName, issue) => Error(onError, "server_error",
                                    "Token signing configuration is invalid.").AsTask());
                        },
                        why => Error(onError, "server_error",
                            "Token scope is not configured.").AsTask());
                },
                why => Error(onError, "invalid_grant",
                    "The underlying sign-in is no longer valid.").AsTask());
        }

        #endregion

        #region Client authentication

        /// <summary>
        /// Reads client_id/client_secret from HTTP Basic auth (RFC 6749 §2.3.1 preferred)
        /// or from the form body (client_secret_post / public clients).
        /// </summary>
        private static (string clientId, string clientSecret) ReadClientAuthentication(IHttpRequest request)
        {
            var authorizationHeader = request.GetHeader("Authorization");
            if (authorizationHeader.HasBlackSpace()
                && authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(
                        Convert.FromBase64String(authorizationHeader.Substring("Basic ".Length).Trim()));
                    var separator = decoded.IndexOf(':');
                    if (separator > 0)
                        return (
                            Uri.UnescapeDataString(decoded.Substring(0, separator)),
                            Uri.UnescapeDataString(decoded.Substring(separator + 1)));
                }
                catch (FormatException) { }
            }

            var form = request.Form;
            return (
                (string)form["client_id"],
                (string)form["client_secret"]);
        }

        private static bool AuthenticateClient(ClientCredential client, string providedSecret)
        {
            if (client.tokenEndpointAuthMethod == ClientCredential.TokenEndpointAuthMethods.None)
                return true; // public client: PKCE is the proof of possession
            if (providedSecret.IsNullOrWhiteSpace() || client.clientSecret.IsNullOrWhiteSpace())
                return false;
            // hashed comparison when the stored value is a hash; legacy plaintext fallback
            if (OAuthServer.SecretMatchesHash(providedSecret, client.clientSecret))
                return true;
            return String.Equals(client.clientSecret, providedSecret, StringComparison.Ordinal);
        }

        #endregion

        private static IHttpResponse Error(BadRequestBodyResponse<OAuthTokenError> onError,
            string error, string description)
        {
            var response = onError(
                new OAuthTokenError { Error = error, ErrorDescription = description },
                "application/json");
            return NoStore(response);
        }

        private static IHttpResponse NoStore(IHttpResponse response)
        {
            response.Headers["Cache-Control"] = new[] { "no-store" };
            response.Headers["Pragma"] = new[] { "no-cache" };
            return response;
        }
    }

    public class TokenSuccessResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("expires_in")]
        public long ExpiresIn { get; set; }

        [JsonProperty("refresh_token", NullValueHandling = NullValueHandling.Ignore)]
        public string RefreshToken { get; set; }

        [JsonProperty("scope", NullValueHandling = NullValueHandling.Ignore)]
        public string Scope { get; set; }
    }

    public class OAuthTokenError
    {
        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("error_description")]
        public string ErrorDescription { get; set; }
    }
}

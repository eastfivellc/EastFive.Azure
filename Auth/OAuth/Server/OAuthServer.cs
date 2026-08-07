using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using EastFive.Extensions;
using EastFive.Web.Configuration;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// Shared helpers + configuration for the OAuth 2.1 authorization server
    /// (the endpoints under /oauth/* and /.well-known/*).
    /// </summary>
    public static class OAuthServer
    {
        public static class AppSettings
        {
            /// <summary>
            /// Lifetime of OAuth access tokens (issued from /oauth/token) in minutes.
            /// Deliberately separate from the session-token lifetime settings.
            /// </summary>
            [EastFive.Web.ConfigKey(
                "Lifetime of OAuth access tokens in minutes (default 60).",
                EastFive.Web.DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = false,
                Location = "Application configuration")]
            public const string AccessTokenExpirationInMinutes =
                "EastFive.Azure.OAuth.AccessTokenExpirationInMinutes";

            /// <summary>
            /// Lifetime of OAuth refresh tokens in days (default 30).
            /// </summary>
            [EastFive.Web.ConfigKey(
                "Lifetime of OAuth refresh tokens in days (default 30).",
                EastFive.Web.DeploymentOverrides.Suggested,
                DeploymentSecurityConcern = false,
                Location = "Application configuration")]
            public const string RefreshTokenExpirationInDays =
                "EastFive.Azure.OAuth.RefreshTokenExpirationInDays";

            /// <summary>
            /// Space-separated scopes advertised in the RFC 8414 / RFC 9728 metadata
            /// documents (default "mcp"). Advisory — tokens carry whatever scope the
            /// authorization granted; endpoints enforce via [RequiredScope].
            /// </summary>
            [EastFive.Web.ConfigKey(
                "Space-separated OAuth scopes advertised in discovery metadata (default `mcp`).",
                EastFive.Web.DeploymentOverrides.Optional,
                DeploymentSecurityConcern = false,
                Location = "Application configuration")]
            public const string ScopesSupported =
                "EastFive.Azure.OAuth.ScopesSupported";
        }

        /// <summary>Claim type carrying the OAuth scope(s) granted to the token (space delimited).</summary>
        public const string ScopeClaimType = "scp";

        /// <summary>
        /// JwtSecurityTokenHandler's default inbound claim-type map rewrites "scp" to this
        /// URI when validating tokens — read-side checks must accept both forms.
        /// </summary>
        public const string ScopeClaimTypeMapped = "http://schemas.microsoft.com/identity/claims/scope";

        /// <summary>Claim type carrying the OAuth client through which the token was issued.</summary>
        public const string ClientIdClaimType = "client_id";

        public static TimeSpan AccessTokenDuration() =>
            AppSettings.AccessTokenExpirationInMinutes.ConfigurationDouble(
                minutes => TimeSpan.FromMinutes(minutes),
                onNotSpecified: () => TimeSpan.FromMinutes(60));

        public static TimeSpan RefreshTokenDuration() =>
            AppSettings.RefreshTokenExpirationInDays.ConfigurationDouble(
                days => TimeSpan.FromDays(days),
                onNotSpecified: () => TimeSpan.FromDays(30));

        /// <summary>The default scope MCP endpoints are gated on.</summary>
        public const string McpScope = "mcp";

        public static string[] ScopesSupported() =>
            AppSettings.ScopesSupported.ConfigurationString(
                scopes => scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                (why) => new[] { McpScope });

        /// <summary>The RFC 9728 protected-resource-metadata URL for this host —
        /// advertised in WWW-Authenticate challenges so clients can begin discovery.</summary>
        public static string ProtectedResourceMetadataUrl(Uri requestUri) =>
            $"{Origin(requestUri)}/.well-known/oauth-protected-resource";

        /// <summary>
        /// The issuer identifier (RFC 8414 §2). Clients validate that this equals the
        /// origin they fetched the metadata document from, so it MUST be the request
        /// origin — NOT EastFive.Security.Token.Issuer (the JWT `iss` claim is internal
        /// to our own validation and opaque to OAuth clients).
        /// </summary>
        public static string Issuer(Uri requestUri) =>
            Origin(requestUri);

        public static string Origin(Uri requestUri) =>
            $"{requestUri.Scheme}://{requestUri.Authority}";

        #region Secrets: generation, hashing, lookup keys

        /// <summary>Generate a 256-bit URL-safe secret (authorization codes, refresh tokens).</summary>
        public static string GenerateSecret()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        /// <summary>
        /// Deterministic storage row key for a secret: first 16 bytes of SHA256(secret).
        /// The secret itself is never stored.
        /// </summary>
        public static Guid ComputeLookupGuid(string secret)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            return new Guid(hash.AsSpan(0, 16));
        }

        /// <summary>Full SHA256 of a secret, base64url — stored for verification.</summary>
        public static string ComputeSecretHash(string secret) =>
            Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

        public static bool SecretMatchesHash(string secret, string storedHash) =>
            storedHash.HasBlackSpace() &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(ComputeSecretHash(secret)),
                Encoding.UTF8.GetBytes(storedHash));

        #endregion

        #region PKCE (RFC 7636), S256 only per OAuth 2.1

        public const string CodeChallengeMethodS256 = "S256";

        public static bool VerifyCodeChallenge(string codeVerifier, string codeChallenge)
        {
            if (codeVerifier.IsNullOrWhiteSpace() || codeChallenge.IsNullOrWhiteSpace())
                return false;
            // RFC 7636 §4.1: verifier is 43..128 unreserved characters
            if (codeVerifier.Length < 43 || codeVerifier.Length > 128)
                return false;
            var computed = Base64UrlEncode(
                SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(computed),
                Encoding.ASCII.GetBytes(codeChallenge));
        }

        #endregion

        public static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        #region Redirect URI validation (OAuth 2.1: exact match; https or loopback http)

        public static bool IsAllowableRedirectUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri)
                return false;
            if (uri.Fragment.HasBlackSpace())
                return false;
            if (uri.Scheme == Uri.UriSchemeHttps)
                return true;
            if (uri.Scheme == Uri.UriSchemeHttp)
                return uri.IsLoopback;
            // Private-use schemes for native apps (RFC 8252 §7.1) contain a '.' — plus the
            // fixed (dotless) callback schemes of the VS Code family, which extensions
            // cannot choose (vscode.env.uriScheme).
            if (uri.Scheme.Contains('.'))
                return true;
            return uri.Scheme == "vscode"
                || uri.Scheme == "vscode-insiders"
                || uri.Scheme == "vscodium"
                || uri.Scheme == "cursor";
        }

        /// <summary>Exact string match against the client's registered redirect uris (comma separated).</summary>
        public static bool RedirectUriIsRegistered(string redirectUri, string registeredUris) =>
            registeredUris.HasBlackSpace() &&
            registeredUris
                .Split(',')
                .Select(u => u.Trim())
                .Any(registered => String.Equals(registered, redirectUri, StringComparison.Ordinal));

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EastFive;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.OAuth
{
    public partial struct ClientCredential
    {
        #region RFC 6749 Section 2 - Client Registration Validation

        /// <summary>
        /// Valid OAuth 2.0 client types per RFC 6749 Section 2.1
        /// </summary>
        public static class ClientTypes
        {
            public const string Confidential = "confidential";
            public const string Public = "public";

            public static bool IsValid(string clientType) =>
                clientType == Confidential || clientType == Public;
        }

        /// <summary>
        /// Valid OAuth 2.0 grant types per RFC 6749 Section 4
        /// </summary>
        public static class GrantTypeValues
        {
            public const string AuthorizationCode = "authorization_code";
            public const string Implicit = "implicit";
            public const string Password = "password";
            public const string ClientCredentials = "client_credentials";
            public const string RefreshToken = "refresh_token";

            public static readonly string[] All = new[]
            {
                AuthorizationCode, Implicit, Password, ClientCredentials, RefreshToken
            };

            public static bool IsValid(string grantType) => All.Contains(grantType);
        }

        /// <summary>
        /// Valid token endpoint authentication methods per RFC 6749 Section 2.3
        /// </summary>
        public static class TokenEndpointAuthMethods
        {
            public const string ClientSecretBasic = "client_secret_basic";
            public const string ClientSecretPost = "client_secret_post";
            public const string None = "none";

            public static readonly string[] All = new[]
            {
                ClientSecretBasic, ClientSecretPost, None
            };

            public static bool IsValid(string method) => All.Contains(method);
        }

        /// <summary>
        /// Validates client registration per RFC 6749 Section 2
        /// </summary>
        public TResult ValidateRegistration<TResult>(
            Func<TResult> onValid,
            Func<string, TResult> onInvalid)
        {
            // Client type is REQUIRED (RFC 6749 Section 2.1)
            if (string.IsNullOrWhiteSpace(this.clientType))
                return onInvalid("client_type is required");

            if (!ClientTypes.IsValid(this.clientType))
                return onInvalid($"client_type must be '{ClientTypes.Confidential}' or '{ClientTypes.Public}'");

            // Client secret validation based on client type (RFC 6749 Section 2.3)
            if (this.clientType == ClientTypes.Confidential)
            {
                if (string.IsNullOrWhiteSpace(this.clientSecret))
                    return onInvalid("client_secret is required for confidential clients");

                if (string.IsNullOrWhiteSpace(this.tokenEndpointAuthMethod))
                    return onInvalid("token_endpoint_auth_method is required for confidential clients");

                if (!TokenEndpointAuthMethods.IsValid(this.tokenEndpointAuthMethod))
                    return onInvalid($"Invalid token_endpoint_auth_method");
            }
            else if (this.clientType == ClientTypes.Public)
            {
                // Public clients MUST NOT have a client secret (RFC 6749 Section 2.3)
                if (!string.IsNullOrWhiteSpace(this.clientSecret))
                    return onInvalid("Public clients MUST NOT have a client_secret");

                // Public clients use 'none' authentication
                if (!string.IsNullOrWhiteSpace(this.tokenEndpointAuthMethod) &&
                    this.tokenEndpointAuthMethod != TokenEndpointAuthMethods.None)
                    return onInvalid("Public clients must use 'none' authentication method");
            }

            // Redirection URI validation (RFC 6749 Section 3.1.2)
            if (this.clientType == ClientTypes.Public ||
                (!string.IsNullOrWhiteSpace(this.grantTypes) && 
                 (this.grantTypes.Contains(GrantTypeValues.Implicit) ||
                  this.grantTypes.Contains(GrantTypeValues.AuthorizationCode))))
            {
                if (string.IsNullOrWhiteSpace(this.redirectUris))
                    return onInvalid("redirect_uris is required for public clients and authorization/implicit grant types");

                // Validate URIs are absolute
                var uris = this.redirectUris.Split(',');
                foreach (var uriStr in uris)
                {
                    if (!Uri.TryCreate(uriStr.Trim(), UriKind.Absolute, out var uri))
                        return onInvalid($"redirect_uri '{uriStr}' must be an absolute URI");

                    // Fragment component MUST NOT be included (RFC 6749 Section 3.1.2)
                    if (!string.IsNullOrEmpty(uri.Fragment))
                        return onInvalid($"redirect_uri '{uriStr}' MUST NOT include a fragment component");
                }
            }

            // Grant types validation
            if (!string.IsNullOrWhiteSpace(this.grantTypes))
            {
                var grantTypes = this.grantTypes.Split(',');
                foreach (var gt in grantTypes)
                {
                    if (!GrantTypeValues.IsValid(gt.Trim()))
                        return onInvalid($"Invalid grant_type: {gt}");
                }
            }

            return onValid();
        }

        /// <summary>
        /// Checks if client is authorized for a specific grant type
        /// </summary>
        public bool IsAuthorizedForGrantType(string grantType)
        {
            if (string.IsNullOrWhiteSpace(this.grantTypes))
                return false;

            var authorizedGrants = this.grantTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim());

            return authorizedGrants.Contains(grantType);
        }

        #endregion

        #region Secret Management

        /// <summary>
        /// Validates if the provided client secret matches the stored secret
        /// In production, this should use secure hashing (e.g., BCrypt, PBKDF2)
        /// </summary>
        public bool ValidateSecret(string providedSecret)
        {
            if (string.IsNullOrWhiteSpace(providedSecret))
                return false;

            // TODO: In production, use secure comparison to prevent timing attacks
            // and hash comparison instead of plain text
            return this.clientSecret == providedSecret;
        }

        #endregion

        #region Cryptographic Helpers

        /// <summary>
        /// Generates a cryptographically secure client secret
        /// </summary>
        public static string GenerateClientSecret()
        {
            // Generate 32 bytes (256 bits) of random data
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            // Convert to base64 string (safe for URLs and storage)
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        /// <summary>
        /// Generates a cryptographically secure client ID
        /// </summary>
        public static string GenerateClientId()
        {
            // Generate 16 bytes (128 bits) of random data
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            // Convert to hex string
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        #endregion

        #region Scope Validation

        /// <summary>
        /// Validates if the client has access to the requested scope
        /// Implements RFC 6749 Section 3.3: Access Token Scope
        /// </summary>
        public bool HasScope(string requestedScope)
        {
            if (string.IsNullOrWhiteSpace(requestedScope))
                return true; // No scope requested

            if (string.IsNullOrWhiteSpace(this.scope))
                return false; // Client has no scopes

            var clientScopes = this.scope.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var requestedScopes = requestedScope.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // All requested scopes must be present in client scopes
            return requestedScopes.All(rs => clientScopes.Contains(rs));
        }

        #endregion

        #region Authentication Validation

        /// <summary>
        /// Checks if the client credential is valid for authentication
        /// </summary>
        public TResult ValidateForAuthentication<TResult>(
            Func<TResult> onValid,
            Func<string, TResult> onInvalid)
        {
            if (!this.isActive)
                return onInvalid("Client is not active");

            if (string.IsNullOrWhiteSpace(this.clientId))
                return onInvalid("Client ID is missing");

            // Only confidential clients require secrets
            if (this.clientType == ClientTypes.Confidential && 
                string.IsNullOrWhiteSpace(this.clientSecret))
                return onInvalid("Client secret is missing for confidential client");

            return onValid();
        }

        #endregion

        #region Hashing Utilities

        /// <summary>
        /// Hash a client secret for secure storage
        /// Uses PBKDF2 with SHA256
        /// </summary>
        public static string HashSecret(string secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
                throw new ArgumentException("Secret cannot be empty", nameof(secret));

            // Generate salt
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Hash the secret
            using (var pbkdf2 = new Rfc2898DeriveBytes(secret, salt, 10000, HashAlgorithmName.SHA256))
            {
                var hash = pbkdf2.GetBytes(32);

                // Combine salt and hash
                var combined = new byte[salt.Length + hash.Length];
                Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
                Buffer.BlockCopy(hash, 0, combined, salt.Length, hash.Length);

                return Convert.ToBase64String(combined);
            }
        }

        /// <summary>
        /// Verify a client secret against a stored hash
        /// </summary>
        public static bool VerifyHashedSecret(string providedSecret, string hashedSecret)
        {
            if (string.IsNullOrWhiteSpace(providedSecret) || string.IsNullOrWhiteSpace(hashedSecret))
                return false;

            try
            {
                // Decode the combined salt+hash
                var combined = Convert.FromBase64String(hashedSecret);

                // Extract salt and hash
                var salt = new byte[16];
                var storedHash = new byte[32];
                Buffer.BlockCopy(combined, 0, salt, 0, 16);
                Buffer.BlockCopy(combined, 16, storedHash, 0, 32);

                // Hash the provided secret with the same salt
                using (var pbkdf2 = new Rfc2898DeriveBytes(providedSecret, salt, 10000, HashAlgorithmName.SHA256))
                {
                    var providedHash = pbkdf2.GetBytes(32);

                    // Compare hashes (constant time comparison)
                    return CryptographicOperations.FixedTimeEquals(providedHash, storedHash);
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Update Helpers

        /// <summary>
        /// Updates the last used timestamp for this client
        /// </summary>
        public async Task<TResult> UpdateLastUsedAsync<TResult>(
            Func<ClientCredential, TResult> onUpdated,
            Func<TResult> onNotFound)
        {
            return await this.@ref.StorageUpdateAsync2(
                client =>
                {
                    client.lastUsedAt = DateTime.UtcNow;
                    client.updatedAt = DateTime.UtcNow;
                    return client;
                },
                onUpdated,
                onNotFound);
        }

        #endregion
    }
}

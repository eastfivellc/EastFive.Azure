using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using EastFive;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;

namespace EastFive.Azure.OAuth
{
    public partial struct ClientCredentialFlow
    {
        #region RFC 6749 Section 3.1 - Authorization Endpoint Validation

        /// <summary>
        /// Supported response type values per RFC 6749 §3.1.1 and §8.4
        /// </summary>
        public static class ResponseTypeValues
        {
            /// <summary>
            /// Authorization Code grant type (RFC 6749 §4.1)
            /// </summary>
            public const string Code = "code";

            /// <summary>
            /// Implicit grant type (RFC 6749 §4.2)
            /// </summary>
            public const string Token = "token";

            /// <summary>
            /// All supported response types
            /// </summary>
            public static readonly string[] All = new[] { Code, Token };

            /// <summary>
            /// Validates if response type is supported per RFC 6749
            /// </summary>
            public static bool IsValid(string responseType)
            {
                if (string.IsNullOrWhiteSpace(responseType))
                    return false;

                // RFC 6749 §3.1.1: Response type MAY be space-delimited list
                var types = responseType.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return types.All(t => All.Contains(t, StringComparer.Ordinal));
            }
        }

        /// <summary>
        /// Standard error codes for authorization endpoint per RFC 6749 §4.1.2.1 and §4.2.2.1
        /// </summary>
        public static class AuthorizationErrorCodes
        {
            /// <summary>
            /// The request is missing a required parameter, includes an invalid parameter value,
            /// includes a parameter more than once, or is otherwise malformed.
            /// </summary>
            public const string InvalidRequest = "invalid_request";

            /// <summary>
            /// The client is not authorized to request an authorization code using this method.
            /// </summary>
            public const string UnauthorizedClient = "unauthorized_client";

            /// <summary>
            /// The resource owner or authorization server denied the request.
            /// </summary>
            public const string AccessDenied = "access_denied";

            /// <summary>
            /// The authorization server does not support obtaining an authorization code using this method.
            /// </summary>
            public const string UnsupportedResponseType = "unsupported_response_type";

            /// <summary>
            /// The requested scope is invalid, unknown, or malformed.
            /// </summary>
            public const string InvalidScope = "invalid_scope";

            /// <summary>
            /// The authorization server encountered an unexpected condition.
            /// </summary>
            public const string ServerError = "server_error";

            /// <summary>
            /// The authorization server is currently unable to handle the request.
            /// </summary>
            public const string TemporarilyUnavailable = "temporarily_unavailable";
        }

        /// <summary>
        /// Validates an authorization request according to RFC 6749 Section 3.1
        /// </summary>
        /// <returns>Returns onValid callback if valid, otherwise onInvalid with error reason</returns>
        public TResult ValidateAuthorizationRequest<TResult>(
            AuthorizationRequest request,
            Func<TResult> onValid,
            Func<string, string, TResult> onInvalid)
        {
            // RFC 6749 §3.1.1: response_type is REQUIRED
            if (string.IsNullOrWhiteSpace(request?.ResponseType))
                return onInvalid(AuthorizationErrorCodes.InvalidRequest, 
                    "Missing required parameter: response_type");

            // Validate response_type is supported
            if (!ResponseTypeValues.IsValid(request.ResponseType))
                return onInvalid(AuthorizationErrorCodes.UnsupportedResponseType,
                    $"Unsupported response_type: {request.ResponseType}. Supported values: {string.Join(", ", ResponseTypeValues.All)}");

            // Validate response_type matches configured types
            if (!string.IsNullOrWhiteSpace(this.responseTypes))
            {
                var configuredTypes = this.responseTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .ToArray();

                var requestedTypes = request.ResponseType.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (!requestedTypes.All(rt => configuredTypes.Contains(rt, StringComparer.OrdinalIgnoreCase)))
                    return onInvalid(AuthorizationErrorCodes.UnauthorizedClient,
                        $"Client not authorized for response_type: {request.ResponseType}");
            }

            // RFC 6749 §4.1.1 and §4.2.1: client_id is REQUIRED
            if (string.IsNullOrWhiteSpace(request.ClientId))
                return onInvalid(AuthorizationErrorCodes.InvalidRequest,
                    "Missing required parameter: client_id");

            // Validate client_id matches configuration
            if (!string.Equals(request.ClientId, this.clientId, StringComparison.Ordinal))
                return onInvalid(AuthorizationErrorCodes.UnauthorizedClient,
                    "Invalid client_id");

            // RFC 6749 §3.1.2: Validate redirect_uri if provided
            if (!string.IsNullOrWhiteSpace(request.RedirectUri))
            {
                if (!Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var redirectUri))
                    return onInvalid(AuthorizationErrorCodes.InvalidRequest,
                        "redirect_uri must be an absolute URI");

                // RFC 6749 §3.1.2: Endpoint URI MUST NOT include a fragment
                if (!string.IsNullOrEmpty(redirectUri.Fragment))
                    return onInvalid(AuthorizationErrorCodes.InvalidRequest,
                        "redirect_uri MUST NOT include a fragment component");

                // Validate against registered redirect URI if configured
                if (this.redirectUri != null &&
                    !string.Equals(redirectUri.ToString(), this.redirectUri.ToString(), StringComparison.OrdinalIgnoreCase))
                    return onInvalid(AuthorizationErrorCodes.InvalidRequest,
                        "redirect_uri does not match registered value");
            }
            else
            {
                // RFC 6749 §3.1.2.2: Public clients MUST register redirect URI
                // If no redirect_uri in request, must have registered one
                if (this.redirectUri == null)
                    return onInvalid(AuthorizationErrorCodes.InvalidRequest,
                        "Missing required parameter: redirect_uri (not registered)");
            }

            // RFC 6749 §3.3: Validate scope if provided
            if (!string.IsNullOrWhiteSpace(request.Scope))
            {
                // Scope validation logic can be extended based on requirements
                // For now, just ensure it's well-formed (space-delimited)
                var scopeValues = request.Scope.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (scopeValues.Length == 0)
                    return onInvalid(AuthorizationErrorCodes.InvalidScope,
                        "scope parameter is malformed");
            }

            return onValid();
        }

        /// <summary>
        /// Builds an authorization request URL per RFC 6749 §4.1.1 or §4.2.1
        /// </summary>
        public string BuildAuthorizationUrl(
            string responseType,
            string state = null,
            string scope = null,
            string redirectUri = null)
        {
            if (this.authorizationEndpoint == null)
                throw new InvalidOperationException("Authorization endpoint not configured");

            var queryParams = new Dictionary<string, string>
            {
                ["response_type"] = responseType,
                ["client_id"] = this.clientId
            };

            // Add optional parameters
            if (!string.IsNullOrWhiteSpace(redirectUri))
                queryParams["redirect_uri"] = redirectUri;
            else if (this.redirectUri != null)
                queryParams["redirect_uri"] = this.redirectUri.ToString();

            if (!string.IsNullOrWhiteSpace(scope))
                queryParams["scope"] = scope;
            else if (!string.IsNullOrWhiteSpace(this.scope))
                queryParams["scope"] = this.scope;

            if (!string.IsNullOrWhiteSpace(state))
                queryParams["state"] = state;

            // Build query string
            var queryString = string.Join("&",
                queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var uriBuilder = new UriBuilder(this.authorizationEndpoint)
            {
                Query = queryString
            };

            return uriBuilder.ToString();
        }

        /// <summary>
        /// Parses authorization response from redirect URI per RFC 6749 §4.1.2 or §4.2.2
        /// </summary>
        public AuthorizationResponse ParseAuthorizationResponse(Uri responseUri, out string error, out string errorDescription)
        {
            error = null;
            errorDescription = null;

            if (responseUri == null)
            {
                error = AuthorizationErrorCodes.InvalidRequest;
                errorDescription = "Response URI is null";
                return null;
            }

            // RFC 6749 §4.1.2: Authorization code in query parameters
            // RFC 6749 §4.2.2: Access token in fragment (implicit grant)
            var queryParams = HttpUtility.ParseQueryString(responseUri.Query);
            var fragmentParams = HttpUtility.ParseQueryString(responseUri.Fragment.TrimStart('#'));

            // Check for error response first
            var errorCode = queryParams["error"] ?? fragmentParams["error"];
            if (!string.IsNullOrEmpty(errorCode))
            {
                error = errorCode;
                errorDescription = queryParams["error_description"] ?? fragmentParams["error_description"];
                return null;
            }

            var response = new AuthorizationResponse();

            // Parse authorization code (query) or access token (fragment)
            response.Code = queryParams["code"];
            response.AccessToken = fragmentParams["access_token"];
            response.TokenType = fragmentParams["token_type"];
            
            if (int.TryParse(fragmentParams["expires_in"], out var expiresIn))
                response.ExpiresIn = expiresIn;

            response.Scope = queryParams["scope"] ?? fragmentParams["scope"];
            response.State = queryParams["state"] ?? fragmentParams["state"];

            return response;
        }

        #endregion

        #region Token Exchange (RFC 6749 Section 4.1.3)

        /// <summary>
        /// Exchanges an authorization code for an access token per RFC 6749 §4.1.3
        /// </summary>
        public async Task<TResult> ExchangeAuthorizationCodeAsync<TResult>(
            string authorizationCode,
            string redirectUri = null,
            HttpClient httpClient = null,
            Func<AccessTokenResponse, TResult> onSuccess = null,
            Func<TokenErrorResponse, TResult> onError = null,
            Func<string, TResult> onFailure = null)
        {
            if (this.tokenEndpoint == null)
                return onFailure("Token endpoint not configured");

            if (string.IsNullOrWhiteSpace(authorizationCode))
                return onFailure("Authorization code is required");

            var client = httpClient ?? new HttpClient();
            var disposeClient = httpClient is null;

            try
            {
                // RFC 6749 §4.1.3: Build token request
                var requestParams = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = authorizationCode,
                    ["client_id"] = this.clientId
                };

                // Add redirect_uri if provided or configured
                var effectiveRedirectUri = redirectUri ?? this.redirectUri?.ToString();
                if (!string.IsNullOrWhiteSpace(effectiveRedirectUri))
                    requestParams["redirect_uri"] = effectiveRedirectUri;

                // Add client_secret for confidential clients (RFC 6749 §2.3)
                if (!string.IsNullOrWhiteSpace(this.clientSecret))
                    requestParams["client_secret"] = this.clientSecret;

                var requestContent = new FormUrlEncodedContent(requestParams);

                // RFC 6749 §3.2: POST to token endpoint
                var response = await client.PostAsync(this.tokenEndpoint, requestContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // RFC 6749 §5.1: Successful response
                    var tokenResponse = JsonConvert.DeserializeObject<AccessTokenResponse>(responseBody);
                    return onSuccess(tokenResponse);
                }

                // RFC 6749 §5.2: Error response
                var errorResponse = JsonConvert.DeserializeObject<TokenErrorResponse>(responseBody);
                return onError(errorResponse);
            }
            catch (Exception ex)
            {
                return onFailure($"Token exchange failed: {ex.Message}");
            }
            finally
            {
                if (disposeClient)
                    client.Dispose();
            }
        }

        #endregion

        #region RFC 6749 Section 4.3 - Resource Owner Password Credentials Grant

        /// <summary>
        /// Exchanges resource owner credentials for an access token per RFC 6749 §4.3.
        /// SHOULD only be used when there is a high degree of trust between the resource owner and the client.
        /// </summary>
        /// <remarks>
        /// RFC 6749 §4.3: This grant type is suitable for clients capable of obtaining the resource
        /// owner's credentials. It should only be used when other flows are not viable.
        /// 
        /// Security Considerations (RFC 6749 §10.7):
        /// - This grant type carries higher risk than other grant types
        /// - Client could abuse the password
        /// - Authorization server MUST protect endpoint against brute force attacks
        /// - Client SHOULD minimize use of this grant type
        /// </remarks>
        public async Task<TResult> ExchangePasswordCredentialsAsync<TResult>(
            string username,
            string password,
            string scope = null,
            HttpClient httpClient = null,
            Func<AccessTokenResponse, TResult> onSuccess = null,
            Func<TokenErrorResponse, TResult> onError = null,
            Func<string, TResult> onFailure = null)
        {
            if (this.tokenEndpoint == null)
                return onFailure("Token endpoint not configured");

            if (string.IsNullOrWhiteSpace(username))
                return onFailure("Username is required");

            if (string.IsNullOrWhiteSpace(password))
                return onFailure("Password is required");

            var client = httpClient ?? new HttpClient();
            var disposeClient = httpClient is null;

            try
            {
                // RFC 6749 §4.3.2: Build token request
                var requestParams = new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["username"] = username,
                    ["password"] = password
                };

                // Add scope if provided (RFC 6749 §4.3.2)
                if (!string.IsNullOrWhiteSpace(scope))
                    requestParams["scope"] = scope;

                // RFC 6749 §3.2.1: Client authentication required for confidential clients
                if (!string.IsNullOrWhiteSpace(this.clientId))
                    requestParams["client_id"] = this.clientId;

                if (!string.IsNullOrWhiteSpace(this.clientSecret))
                    requestParams["client_secret"] = this.clientSecret;

                var requestContent = new FormUrlEncodedContent(requestParams);

                // RFC 6749 §3.2: POST to token endpoint (MUST use TLS)
                var response = await client.PostAsync(this.tokenEndpoint, requestContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // RFC 6749 §4.3.3: Successful response (same as §5.1)
                    var tokenResponse = JsonConvert.DeserializeObject<AccessTokenResponse>(responseBody);
                    return onSuccess(tokenResponse);
                }

                // RFC 6749 §5.2: Error response
                var errorResponse = JsonConvert.DeserializeObject<TokenErrorResponse>(responseBody);
                return onError(errorResponse);
            }
            catch (Exception ex)
            {
                return onFailure($"Password credentials exchange failed: {ex.Message}");
            }
            finally
            {
                if (disposeClient)
                    client.Dispose();
            }
        }

        #endregion

        #region RFC 6749 Section 4.4 - Client Credentials Grant

        /// <summary>
        /// Requests access token using client credentials per RFC 6749 §4.4.
        /// Used when client is requesting access to protected resources under its control.
        /// </summary>
        /// <remarks>
        /// RFC 6749 §4.4: The client credentials grant type MUST only be used by confidential clients.
        /// The client can request an access token using only its client credentials when the client
        /// is requesting access to protected resources under its control.
        /// 
        /// Security Considerations:
        /// - MUST only be used by confidential clients (RFC 6749 §4.4)
        /// - Client MUST authenticate with authorization server (RFC 6749 §3.2.1)
        /// - Refresh token SHOULD NOT be included (RFC 6749 §4.4.3)
        /// </remarks>
        public async Task<TResult> RequestClientCredentialsTokenAsync<TResult>(
            string scope = null,
            HttpClient httpClient = null,
            Func<AccessTokenResponse, TResult> onSuccess = null,
            Func<TokenErrorResponse, TResult> onError = null,
            Func<string, TResult> onFailure = null)
        {
            if (this.tokenEndpoint == null)
                return onFailure("Token endpoint not configured");

            // RFC 6749 §4.4: Client credentials grant requires client authentication
            if (string.IsNullOrWhiteSpace(this.clientId))
                return onFailure("Client ID is required for client credentials grant");

            if (string.IsNullOrWhiteSpace(this.clientSecret))
                return onFailure("Client secret is required for client credentials grant (confidential clients only)");

            var client = httpClient ?? new HttpClient();
            var disposeClient = httpClient is null;

            try
            {
                // RFC 6749 §4.4.2: Build token request
                var requestParams = new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = this.clientId,
                    ["client_secret"] = this.clientSecret
                };

                // Add scope if provided (RFC 6749 §4.4.2)
                if (!string.IsNullOrWhiteSpace(scope))
                    requestParams["scope"] = scope;

                var requestContent = new FormUrlEncodedContent(requestParams);

                // RFC 6749 §3.2: POST to token endpoint (MUST use TLS)
                var response = await client.PostAsync(this.tokenEndpoint, requestContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // RFC 6749 §4.4.3: Successful response
                    // Note: Refresh token SHOULD NOT be included per RFC 6749 §4.4.3
                    var tokenResponse = JsonConvert.DeserializeObject<AccessTokenResponse>(responseBody);
                    return onSuccess(tokenResponse);
                }

                // RFC 6749 §5.2: Error response
                var errorResponse = JsonConvert.DeserializeObject<TokenErrorResponse>(responseBody);
                return onError(errorResponse);
            }
            catch (Exception ex)
            {
                return onFailure($"Client credentials token request failed: {ex.Message}");
            }
            finally
            {
                if (disposeClient)
                    client.Dispose();
            }
        }

        #endregion
    }
}
using System;
using Newtonsoft.Json;
using EastFive;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.OAuth
{
    /// <summary>
    /// Represents an OAuth 2.0 Authorization Endpoint resource per RFC 6749 Section 3.1.
    /// The authorization endpoint is used to interact with the resource owner and obtain 
    /// an authorization grant. Supports authorization code and implicit grant flows.
    /// </summary>
    /// <remarks>
    /// RFC 6749 §3.1 Requirements:
    /// - MUST verify resource owner identity
    /// - MUST use TLS (handled by infrastructure)
    /// - MUST support HTTP GET method
    /// - MAY support HTTP POST method
    /// - Endpoint URI MUST NOT include fragment component
    /// </remarks>
    [StorageTable]
    public partial struct ClientCredentialFlow : IReferenceable
    {
        #region Base

        [JsonIgnore]
        public Guid id => @ref.id;

        public const string IdPropertyName = "id";
        [RowKey]
        [RowKeyPrefix(Characters = 3)]
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        public IRef<ClientCredentialFlow> @ref;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [LastModified]
        [JsonIgnore]
        public DateTime lastModified;

        #endregion

        #region Authorization Endpoint Configuration (RFC 6749 Section 3.1)

        /// <summary>
        /// The authorization endpoint URI where authorization requests are sent.
        /// RFC 6749 §3.1: The endpoint URI MAY include query component but MUST NOT include fragment.
        /// </summary>
        public const string AuthorizationEndpointPropertyName = "authorization_endpoint";
        [ApiProperty(PropertyName = AuthorizationEndpointPropertyName)]
        [JsonProperty(PropertyName = AuthorizationEndpointPropertyName)]
        [Storage]
        public Uri authorizationEndpoint;

        /// <summary>
        /// The token endpoint URI for exchanging authorization codes for access tokens.
        /// RFC 6749 §3.2: Token endpoint used with authorization code grant.
        /// </summary>
        public const string TokenEndpointPropertyName = "token_endpoint";
        [ApiProperty(PropertyName = TokenEndpointPropertyName)]
        [JsonProperty(PropertyName = TokenEndpointPropertyName)]
        [Storage]
        public Uri tokenEndpoint;

        /// <summary>
        /// Client identifier issued during registration (RFC 6749 §2.2).
        /// REQUIRED for all authorization requests.
        /// </summary>
        public const string ClientIdPropertyName = "client_id";
        [ApiProperty(PropertyName = ClientIdPropertyName)]
        [JsonProperty(PropertyName = ClientIdPropertyName)]
        [Storage]
        public string clientId;

        /// <summary>
        /// Client secret for confidential clients (RFC 6749 §2.3).
        /// Optional - only used for confidential clients authenticating at token endpoint.
        /// </summary>
        public const string ClientSecretPropertyName = "client_secret";
        [ApiProperty(PropertyName = ClientSecretPropertyName)]
        [JsonProperty(PropertyName = ClientSecretPropertyName)]
        [Storage]
        public string clientSecret;

        /// <summary>
        /// Registered redirection URI where authorization server sends user after authorization.
        /// RFC 6749 §3.1.2: MUST be registered for public clients and confidential clients using implicit grant.
        /// </summary>
        public const string RedirectUriPropertyName = "redirect_uri";
        [ApiProperty(PropertyName = RedirectUriPropertyName)]
        [JsonProperty(PropertyName = RedirectUriPropertyName)]
        [Storage]
        public Uri redirectUri;

        /// <summary>
        /// Supported response types (RFC 6749 §3.1.1).
        /// Comma-separated list: "code" (authorization code), "token" (implicit grant).
        /// </summary>
        public const string ResponseTypesPropertyName = "response_types";
        [ApiProperty(PropertyName = ResponseTypesPropertyName)]
        [JsonProperty(PropertyName = ResponseTypesPropertyName)]
        [Storage]
        public string responseTypes;

        /// <summary>
        /// Default scope for authorization requests (RFC 6749 §3.3).
        /// Space-delimited list of scope values.
        /// </summary>
        public const string ScopePropertyName = "scope";
        [ApiProperty(PropertyName = ScopePropertyName)]
        [JsonProperty(PropertyName = ScopePropertyName)]
        [Storage]
        public string scope;

        #endregion

        #region Metadata

        public const string NamePropertyName = "name";
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        [Storage]
        public string name;

        public const string DescriptionPropertyName = "description";
        [ApiProperty(PropertyName = DescriptionPropertyName)]
        [JsonProperty(PropertyName = DescriptionPropertyName)]
        [Storage]
        public string description;

        public const string IsActivePropertyName = "is_active";
        [ApiProperty(PropertyName = IsActivePropertyName)]
        [JsonProperty(PropertyName = IsActivePropertyName)]
        [Storage]
        public bool isActive;

        public const string CreatedAtPropertyName = "created_at";
        [ApiProperty(PropertyName = CreatedAtPropertyName)]
        [JsonProperty(PropertyName = CreatedAtPropertyName)]
        [Storage]
        public DateTime createdAt;

        public const string UpdatedAtPropertyName = "updated_at";
        [ApiProperty(PropertyName = UpdatedAtPropertyName)]
        [JsonProperty(PropertyName = UpdatedAtPropertyName)]
        [Storage]
        public DateTime updatedAt;

        #endregion
    }

    #region Authorization Request/Response Models (RFC 6749 Section 3.1, 4.1, 4.2)

    /// <summary>
    /// Authorization Request parameters per RFC 6749 §4.1.1 (Authorization Code) 
    /// and §4.2.1 (Implicit Grant)
    /// </summary>
    public class AuthorizationRequest
    {
        /// <summary>
        /// REQUIRED. Value MUST be "code" for authorization code grant or "token" for implicit grant.
        /// RFC 6749 §3.1.1
        /// </summary>
        [JsonProperty("response_type")]
        public string ResponseType { get; set; }

        /// <summary>
        /// REQUIRED. The client identifier as described in RFC 6749 §2.2.
        /// </summary>
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        /// <summary>
        /// OPTIONAL. Redirection URI where authorization server sends user after authorization.
        /// RFC 6749 §3.1.2: MUST be absolute URI, MUST NOT include fragment.
        /// </summary>
        [JsonProperty("redirect_uri")]
        public string RedirectUri { get; set; }

        /// <summary>
        /// OPTIONAL. The scope of the access request as described by RFC 6749 §3.3.
        /// Space-delimited list of scope values.
        /// </summary>
        [JsonProperty("scope")]
        public string Scope { get; set; }

        /// <summary>
        /// RECOMMENDED. Opaque value used by client to maintain state between request and callback.
        /// RFC 6749 §10.12: SHOULD be used for CSRF protection.
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; }
    }

    /// <summary>
    /// Authorization Response (Success) per RFC 6749 §4.1.2 (Authorization Code) 
    /// or §4.2.2 (Implicit Grant)
    /// </summary>
    public class AuthorizationResponse
    {
        /// <summary>
        /// Authorization code for authorization code grant (RFC 6749 §4.1.2).
        /// MUST expire shortly after issuance (10 minutes maximum recommended).
        /// </summary>
        [JsonProperty("code")]
        public string Code { get; set; }

        /// <summary>
        /// Access token for implicit grant (RFC 6749 §4.2.2).
        /// </summary>
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// Token type for implicit grant (RFC 6749 §4.2.2).
        /// </summary>
        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        /// <summary>
        /// Expiration time in seconds for implicit grant (RFC 6749 §4.2.2).
        /// </summary>
        [JsonProperty("expires_in")]
        public int? ExpiresIn { get; set; }

        /// <summary>
        /// Scope of the access token (RFC 6749 §4.2.2).
        /// REQUIRED if different from requested scope.
        /// </summary>
        [JsonProperty("scope")]
        public string Scope { get; set; }

        /// <summary>
        /// REQUIRED if state parameter was present in authorization request.
        /// Exact value received from client.
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; }
    }

    /// <summary>
    /// Authorization Error Response per RFC 6749 §4.1.2.1 and §4.2.2.1
    /// </summary>
    public class AuthorizationErrorResponse
    {
        /// <summary>
        /// REQUIRED. Single ASCII error code from RFC 6749.
        /// Values: invalid_request, unauthorized_client, access_denied, 
        /// unsupported_response_type, invalid_scope, server_error, temporarily_unavailable
        /// </summary>
        [JsonProperty("error")]
        public string Error { get; set; }

        /// <summary>
        /// OPTIONAL. Human-readable ASCII text providing additional information.
        /// </summary>
        [JsonProperty("error_description")]
        public string ErrorDescription { get; set; }

        /// <summary>
        /// OPTIONAL. URI identifying a human-readable web page with error information.
        /// </summary>
        [JsonProperty("error_uri")]
        public string ErrorUri { get; set; }

        /// <summary>
        /// REQUIRED if state parameter was present in authorization request.
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; }
    }

    /// <summary>
    /// Access Token Request for Authorization Code Grant per RFC 6749 §4.1.3
    /// </summary>
    public class TokenRequest
    {
        /// <summary>
        /// REQUIRED. Value MUST be set to "authorization_code".
        /// </summary>
        [JsonProperty("grant_type")]
        public string GrantType { get; set; }

        /// <summary>
        /// REQUIRED. The authorization code received from the authorization server.
        /// </summary>
        [JsonProperty("code")]
        public string Code { get; set; }

        /// <summary>
        /// REQUIRED if redirect_uri was included in authorization request.
        /// Values MUST be identical.
        /// </summary>
        [JsonProperty("redirect_uri")]
        public string RedirectUri { get; set; }

        /// <summary>
        /// REQUIRED if client is not authenticating with authorization server.
        /// </summary>
        [JsonProperty("client_id")]
        public string ClientId { get; set; }
    }

    /// <summary>
    /// Access Token Response per RFC 6749 §5.1
    /// </summary>
    public class AccessTokenResponse
    {
        /// <summary>
        /// REQUIRED. The access token issued by the authorization server.
        /// </summary>
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// REQUIRED. The type of the token issued (e.g., "Bearer").
        /// </summary>
        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        /// <summary>
        /// RECOMMENDED. The lifetime in seconds of the access token.
        /// </summary>
        [JsonProperty("expires_in")]
        public int? ExpiresIn { get; set; }

        /// <summary>
        /// OPTIONAL. The refresh token for obtaining new access tokens.
        /// </summary>
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// OPTIONAL if identical to requested scope, otherwise REQUIRED.
        /// </summary>
        [JsonProperty("scope")]
        public string Scope { get; set; }
    }

    /// <summary>
    /// Token Error Response per RFC 6749 §5.2
    /// </summary>
    public class TokenErrorResponse
    {
        /// <summary>
        /// REQUIRED. Error code: invalid_request, invalid_client, invalid_grant,
        /// unauthorized_client, unsupported_grant_type, invalid_scope
        /// </summary>
        [JsonProperty("error")]
        public string Error { get; set; }

        /// <summary>
        /// OPTIONAL. Human-readable ASCII text providing additional information.
        /// </summary>
        [JsonProperty("error_description")]
        public string ErrorDescription { get; set; }

        /// <summary>
        /// OPTIONAL. URI identifying a human-readable web page with error information.
        /// </summary>
        [JsonProperty("error_uri")]
        public string ErrorUri { get; set; }
    }

    #endregion

    #region Resource Owner Password Credentials Grant (RFC 6749 Section 4.3)

    /// <summary>
    /// Resource Owner Password Credentials Grant Request per RFC 6749 §4.3.2
    /// Used when resource owner has trust relationship with client.
    /// </summary>
    public class ResourceOwnerPasswordRequest
    {
        /// <summary>
        /// REQUIRED. Value MUST be set to "password".
        /// </summary>
        [JsonProperty("grant_type")]
        public string GrantType { get; set; } = "password";

        /// <summary>
        /// REQUIRED. The resource owner username.
        /// </summary>
        [JsonProperty("username")]
        public string Username { get; set; }

        /// <summary>
        /// REQUIRED. The resource owner password.
        /// </summary>
        [JsonProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// OPTIONAL. The scope of the access request as described by RFC 6749 §3.3.
        /// </summary>
        [JsonProperty("scope")]
        public string Scope { get; set; }
    }

    #endregion

    #region Client Credentials Grant (RFC 6749 Section 4.4)

    /// <summary>
    /// Client Credentials Grant Request per RFC 6749 §4.4.2
    /// Used when client is requesting access to protected resources under its control.
    /// </summary>
    public class ClientCredentialsRequest
    {
        /// <summary>
        /// REQUIRED. Value MUST be set to "client_credentials".
        /// </summary>
        [JsonProperty("grant_type")]
        public string GrantType { get; set; } = "client_credentials";

        /// <summary>
        /// OPTIONAL. The scope of the access request as described by RFC 6749 §3.3.
        /// </summary>
        [JsonProperty("scope")]
        public string Scope { get; set; }
    }

    #endregion
}

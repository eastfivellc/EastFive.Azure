using System;
using Newtonsoft.Json;
using EastFive;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.OAuth
{
    /// <summary>
    /// Represents an external client registered per OAuth 2.0 Client Registration (RFC 6749 Section 2)
    /// This resource stores client configurations for external applications that access this API.
    /// </summary>
    [StorageTable]
    public partial struct ClientCredential : IReferenceable
    {
        #region Base Properties

        [JsonIgnore]
        public Guid id => @ref.id;

        public const string IdPropertyName = "id";
        [RowKey]
        [RowKeyPrefix(Characters = 3)]
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        public IRef<ClientCredential> @ref;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [LastModified]
        [JsonIgnore]
        public DateTime lastModified;

        #endregion

        #region Client Registration (RFC 6749 Section 2)

        const string ClientIdPropertyName = "client_id";
        /// <summary>
        /// OAuth 2.0 client identifier (RFC 6749 Section 2.2)
        /// Unique string representing the registration information.
        /// NOT a secret, exposed to resource owner.
        /// </summary>
        [ApiProperty(PropertyName = ClientIdPropertyName)]
        [JsonProperty(PropertyName = ClientIdPropertyName)]
        [Storage]
        [StorageConstraintUnique]
        public string clientId;

        const string ClientTypePropertyName = "client_type";
        /// <summary>
        /// OAuth 2.0 client type (RFC 6749 Section 2.1)
        /// Values: "confidential" - capable of maintaining credential confidentiality
        ///         "public" - incapable of maintaining credential confidentiality
        /// </summary>
        [ApiProperty(PropertyName = ClientTypePropertyName)]
        [JsonProperty(PropertyName = ClientTypePropertyName)]
        [Storage]
        public string clientType;

        const string ClientSecretPropertyName = "client_secret";
        /// <summary>
        /// OAuth 2.0 client secret for confidential clients (RFC 6749 Section 2.3.1)
        /// NULL for public clients as they cannot maintain confidentiality.
        /// Should be hashed/encrypted in production.
        /// </summary>
        [ApiProperty(PropertyName = ClientSecretPropertyName)]
        [JsonProperty(PropertyName = ClientSecretPropertyName)]
        [Storage]
        public string clientSecret;

        const string RedirectUrisPropertyName = "redirect_uris";
        /// <summary>
        /// Redirection endpoint URIs (RFC 6749 Section 3.1.2)
        /// Comma-separated list of absolute URIs.
        /// REQUIRED for public clients and confidential clients using implicit grant.
        /// </summary>
        [ApiProperty(PropertyName = RedirectUrisPropertyName)]
        [JsonProperty(PropertyName = RedirectUrisPropertyName)]
        [Storage]
        public string redirectUris;

        const string GrantTypesPropertyName = "grant_types";
        /// <summary>
        /// OAuth 2.0 grant types this client is authorized to use
        /// Comma-separated list. Values: "authorization_code", "implicit", "password", "client_credentials", "refresh_token"
        /// </summary>
        [ApiProperty(PropertyName = GrantTypesPropertyName)]
        [JsonProperty(PropertyName = GrantTypesPropertyName)]
        [Storage]
        public string grantTypes;

        const string TokenEndpointAuthMethodPropertyName = "token_endpoint_auth_method";
        /// <summary>
        /// Authentication method for token endpoint (RFC 6749 Section 2.3)
        /// Values: "client_secret_basic", "client_secret_post", "none"
        /// </summary>
        [ApiProperty(PropertyName = TokenEndpointAuthMethodPropertyName)]
        [JsonProperty(PropertyName = TokenEndpointAuthMethodPropertyName)]
        [Storage]
        public string tokenEndpointAuthMethod;

        #endregion

        #region Client Metadata

        const string NamePropertyName = "name";
        /// <summary>
        /// Human-readable name for the client application
        /// </summary>
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        [Storage]
        public string name;

        const string DescriptionPropertyName = "description";
        /// <summary>
        /// Description of the client application's purpose
        /// </summary>
        [ApiProperty(PropertyName = DescriptionPropertyName)]
        [JsonProperty(PropertyName = DescriptionPropertyName)]
        [Storage]
        public string description;

        const string ScopePropertyName = "scope";
        /// <summary>
        /// Space-delimited list of scopes this client is authorized to request (RFC 6749 Section 3.3)
        /// </summary>
        [ApiProperty(PropertyName = ScopePropertyName)]
        [JsonProperty(PropertyName = ScopePropertyName)]
        [Storage]
        public string scope;

        #endregion

        #region Status and Audit

        const string IsActivePropertyName = "is_active";
        /// <summary>
        /// Whether this client is currently active and can authenticate
        /// </summary>
        [ApiProperty(PropertyName = IsActivePropertyName)]
        [JsonProperty(PropertyName = IsActivePropertyName)]
        [Storage]
        public bool isActive;

        const string CreatedAtPropertyName = "created_at";
        /// <summary>
        /// Timestamp when the client was created
        /// </summary>
        [ApiProperty(PropertyName = CreatedAtPropertyName)]
        [JsonProperty(PropertyName = CreatedAtPropertyName)]
        [Storage]
        public DateTime createdAt;

        const string UpdatedAtPropertyName = "updated_at";
        /// <summary>
        /// Timestamp when the client was last updated
        /// </summary>
        [ApiProperty(PropertyName = UpdatedAtPropertyName)]
        [JsonProperty(PropertyName = UpdatedAtPropertyName)]
        [Storage]
        public DateTime updatedAt;

        const string LastUsedAtPropertyName = "last_used_at";
        /// <summary>
        /// Timestamp when the client last successfully authenticated
        /// </summary>
        [ApiProperty(PropertyName = LastUsedAtPropertyName)]
        [JsonProperty(PropertyName = LastUsedAtPropertyName)]
        [Storage]
        public DateTime? lastUsedAt;

        #endregion

        #region Optional Metadata

        const string ContactEmailPropertyName = "contact_email";
        /// <summary>
        /// Contact email for the client application owner
        /// </summary>
        [ApiProperty(PropertyName = ContactEmailPropertyName)]
        [JsonProperty(PropertyName = ContactEmailPropertyName)]
        [Storage]
        public string contactEmail;

        #endregion
    }
}

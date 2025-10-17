using System;
using Newtonsoft.Json;
using EastFive;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.OAuth
{
    /// <summary>
    /// Represents an OAuth 2.0 Client Credentials Flow configuration (RFC 6749 Section 4.4)
    /// </summary>
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

        const string ClientIdPropertyName = "client_id";
        [ApiProperty(PropertyName = ClientIdPropertyName)]
        [JsonProperty(PropertyName = ClientIdPropertyName)]
        [Storage]
        public string clientId;

        const string ClientSecretPropertyName = "client_secret";
        [ApiProperty(PropertyName = ClientSecretPropertyName)]
        [JsonProperty(PropertyName = ClientSecretPropertyName)]
        [Storage]
        public string clientSecret;

        const string TokenEndpointPropertyName = "token_endpoint";
        [ApiProperty(PropertyName = TokenEndpointPropertyName)]
        [JsonProperty(PropertyName = TokenEndpointPropertyName)]
        [Storage]
        public Uri tokenEndpoint;

        const string ScopePropertyName = "scope";
        [ApiProperty(PropertyName = ScopePropertyName)]
        [JsonProperty(PropertyName = ScopePropertyName)]
        [Storage]
        public string scope;

        const string NamePropertyName = "name";
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        [Storage]
        public string name;

        const string DescriptionPropertyName = "description";
        [ApiProperty(PropertyName = DescriptionPropertyName)]
        [JsonProperty(PropertyName = DescriptionPropertyName)]
        [Storage]
        public string description;

        const string IsActivePropertyName = "is_active";
        [ApiProperty(PropertyName = IsActivePropertyName)]
        [JsonProperty(PropertyName = IsActivePropertyName)]
        [Storage]
        public bool isActive;

        const string CreatedAtPropertyName = "created_at";
        [ApiProperty(PropertyName = CreatedAtPropertyName)]
        [JsonProperty(PropertyName = CreatedAtPropertyName)]
        [Storage]
        public DateTime createdAt;

        const string UpdatedAtPropertyName = "updated_at";
        [ApiProperty(PropertyName = UpdatedAtPropertyName)]
        [JsonProperty(PropertyName = UpdatedAtPropertyName)]
        [Storage]
        public DateTime updatedAt;
    }

    /// <summary>
    /// OAuth 2.0 Access Token Response (RFC 6749 Section 5.1)
    /// </summary>
    public class AccessTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }
    }

    /// <summary>
    /// OAuth 2.0 Error Response (RFC 6749 Section 5.2)
    /// </summary>
    public class OAuthErrorResponse
    {
        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("error_description")]
        public string ErrorDescription { get; set; }

        [JsonProperty("error_uri")]
        public string ErrorUri { get; set; }
    }
}

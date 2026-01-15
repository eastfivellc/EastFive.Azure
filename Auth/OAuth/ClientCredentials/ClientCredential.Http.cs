using System;
using System.Linq;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Serialization.Json;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;
using Newtonsoft.Json;

namespace EastFive.Azure.OAuth
{
    /// <summary>
    /// HTTP API for managing OAuth 2.0 Client Credentials (RFC 6749 Section 4.4)
    /// Stores and allows HTTP access to a list of external clients which authenticate using client credential flows.
    /// </summary>
    [FunctionViewController(
        Route = "OAuth/ClientCredential",
        ContentType = "application/x-oauth-clientcredential+json")]
    [CastSerialization]
    public partial struct ClientCredential
    {
        /// <summary>
        /// GET /OAuth/ClientCredential - Get all client credentials
        /// </summary>
        [HttpGet]
        [SuperAdminClaim]
        public static IHttpResponse GetAllAsync(
                RequestMessage<ClientCredential> clientsQuery,
            MultipartAsyncResponse<ClientCredential> onResults)
        {
            return clientsQuery
                .StorageGet()
                .HttpResponse(onResults);
        }

        /// <summary>
        /// GET /OAuth/ClientCredential/{id} - Get specific client by ID
        /// </summary>
        [HttpGet]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> GetByIdAsync(
                [UpdateId] IRef<ClientCredential> clientRef,
            ContentTypeResponse<ClientCredential> onFound,
            NotFoundResponse onNotFound)
        {
            return await clientRef.StorageGetAsync(
                client => onFound(client),
                () => onNotFound());
        }

        /// <summary>
        /// POST /OAuth/ClientCredential - Create new OAuth 2.0 client registration (RFC 6749 Section 2)
        /// </summary>
        [HttpPost]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> CreateAsync(
                [UpdateId] IRef<ClientCredential> clientRef,
                [Property(Name = ClientIdPropertyName)] string clientId,
                [Property(Name = ClientTypePropertyName)] string clientType,
                [PropertyOptional(Name = ClientSecretPropertyName)] string clientSecret,
                [Property(Name = NamePropertyName)] string name,
                [PropertyOptional(Name = DescriptionPropertyName)] string description,
                [PropertyOptional(Name = ScopePropertyName)] string scope,
                [PropertyOptional(Name = RedirectUrisPropertyName)] string redirectUris,
                [PropertyOptional(Name = GrantTypesPropertyName)] string grantTypes,
                [PropertyOptional(Name = TokenEndpointAuthMethodPropertyName)] string tokenEndpointAuthMethod,
                [PropertyOptional(Name = IsActivePropertyName)] bool? isActive,
                [PropertyOptional(Name = ContactEmailPropertyName)] string contactEmail,
                [Resource] ClientCredential client,
            CreatedBodyResponse<ClientCredential> onCreated,
            AlreadyExistsResponse onAlreadyExists,
            BadRequestResponse onBadRequest)
        {
            // Validate required fields per RFC 6749 Section 2
            if (string.IsNullOrWhiteSpace(clientId))
                return onBadRequest().AddReason("client_id is required");
            
            if (string.IsNullOrWhiteSpace(clientType))
                return onBadRequest().AddReason("client_type is required (must be 'confidential' or 'public')");

            if (string.IsNullOrWhiteSpace(name))
                return onBadRequest().AddReason("name is required");

            // Set defaults and audit timestamps
            client.isActive = isActive ?? true;
            client.createdAt = DateTime.UtcNow;
            client.updatedAt = DateTime.UtcNow;
            client.lastUsedAt = null;

            // Validate registration per RFC 6749 Section 2
            return await client.ValidateRegistration(
                () =>
                {
                    return client.StorageCreateAsync(
                        created => onCreated(created.Entity),
                        onAlreadyExists: () => onAlreadyExists());
                },
                invalidReason => onBadRequest().AddReason(invalidReason).AsTask());
        }

        /// <summary>
        /// PATCH /OAuth/ClientCredential/{id} - Update existing client credential
        /// </summary>
        [HttpPatch]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> UpdateAsync(
                [UpdateId] IRef<ClientCredential> clientRef,
                MutateResource<ClientCredential> mutateResource,
            ContentTypeResponse<ClientCredential> onUpdated,
            NotFoundResponse onNotFound)
        {
            return await clientRef.StorageUpdateAsync2(
                client =>
                {
                    client.updatedAt = DateTime.UtcNow;
                    var updates = mutateResource(client);
                    return updates;
                },
                (updatedClient) => onUpdated(updatedClient),
                () => onNotFound());
        }

        /// <summary>
        /// DELETE /OAuth/ClientCredential/{id} - Delete client credential
        /// </summary>
        [HttpDelete]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> DeleteAsync(
                [UpdateId] IRef<ClientCredential> clientRef,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound)
        {
            return await clientRef.StorageDeleteAsync(
                deleted => onDeleted(),
                () => onNotFound());
        }

        /// <summary>
        /// POST /OAuth/ClientCredential/authenticate - Authenticate a client using client credentials
        /// Implements RFC 6749 Section 4.4: Client Credentials Grant
        /// This endpoint validates client_id and client_secret and returns success/failure
        /// </summary>
        [HttpAction("authenticate")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> AuthenticateAsync(
                [Property(Name = ClientIdPropertyName)] string clientId,
                [Property(Name = ClientSecretPropertyName)] string clientSecret,
            ContentTypeResponse<ClientAuthenticationResponse> onAuthenticated,
            UnauthorizedResponse onUnauthorized,
            BadRequestResponse onBadRequest)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return onBadRequest().AddReason("client_id is required");

            if (string.IsNullOrWhiteSpace(clientSecret))
                return onBadRequest().AddReason("client_secret is required");

            // Find client by clientId using StorageGetBy with unique constraint
            var matchingClients = await clientId.StorageGetBy(
                (ClientCredential client) => client.clientId)
                .ToArrayAsync();

            if (matchingClients.Length == 0)
                return onUnauthorized().AddReason("Invalid client_id");

            var client = matchingClients.First();

            // Validate client is active
            if (!client.isActive)
                return onUnauthorized().AddReason("Client is not active");

            // Validate client secret
            // Note: In production, this should use secure hashing comparison
            if (client.clientSecret != clientSecret)
                return onUnauthorized().AddReason("Invalid client credentials");

            // Update last used timestamp (fire and forget for performance)
            _ = client.@ref.StorageUpdateAsync2(
                c =>
                {
                    c.lastUsedAt = DateTime.UtcNow;
                    c.updatedAt = DateTime.UtcNow;
                    return c;
                },
                _ => true,
                () => false);

            // Return authentication success
            var response = new ClientAuthenticationResponse
            {
                ClientId = client.clientId,
                Name = client.name,
                Scope = client.scope,
                IsActive = client.isActive
            };

            return onAuthenticated(response);
        }

        /// <summary>
        /// POST /OAuth/ClientCredential/{id}/rotate-secret - Rotate client secret
        /// Generates a new client secret for security purposes
        /// </summary>
        [HttpAction("rotate-secret")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> RotateSecretAsync(
                [UpdateId] IRef<ClientCredential> clientRef,
            ContentTypeResponse<ClientSecretResponse> onRotated,
            NotFoundResponse onNotFound)
        {
            return await clientRef.StorageUpdateAsync2(
                client =>
                {
                    // Generate new secret (in production, use cryptographically secure generation)
                    var newSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                    client.clientSecret = newSecret;
                    client.updatedAt = DateTime.UtcNow;

                    return client;
                },
                updatedClient =>
                {
                    var response = new ClientSecretResponse
                    {
                        ClientId = updatedClient.clientId,
                        ClientSecret = updatedClient.clientSecret,
                        UpdatedAt = updatedClient.updatedAt
                    };
                    return onRotated(response);
                },
                () => onNotFound());
        }

        /// <summary>
        /// POST /OAuth/ClientCredential/{id}/activate - Activate a client
        /// </summary>
        [HttpAction("activate")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> ActivateAsync(
                [UpdateId] IRef<ClientCredential> clientRef,
            ContentTypeResponse<ClientCredential> onActivated,
            NotFoundResponse onNotFound)
        {
            return await clientRef.StorageUpdateAsync2(
                client =>
                {
                    client.isActive = true;
                    client.updatedAt = DateTime.UtcNow;
                    return client;
                },
                (updatedClient) => onActivated(updatedClient),
                () => onNotFound());
        }

        /// <summary>
        /// POST /OAuth/ClientCredential/{id}/deactivate - Deactivate a client
        /// </summary>
        [HttpAction("deactivate")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> DeactivateAsync(
                [UpdateId] IRef<ClientCredential> clientRef,
            ContentTypeResponse<ClientCredential> onDeactivated,
            NotFoundResponse onNotFound)
        {
            return await clientRef.StorageUpdateAsync2(
                client =>
                {
                    client.isActive = false;
                    client.updatedAt = DateTime.UtcNow;
                    return client;
                },
                (updatedClient) => onDeactivated(updatedClient),
                () => onNotFound());
        }
    }

    /// <summary>
    /// Response for successful client authentication
    /// </summary>
    public class ClientAuthenticationResponse
    {
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }

        [JsonProperty("is_active")]
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Response for client secret rotation
    /// </summary>
    public class ClientSecretResponse
    {
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("client_secret")]
        public string ClientSecret { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}

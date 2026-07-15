using System;
using System.Linq;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// POST /oauth/register — OAuth 2.0 Dynamic Client Registration (RFC 7591).
    /// MCP clients (Claude, VS Code, etc.) self-register as PUBLIC clients here
    /// (token_endpoint_auth_method "none"); no client secret is ever issued from
    /// this endpoint. Confidential clients are provisioned by admins via
    /// /api/OAuth/ClientCredential instead.
    /// </summary>
    [FunctionViewController(
        Namespace = "oauth",
        Route = "register",
        ContentType = "application/json")]
    public class OAuthClientRegistration
    {
        [Unsecured("RFC 7591 dynamic client registration - MCP clients must be able to self-register; only public (secret-less) clients are created and every authorization still requires user consent.")]
        [HttpPost(MatchAllParameters = false)]
        public static async Task<IHttpResponse> RegisterAsync(
                IHttpRequest request,
            CreatedBodyResponse<ClientRegistrationResponse> onRegistered,
            BadRequestBodyResponse<ClientRegistrationError> onInvalid)
        {
            ClientRegistrationRequest registration;
            try
            {
                var body = await request.ReadContentAsStringAsync();
                registration = JsonConvert.DeserializeObject<ClientRegistrationRequest>(body);
            }
            catch (JsonException)
            {
                registration = default;
            }
            if (registration == null)
                return Invalid("Request body must be a JSON client metadata document (RFC 7591).");

            #region Validate metadata (RFC 7591 section 2, OAuth 2.1 constraints)

            if (registration.RedirectUris.IsDefaultNullOrEmpty())
                return Invalid("redirect_uris is required.");

            foreach (var uriString in registration.RedirectUris)
            {
                if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri)
                        || !OAuthServer.IsAllowableRedirectUri(uri))
                    return Invalid(
                        $"redirect_uri `{uriString}` must be absolute https, loopback http, or a private-use scheme, without a fragment.");
            }

            var authMethod = registration.TokenEndpointAuthMethod.HasBlackSpace()
                ? registration.TokenEndpointAuthMethod
                : ClientCredential.TokenEndpointAuthMethods.None;
            if (authMethod != ClientCredential.TokenEndpointAuthMethods.None)
                return Invalid(
                    "Only public clients (token_endpoint_auth_method `none`) can be dynamically registered. Confidential clients are provisioned by an administrator.");

            var grantTypes = registration.GrantTypes.IsDefaultNullOrEmpty()
                ? new[] { ClientCredential.GrantTypeValues.AuthorizationCode, ClientCredential.GrantTypeValues.RefreshToken }
                : registration.GrantTypes;
            var disallowedGrant = grantTypes
                .Where(grant =>
                    grant != ClientCredential.GrantTypeValues.AuthorizationCode
                    && grant != ClientCredential.GrantTypeValues.RefreshToken)
                .FirstOrDefault();
            if (disallowedGrant != null)
                return Invalid($"grant_type `{disallowedGrant}` is not supported for dynamically registered clients.");

            var responseTypes = registration.ResponseTypes.IsDefaultNullOrEmpty()
                ? new[] { "code" }
                : registration.ResponseTypes;
            if (responseTypes.Any(rt => rt != "code"))
                return Invalid("Only the `code` response type is supported.");

            #endregion

            var clientRef = Guid.NewGuid().AsRef<ClientCredential>();
            var client = new ClientCredential
            {
                @ref = clientRef,
                clientId = clientRef.id.ToString("N"),
                clientType = ClientCredential.ClientTypes.Public,
                tokenEndpointAuthMethod = ClientCredential.TokenEndpointAuthMethods.None,
                redirectUris = registration.RedirectUris.Join(","),
                grantTypes = grantTypes.Join(","),
                name = registration.ClientName.HasBlackSpace()
                    ? registration.ClientName
                    : "Dynamically registered client",
                description = registration.ClientUri,
                scope = registration.Scope,
                isActive = true,
                createdAt = DateTime.UtcNow,
                updatedAt = DateTime.UtcNow,
            };

            return await client.StorageCreateAsync(
                created =>
                {
                    var response = new ClientRegistrationResponse
                    {
                        ClientId = client.clientId,
                        ClientIdIssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ClientName = client.name,
                        RedirectUris = registration.RedirectUris,
                        GrantTypes = grantTypes,
                        ResponseTypes = responseTypes,
                        TokenEndpointAuthMethod = client.tokenEndpointAuthMethod,
                        Scope = client.scope,
                    };
                    return onRegistered(response, "application/json");
                },
                onAlreadyExists: () => Invalid("Client already registered."));

            IHttpResponse Invalid(string description) =>
                onInvalid(
                    new ClientRegistrationError
                    {
                        Error = "invalid_client_metadata",
                        ErrorDescription = description,
                    },
                    "application/json");
        }
    }

    public class ClientRegistrationRequest
    {
        [JsonProperty("redirect_uris")]
        public string[] RedirectUris { get; set; }

        [JsonProperty("client_name")]
        public string ClientName { get; set; }

        [JsonProperty("client_uri")]
        public string ClientUri { get; set; }

        [JsonProperty("token_endpoint_auth_method")]
        public string TokenEndpointAuthMethod { get; set; }

        [JsonProperty("grant_types")]
        public string[] GrantTypes { get; set; }

        [JsonProperty("response_types")]
        public string[] ResponseTypes { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }
    }

    public class ClientRegistrationResponse
    {
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("client_id_issued_at")]
        public long ClientIdIssuedAt { get; set; }

        [JsonProperty("client_name")]
        public string ClientName { get; set; }

        [JsonProperty("redirect_uris")]
        public string[] RedirectUris { get; set; }

        [JsonProperty("grant_types")]
        public string[] GrantTypes { get; set; }

        [JsonProperty("response_types")]
        public string[] ResponseTypes { get; set; }

        [JsonProperty("token_endpoint_auth_method")]
        public string TokenEndpointAuthMethod { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }
    }

    public class ClientRegistrationError
    {
        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("error_description")]
        public string ErrorDescription { get; set; }
    }
}

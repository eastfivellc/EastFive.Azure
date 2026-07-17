using System;

using Newtonsoft.Json;

using EastFive.Api;

namespace EastFive.Azure.OAuth.Server
{
    /// <summary>
    /// GET /.well-known/oauth-authorization-server — OAuth 2.0 Authorization Server
    /// Metadata (RFC 8414). MCP clients use this to discover the authorize/token/
    /// registration endpoints.
    /// </summary>
    [FunctionViewController(
        Namespace = ".well-known",
        Route = "oauth-authorization-server",
        ContentType = "application/json")]
    public class AuthorizationServerMetadata
    {
        [Unsecured("RFC 8414 metadata documents are public by definition.")]
        [HttpGet(MatchAllParameters = false)]
        public static IHttpResponse Get(
                IHttpRequest request,
            ContentTypeResponse<AuthorizationServerMetadataDocument> onFound)
        {
            var origin = OAuthServer.Origin(request.RequestUri);
            var document = new AuthorizationServerMetadataDocument
            {
                Issuer = OAuthServer.Issuer(request.RequestUri),
                AuthorizationEndpoint = $"{origin}/oauth/authorize",
                TokenEndpoint = $"{origin}/oauth/token",
                RegistrationEndpoint = $"{origin}/oauth/register",
                RevocationEndpoint = $"{origin}/oauth/revoke",
                JwksUri = $"{origin}/.well-known/jwks.json",
                ResponseTypesSupported = new[] { "code" },
                GrantTypesSupported = new[] { "authorization_code", "refresh_token", "client_credentials" },
                CodeChallengeMethodsSupported = new[] { OAuthServer.CodeChallengeMethodS256 },
                ScopesSupported = OAuthServer.ScopesSupported(),
                TokenEndpointAuthMethodsSupported = new[]
                {
                    ClientCredential.TokenEndpointAuthMethods.None,
                    ClientCredential.TokenEndpointAuthMethods.ClientSecretBasic,
                    ClientCredential.TokenEndpointAuthMethods.ClientSecretPost,
                },
            };
            return onFound(document, "application/json");
        }
    }

    /// <summary>
    /// GET /.well-known/oauth-protected-resource — OAuth 2.0 Protected Resource
    /// Metadata (RFC 9728). This is the first document an MCP client fetches; it
    /// points at the authorization server (this same host).
    /// </summary>
    [FunctionViewController(
        Namespace = ".well-known",
        Route = "oauth-protected-resource",
        ContentType = "application/json")]
    public class ProtectedResourceMetadata
    {
        [Unsecured("RFC 9728 metadata documents are public by definition.")]
        [HttpGet(MatchAllParameters = false)]
        public static IHttpResponse Get(
                IHttpRequest request,
            ContentTypeResponse<ProtectedResourceMetadataDocument> onFound)
        {
            var origin = OAuthServer.Origin(request.RequestUri);
            var document = new ProtectedResourceMetadataDocument
            {
                Resource = origin,
                AuthorizationServers = new[] { OAuthServer.Issuer(request.RequestUri) },
                BearerMethodsSupported = new[] { "header" },
                ScopesSupported = OAuthServer.ScopesSupported(),
            };
            return onFound(document, "application/json");
        }
    }

    public class AuthorizationServerMetadataDocument
    {
        [JsonProperty("issuer")]
        public string Issuer { get; set; }

        [JsonProperty("authorization_endpoint")]
        public string AuthorizationEndpoint { get; set; }

        [JsonProperty("token_endpoint")]
        public string TokenEndpoint { get; set; }

        [JsonProperty("registration_endpoint")]
        public string RegistrationEndpoint { get; set; }

        [JsonProperty("revocation_endpoint")]
        public string RevocationEndpoint { get; set; }

        [JsonProperty("jwks_uri")]
        public string JwksUri { get; set; }

        [JsonProperty("response_types_supported")]
        public string[] ResponseTypesSupported { get; set; }

        [JsonProperty("grant_types_supported")]
        public string[] GrantTypesSupported { get; set; }

        [JsonProperty("code_challenge_methods_supported")]
        public string[] CodeChallengeMethodsSupported { get; set; }

        [JsonProperty("scopes_supported")]
        public string[] ScopesSupported { get; set; }

        [JsonProperty("token_endpoint_auth_methods_supported")]
        public string[] TokenEndpointAuthMethodsSupported { get; set; }
    }

    public class ProtectedResourceMetadataDocument
    {
        [JsonProperty("resource")]
        public string Resource { get; set; }

        [JsonProperty("authorization_servers")]
        public string[] AuthorizationServers { get; set; }

        [JsonProperty("bearer_methods_supported")]
        public string[] BearerMethodsSupported { get; set; }

        [JsonProperty("scopes_supported")]
        public string[] ScopesSupported { get; set; }
    }
}

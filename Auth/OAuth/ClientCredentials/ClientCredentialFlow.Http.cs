using System;
using System.Threading.Tasks;
using EastFive;
using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Azure.Auth;
using EastFive.Extensions;
using EastFive.Linq.Async;

namespace EastFive.Azure.OAuth
{
    /// <summary>
    /// HTTP API for OAuth 2.0 Client Credentials Flow (RFC 6749 Section 4.4)
    /// </summary>
    [FunctionViewController(
        Route = "OAuth/ClientCredentialFlow",
        ContentType = "application/json",
        ContentTypeVersion = "1.0")]
    public partial struct ClientCredentialFlow
    { /*
        /// <summary>
        /// GET /OAuth/ClientCredentialFlow - Get all client credential flows
        /// </summary>
        [HttpGet]
        public static async Task<IHttpResponse> GetAllAsync(
                EastFive.Api.Security security,
            MultipartResponseAsync<ClientCredentialFlow> onFound)
        {
            var flows = GetAll();
            return await onFound(flows);
        }

        /// <summary>
        /// GET /OAuth/ClientCredentialFlow?is_active=true - Get active flows
        /// </summary>
        [HttpGet]
        public static async Task<IHttpResponse> GetActiveAsync(
                [QueryParameter(Name = IsActivePropertyName)] bool isActive,
                Security security,
            MultipartResponseAsync<ClientCredentialFlow> onFound)
        {
            if (!isActive)
                return await onFound(GetAll());

            var flows = GetActive();
            return await onFound(flows);
        }

        /// <summary>
        /// GET /OAuth/ClientCredentialFlow/{id} - Get specific flow by ID
        /// </summary>
        [HttpGet]
        public static async Task<IHttpResponse> GetByIdAsync(
                [QueryParameter(Name = IdPropertyName)] IRef<ClientCredentialFlow> flowRef,
                Security security,
            ContentTypeResponse<ClientCredentialFlow> onFound,
            NotFoundResponse onNotFound)
        {
            return await GetAsync(flowRef,
                flow => onFound(flow),
                () => onNotFound());
        }

        /// <summary>
        /// POST /OAuth/ClientCredentialFlow - Create new client credential flow configuration
        /// </summary>
        [HttpPost]
        public static async Task<IHttpResponse> CreateAsync(
                [Property(Name = IdPropertyName)] IRef<ClientCredentialFlow> flowRef,
                [Property(Name = ClientIdPropertyName)] string clientId,
                [Property(Name = ClientSecretPropertyName)] string clientSecret,
                [Property(Name = TokenEndpointPropertyName)] Uri tokenEndpoint,
                [Property(Name = ScopePropertyName)] string scope,
                [Property(Name = NamePropertyName)] string name,
                [Property(Name = DescriptionPropertyName)] string description,
                [PropertyOptional(Name = IsActivePropertyName)] bool isActive = true,
                Security security,
            CreatedBodyResponse<ClientCredentialFlow> onCreated,
            AlreadyExistsResponse onAlreadyExists)
        {
            return await CreateAsync(flowRef, clientId, clientSecret, tokenEndpoint, 
                scope, name, description, isActive,
                flow => onCreated(flow),
                () => onAlreadyExists());
        }

        /// <summary>
        /// PATCH /OAuth/ClientCredentialFlow/{id} - Update existing flow configuration
        /// </summary>
        [HttpPatch]
        public static async Task<IHttpResponse> UpdateAsync(
                [Property(Name = IdPropertyName)] IRef<ClientCredentialFlow> flowRef,
                [PropertyOptional(Name = ClientIdPropertyName)] string clientId,
                [PropertyOptional(Name = ClientSecretPropertyName)] string clientSecret,
                [PropertyOptional(Name = TokenEndpointPropertyName)] Uri tokenEndpoint,
                [PropertyOptional(Name = ScopePropertyName)] string scope,
                [PropertyOptional(Name = NamePropertyName)] string name,
                [PropertyOptional(Name = DescriptionPropertyName)] string description,
                [PropertyOptional(Name = IsActivePropertyName)] bool? isActive,
                Security security,
            ContentTypeResponse<ClientCredentialFlow> onUpdated,
            NotFoundResponse onNotFound)
        {
            return await UpdateAsync(flowRef, clientId, clientSecret, tokenEndpoint, 
                scope, name, description, isActive,
                flow => onUpdated(flow),
                () => onNotFound());
        }

        /// <summary>
        /// DELETE /OAuth/ClientCredentialFlow/{id} - Delete flow configuration
        /// </summary>
        [HttpDelete]
        public static async Task<IHttpResponse> DeleteAsync(
                [Property(Name = IdPropertyName)] IRef<ClientCredentialFlow> flowRef,
                Security security,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound)
        {
            return await DeleteAsync(flowRef,
                () => onDeleted(),
                () => onNotFound());
        }

        /// <summary>
        /// POST /OAuth/ClientCredentialFlow/{id}/token - Execute OAuth 2.0 Client Credentials Flow
        /// Implements RFC 6749 Section 4.4: Client Credentials Grant
        /// </summary>
        [HttpAction("token")]
        public static async Task<IHttpResponse> RequestTokenAsync(
                [Property(Name = IdPropertyName)] IRef<ClientCredentialFlow> flowRef,
                Security security,
            ContentTypeResponse<AccessTokenResponse> onSuccess,
            ContentTypeResponse<OAuthErrorResponse> onOAuthError,
            NotFoundResponse onNotFound,
            BadRequestResponse onBadRequest)
        {
            return await GetAsync(flowRef,
                async flow =>
                {
                    if (!flow.isActive)
                        return onBadRequest().AddReason("Flow is not active");

                    return await flow.RequestAccessTokenAsync(
                        httpClient: null,
                        onSuccess: token => onSuccess(token),
                        onOAuthError: error => onOAuthError(error),
                        onFailure: why => onBadRequest().AddReason(why));
                },
                () => onNotFound());
        } */
    }
}

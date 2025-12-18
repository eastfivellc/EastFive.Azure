using System;
using System.Linq;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Serialization.Json;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;

namespace EastFive.Azure.OAuth
{
    [FunctionViewController(
        Namespace = "OAuth",
        Route = "ClientCredentialFlow",
        ContentType = "application/x-oauth-authorization+json")]
    [CastSerialization]
    public partial struct ClientCredentialFlow
    {
        [HttpGet]
        [SuperAdminClaim]
        public static IHttpResponse GetAllAsync(
                RequestMessage<ClientCredentialFlow> flowsQuery,
            MultipartAsyncResponse<ClientCredentialFlow> onResults)
        {
            return flowsQuery
                .StorageGet()
                .HttpResponse(onResults);
        }

        [HttpPost]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> CreateAsync(
                [UpdateId] IRef<ClientCredentialFlow> flowRef,
                [Property(Name = AuthorizationEndpointPropertyName)] Uri authorizationEndpoint,
                [PropertyOptional(Name = TokenEndpointPropertyName)] Uri tokenEndpoint,
                [Property(Name = ClientIdPropertyName)] string clientId,
                [PropertyOptional(Name = ClientSecretPropertyName)] string clientSecret,
                [Property(Name = RedirectUriPropertyName)] Uri redirectUri,
                [Property(Name = ResponseTypesPropertyName)] string responseTypes,
                [PropertyOptional(Name = ScopePropertyName)] string scope,
                [Property(Name = NamePropertyName)] string name,
                [PropertyOptional(Name = DescriptionPropertyName)] string description,
                [PropertyOptional(Name = IsActivePropertyName)] bool? isActive,
                [Resource] ClientCredentialFlow flow,
            CreatedBodyResponse<ClientCredentialFlow> onCreated,
            AlreadyExistsResponse onAlreadyExists,
            BadRequestResponse onBadRequest)
        {
            if (authorizationEndpoint == null)
                return onBadRequest().AddReason("authorization_endpoint is required");

            if (string.IsNullOrWhiteSpace(clientId))
                return onBadRequest().AddReason("client_id is required");

            if (redirectUri == null)
                return onBadRequest().AddReason("redirect_uri is required per RFC 6749 §3.1.2");

            if (string.IsNullOrWhiteSpace(responseTypes))
                return onBadRequest().AddReason("response_types is required (comma-separated: code, token)");

            if (!string.IsNullOrEmpty(authorizationEndpoint.Fragment))
                return onBadRequest().AddReason("authorization_endpoint MUST NOT include fragment per RFC 6749 §3.1");

            if (!string.IsNullOrEmpty(redirectUri.Fragment))
                return onBadRequest().AddReason("redirect_uri MUST NOT include fragment per RFC 6749 §3.1.2");

            var types = responseTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToArray();

            foreach (var type in types)
            {
                if (!ResponseTypeValues.IsValid(type))
                    return onBadRequest().AddReason($"Invalid response_type: {type}");
            }

            if (types.Contains(ResponseTypeValues.Code, StringComparer.OrdinalIgnoreCase) && tokenEndpoint == null)
                return onBadRequest().AddReason("token_endpoint required for authorization code grant per RFC 6749 §4.1");

            flow.isActive = isActive ?? true;
            flow.createdAt = DateTime.UtcNow;
            flow.updatedAt = DateTime.UtcNow;

            return await flow.StorageCreateAsync(
                created => onCreated(created.Entity),
                onAlreadyExists: () => onAlreadyExists());
        }

        [HttpPatch]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> UpdateAsync(
                [UpdateId] IRef<ClientCredentialFlow> flowRef,
                MutateResource<ClientCredentialFlow> mutateResource,
            ContentTypeResponse<ClientCredentialFlow> onUpdated,
            NotFoundResponse onNotFound)
        {
            return await flowRef.StorageUpdateAsync2(
                flow =>
                {
                    flow.updatedAt = DateTime.UtcNow;
                    var updates = mutateResource(flow);
                    return updates;
                },
                (updatedFlow) => onUpdated(updatedFlow),
                () => onNotFound());
        }

        [HttpDelete]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> DeleteAsync(
                [UpdateId] IRef<ClientCredentialFlow> flowRef,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound)
        {
            return await flowRef.StorageDeleteAsync(
                deleted => onDeleted(),
                () => onNotFound());
        }
    }
}

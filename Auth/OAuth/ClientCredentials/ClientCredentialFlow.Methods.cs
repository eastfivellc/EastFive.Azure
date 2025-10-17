using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using EastFive;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;

namespace EastFive.Azure.OAuth
{
    public partial struct ClientCredentialFlow
    {
        /// <summary>
        /// Creates a new Client Credentials Flow configuration
        /// </summary>
        public static async Task<TResult> CreateAsync<TResult>(
            IRef<ClientCredentialFlow> flowRef,
            string clientId,
            string clientSecret,
            Uri tokenEndpoint,
            string scope,
            string name,
            string description,
            bool isActive,
            Func<ClientCredentialFlow, TResult> onCreated,
            Func<TResult> onAlreadyExists)
        {
            var flow = new ClientCredentialFlow
            {
                @ref = flowRef,
                clientId = clientId,
                clientSecret = clientSecret,
                tokenEndpoint = tokenEndpoint,
                scope = scope,
                name = name,
                description = description,
                isActive = isActive,
                createdAt = DateTime.UtcNow,
                updatedAt = DateTime.UtcNow
            };

            return await flowRef.StorageCreateAsync(
                flow,
                created => onCreated(flow),
                onAlreadyExists: onAlreadyExists);
        }

        /// <summary>
        /// Retrieves a Client Credentials Flow configuration by reference
        /// </summary>
        public static async Task<TResult> GetAsync<TResult>(
            IRef<ClientCredentialFlow> flowRef,
            Func<ClientCredentialFlow, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return await flowRef.StorageGetAsync(onFound, onNotFound);
        }

        /// <summary>
        /// Updates an existing Client Credentials Flow configuration
        /// </summary>
        public static async Task<TResult> UpdateAsync<TResult>(
            IRef<ClientCredentialFlow> flowRef,
            string clientId = null,
            string clientSecret = null,
            Uri tokenEndpoint = null,
            string scope = null,
            string name = null,
            string description = null,
            bool? isActive = null,
            Func<ClientCredentialFlow, TResult> onUpdated = null,
            Func<TResult> onNotFound = null)
        {
            return await flowRef.StorageUpdateAsync(
                async (flow, saveAsync) =>
                {
                    if (clientId is not null)
                        flow.clientId = clientId;
                    if (clientSecret is not null)
                        flow.clientSecret = clientSecret;
                    if (tokenEndpoint is not null)
                        flow.tokenEndpoint = tokenEndpoint;
                    if (scope is not null)
                        flow.scope = scope;
                    if (name is not null)
                        flow.name = name;
                    if (description is not null)
                        flow.description = description;
                    if (isActive.HasValue)
                        flow.isActive = isActive.Value;

                    flow.updatedAt = DateTime.UtcNow;

                    await saveAsync(flow);
                    return onUpdated(flow);
                },
                onNotFound);
        }

        /// <summary>
        /// Deletes a Client Credentials Flow configuration
        /// </summary>
        public static async Task<TResult> DeleteAsync<TResult>(
            IRef<ClientCredentialFlow> flowRef,
            Func<TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            return await flowRef.StorageDeleteAsync(
                deleted => onDeleted(),
                onNotFound);
        }

        /// <summary>
        /// Queries all active Client Credentials Flow configurations
        /// </summary>
        public static IEnumerableAsync<ClientCredentialFlow> GetActive()
        {
            return typeof(ClientCredentialFlow)
                .StorageGetAll()
                .Select(entity => (ClientCredentialFlow)entity)
                .Where(flow => flow.isActive);
        }

        /// <summary>
        /// Queries all Client Credentials Flow configurations
        /// </summary>
        public static IEnumerableAsync<ClientCredentialFlow> GetAll()
        {
            return typeof(ClientCredentialFlow)
                .StorageGetAll()
                .Select(entity => (ClientCredentialFlow)entity);
        }

        /// <summary>
        /// Executes the OAuth 2.0 Client Credentials Flow (RFC 6749 Section 4.4)
        /// to obtain an access token
        /// </summary>
        public async Task<TResult> RequestAccessTokenAsync<TResult>(
            HttpClient httpClient = null,
            Func<AccessTokenResponse, TResult> onSuccess = null,
            Func<OAuthErrorResponse, TResult> onOAuthError = null,
            Func<string, TResult> onFailure = null)
        {
            var client = httpClient ?? new HttpClient();
            var disposeClient = httpClient is null;

            try
            {
                // RFC 6749 Section 4.4.2: Client requests access token
                var requestContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "scope", scope ?? string.Empty }
                });

                var response = await client.PostAsync(tokenEndpoint, requestContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // RFC 6749 Section 5.1: Successful response
                    var tokenResponse = JsonConvert.DeserializeObject<AccessTokenResponse>(responseBody);
                    return onSuccess(tokenResponse);
                }

                // RFC 6749 Section 5.2: Error response
                var errorResponse = JsonConvert.DeserializeObject<OAuthErrorResponse>(responseBody);
                return onOAuthError(errorResponse);
            }
            catch (Exception ex)
            {
                return onFailure($"Token request failed: {ex.Message}");
            }
            finally
            {
                if (disposeClient)
                    client.Dispose();
            }
        }
    }
}

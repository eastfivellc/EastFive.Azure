using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
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

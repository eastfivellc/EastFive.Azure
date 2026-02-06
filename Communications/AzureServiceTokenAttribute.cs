using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.IdentityModel.Tokens.Jwt;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Linq;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Validates ACS Call Automation Bearer tokens for webhook callbacks.
    /// Uses cryptographic signature validation against ACS's published JWKS keys
    /// and validates audience against stored AzureCommunicationService resources.
    /// </summary>
    public class AzureServiceTokenAttribute : Attribute, IValidateHttpRequest
    {
        private const string AcsIssuer = "https://acscallautomation.communication.azure.com";
        private const string AcsOpenIdConfigUrl = 
            "https://acscallautomation.communication.azure.com/calling/.well-known/acsopenidconfiguration";

        // ConfigurationManager handles automatic key refresh and caching
        private static readonly ConfigurationManager<OpenIdConnectConfiguration> AcsConfigManager = 
            new ConfigurationManager<OpenIdConnectConfiguration>(
                AcsOpenIdConfigUrl,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

        // Cache of valid immutableResourceIds (audience claims) - cleared only on app restart
        private static readonly ConcurrentDictionary<string, bool> ValidAudienceCache = 
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public async Task<IHttpResponse> ValidateRequest(
            KeyValuePair<ParameterInfo, object>[] parameterSelection,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            ValidateHttpDelegate boundCallback)
        {
            if (!request.Headers.TryGetValue("Authorization", out var authHeaders))
            {
                return request.CreateResponse(HttpStatusCode.Unauthorized)
                    .AddReason("Missing Authorization header");
            }

            return await authHeaders
                .First(
                    async (authHeader, next) =>
                    {
                        return await await ValidateAcsTokenAsync(
                                authHeader,
                            async () => await boundCallback(parameterSelection, method, httpApp, request),
                            (why) => next());
                    },
                    () =>
                    {
                        return request.CreateResponse(HttpStatusCode.Unauthorized)
                            .AddReason("No valid Authorization header found")
                            .AsTask();
                    });
        }

        private async Task<TResult> ValidateAcsTokenAsync<TResult>(string authHeader,
            Func<TResult> onValidated,
            Func<string, TResult> onInvalidToken)
        {
            try
            {
                if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return onInvalidToken("Authorization header does not contain Bearer token");

                var token = authHeader.Substring("Bearer ".Length).Trim();
                if (string.IsNullOrWhiteSpace(token))
                    return onInvalidToken("Bearer token is empty");
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(token))
                    return onInvalidToken("Token is not a valid JWT");

                // Fetch signing keys from ACS JWKS endpoint (cached by ConfigurationManager)
                var openIdConfig = await AcsConfigManager.GetConfigurationAsync();
                var signingKeys = openIdConfig.SigningKeys;

                // Validate signature and issuer, but not audience (we'll validate against storage)
                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = signingKeys,
                    ValidateAudience = false, // We validate audience against storage separately
                    ValidateIssuer = true,
                    ValidIssuer = AcsIssuer,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                };

                handler.ValidateToken(token, validationParams, out var validatedToken);
                
                // Now validate audience against our stored ACS resources
                var jwtToken = validatedToken as JwtSecurityToken;
                var audience = jwtToken?.Audiences.FirstOrDefault();
                
                if (string.IsNullOrWhiteSpace(audience))
                    return onInvalidToken("Token does not contain audience claim");

                var audienceValid = await ValidateAudienceAgainstStorageAsync(audience);
                if (audienceValid)
                    return onValidated();
                
                return await AzureCommunicationService.DiscoverAsync(
                    onDiscovered: (updatedResources) =>
                    {
                        return updatedResources
                            .Where(acs => string.Equals(acs.immutableResourceId, audience, StringComparison.OrdinalIgnoreCase))
                            .First(
                                (acs, next) =>
                                {
                                        // Found - add to cache
                                        ValidAudienceCache.TryAdd(audience, true);
                                        return onValidated();
                                },
                                () =>
                                {
                                    return onInvalidToken("Audience claim does not match any known ACS resources");
                                });
                    },
                    onFailure: (reason) =>
                    {
                        return onInvalidToken($"Failed to validate audience claim: {reason}");
                    });
            }
            catch (SecurityTokenValidationException ex)
            {
                return onInvalidToken($"Token validation failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                return onInvalidToken($"Token validation exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates the audience claim against stored AzureCommunicationService resources.
        /// Uses in-memory cache (cleared on app restart) for performance.
        /// </summary>
        private static async Task<bool> ValidateAudienceAgainstStorageAsync(string audience)
        {
            // Check cache first
            if (ValidAudienceCache.ContainsKey(audience))
                return true;

            // Query storage for ACS resource with matching immutableResourceId
            return await audience.StorageGetBy(
                    (AzureCommunicationService acs) => acs.immutableResourceId)
                .FirstAsync(
                    acs =>
                    {
                        // Found - add to cache for future requests
                        ValidAudienceCache.TryAdd(audience, true);
                        return true;
                    },
                    () => false);
        }
    }
}

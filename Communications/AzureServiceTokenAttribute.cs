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
using EastFive.Web.Configuration;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Specifies which Azure service issued the token to validate.
    /// </summary>
    public enum AzureServiceTokenIssuer
    {
        /// <summary>
        /// Azure Communication Services Call Automation.
        /// Issuer: https://acscallautomation.communication.azure.com
        /// Audience: ACS immutableResourceId (GUID)
        /// </summary>
        AcsCallAutomation,

        /// <summary>
        /// Azure Event Grid webhook delivery with Azure AD authentication.
        /// Issuer: https://login.microsoftonline.com/{tenant-id}/v2.0
        /// Audience: Application's App ID URI
        /// App ID: 4962773b-9cdb-44cf-a8bf-237846a00ab7
        /// </summary>
        EventGrid,
    }

    /// <summary>
    /// Validates Azure service Bearer tokens for webhook callbacks.
    /// Supports ACS Call Automation and Event Grid Azure AD tokens.
    /// Uses cryptographic signature validation against published JWKS keys.
    /// </summary>
    public class AzureServiceTokenAttribute : Attribute, IValidateHttpRequest
    {
        /// <summary>
        /// The Azure service issuer to validate tokens against.
        /// </summary>
        public AzureServiceTokenIssuer Issuer { get; set; } = AzureServiceTokenIssuer.AcsCallAutomation;

        #region ACS Call Automation

        private const string AcsIssuer = "https://acscallautomation.communication.azure.com";
        private const string AcsOpenIdConfigUrl = 
            "https://acscallautomation.communication.azure.com/calling/.well-known/acsopenidconfiguration";

        private static readonly ConfigurationManager<OpenIdConnectConfiguration> AcsConfigManager = 
            new ConfigurationManager<OpenIdConnectConfiguration>(
                AcsOpenIdConfigUrl,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

        private static readonly ConcurrentDictionary<string, bool> ValidAudienceCache = 
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Event Grid Azure AD

        /// <summary>
        /// Event Grid's well-known Azure AD application ID.
        /// All Event Grid webhook deliveries use this app ID in the token.
        /// </summary>
        private const string EventGridAppId = "4962773b-9cdb-44cf-a8bf-237846a00ab7";

        private const string AzureAdOpenIdConfigUrl = 
            "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";

        private static readonly ConfigurationManager<OpenIdConnectConfiguration> AzureAdConfigManager = 
            new ConfigurationManager<OpenIdConnectConfiguration>(
                AzureAdOpenIdConfigUrl,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

        #endregion

        public async Task<IHttpResponse> ValidateRequest(
            KeyValuePair<ParameterInfo, object>[] parameterSelection,
            MethodInfo method,
            IApplication httpApp,
            IHttpRequest request,
            ValidateHttpDelegate boundCallback)
        {
            // Event Grid subscription validation requests do not include a token.
            // Allow them through so the validation handler can respond with the validation code.
            if (Issuer == AzureServiceTokenIssuer.EventGrid)
            {
                if (!request.Headers.TryGetValue("Authorization", out var authHeadersMaybe) 
                    || !authHeadersMaybe.Any())
                {
                    // Check if this is a subscription validation request by inspecting the body
                    // Event Grid validation events contain "Microsoft.EventGrid.SubscriptionValidationEvent"
                    if (request.Headers.TryGetValue("aeg-event-type", out var aegEventType) 
                        && aegEventType.Any(v => "SubscriptionValidation".Equals(v, StringComparison.OrdinalIgnoreCase)))
                    {
                        return await boundCallback(parameterSelection, method, httpApp, request);
                    }

                    return request.CreateResponse(HttpStatusCode.Unauthorized)
                        .AddReason("Missing Authorization header");
                }
            }

            request.Headers.TryGetValue("Authorization", out var authHeaders);

            return await authHeaders
                .First(
                    async (authHeader, next) =>
                    {
                        return await await ValidateTokenAsync(
                                authHeader,
                            async () =>
                            {
                                return await boundCallback(parameterSelection, method, httpApp, request);
                            },
                            (why) => next());
                    },
                    () =>
                    {
                        return request.CreateResponse(HttpStatusCode.Unauthorized)
                            .AddReason("No valid Authorization header found")
                            .AsTask();
                    });
        }

        private Task<TResult> ValidateTokenAsync<TResult>(string authHeader,
            Func<TResult> onValidated,
            Func<string, TResult> onInvalidToken)
        {
            return Issuer switch
            {
                AzureServiceTokenIssuer.AcsCallAutomation => 
                    ValidateAcsTokenAsync(authHeader, onValidated, onInvalidToken),
                AzureServiceTokenIssuer.EventGrid => 
                    ValidateEventGridTokenAsync(authHeader, onValidated, onInvalidToken),
                _ => Task.FromResult(onInvalidToken($"Unknown issuer: {Issuer}")),
            };
        }

        #region ACS Token Validation

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

                var openIdConfig = await AcsConfigManager.GetConfigurationAsync();
                var signingKeys = openIdConfig.SigningKeys;

                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = signingKeys,
                    ValidateAudience = false,
                    ValidateIssuer = true,
                    ValidIssuer = AcsIssuer,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                };

                handler.ValidateToken(token, validationParams, out var validatedToken);
                
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

        private static async Task<bool> ValidateAudienceAgainstStorageAsync(string audience)
        {
            if (ValidAudienceCache.ContainsKey(audience))
                return true;

            return await audience.StorageGetBy(
                    (AzureCommunicationService acs) => acs.immutableResourceId)
                .FirstAsync(
                    acs =>
                    {
                        ValidAudienceCache.TryAdd(audience, true);
                        return true;
                    },
                    () => false);
        }

        #endregion

        #region Event Grid Token Validation

        private async Task<TResult> ValidateEventGridTokenAsync<TResult>(string authHeader,
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

                // Build expected issuer from configured tenant ID
                return await AppSettings.TenantId.ConfigurationString(
                    async tenantId =>
                    {
                        var expectedIssuer = $"https://login.microsoftonline.com/{tenantId}/v2.0";

                        var openIdConfig = await AzureAdConfigManager.GetConfigurationAsync();
                        var signingKeys = openIdConfig.SigningKeys;

                        // Use the Entra application (client) ID as the expected audience
                        return AppSettings.ClientId.ConfigurationString(
                            expectedAudience =>
                            {
                                var validationParams = new TokenValidationParameters
                                {
                                    ValidateIssuerSigningKey = true,
                                    IssuerSigningKeys = signingKeys,
                                    ValidateAudience = true,
                                    ValidAudience = expectedAudience,
                                    ValidateIssuer = true,
                                    ValidIssuer = expectedIssuer,
                                    ValidateLifetime = true,
                                    ClockSkew = TimeSpan.FromMinutes(2),
                                };

                                handler.ValidateToken(token, validationParams, out var validatedToken);

                                // Validate that the token was issued by Event Grid's well-known app ID
                                var jwtToken = validatedToken as JwtSecurityToken;
                                var appId = jwtToken?.Claims
                                    .FirstOrDefault(c => c.Type == "appid" || c.Type == "azp")?.Value;

                                if (!EventGridAppId.Equals(appId, StringComparison.OrdinalIgnoreCase))
                                    return onInvalidToken(
                                        $"Token app ID '{appId}' does not match Event Grid app ID '{EventGridAppId}'");

                                return onValidated();
                            },
                            why => onInvalidToken($"Event Grid audience not configured: {why}"));
                    },
                    why => onInvalidToken($"Azure tenant ID not configured: {why}").AsTask());
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

        #endregion
    }
}

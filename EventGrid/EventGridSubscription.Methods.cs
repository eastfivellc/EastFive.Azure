#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;

using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.EventGrid;
using Azure.ResourceManager.EventGrid.Models;

using EastFive;
using EastFive.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.ResourceManagement;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Web.Configuration;

namespace EastFive.Azure.EventGrid
{
    public partial struct EventGridSubscription
    {
        #region Azure Operations

        /// <summary>
        /// Creates or updates an Event Grid subscription in Azure and persists the record.
        /// Generic method that works with any Azure resource scope.
        /// </summary>
        /// <param name="scopeResourceId">The ARM resource ID to attach the subscription to (e.g., ACS, Storage, IoT Hub)</param>
        /// <param name="subscriptionName">Unique name for this subscription within the scope</param>
        /// <param name="callbackUri">Webhook endpoint to receive events</param>
        /// <param name="eventTypes">Event types to subscribe to (e.g., "Microsoft.Communication.IncomingCall")</param>
        public static async Task<TResult> EnsureAsync<TResult>(
            string scopeResourceId,
            string subscriptionName,
            Uri callbackUri,
            string[] eventTypes,
            Func<EventGridSubscription, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (string.IsNullOrEmpty(scopeResourceId))
            {
                return onFailure("Scope resource ID is required.");
            }

            if (string.IsNullOrEmpty(subscriptionName))
            {
                return onFailure("Subscription name is required.");
            }

            if (eventTypes == null || eventTypes.Length == 0)
            {
                return onFailure("At least one event type is required.");
            }

            return await EastFive.Azure.AppSettings.AzureClientApplications.Default.CreateClientSecretCredential(
                async credential =>
                {
                    try
                    {
                        var armClient = new ArmClient(credential);

                        var scope = new global::Azure.Core.ResourceIdentifier(scopeResourceId);
                        var eventSubscriptions = armClient.GetEventSubscriptions(scope);

                        var eventSubscriptionData = new EventGridSubscriptionData
                        {
                            Destination = new WebHookEventSubscriptionDestination
                            {
                                Endpoint = callbackUri
                            },
                            Filter = new EventSubscriptionFilter(),
                            EventDeliverySchema = EventDeliverySchema.EventGridSchema
                        };

                        foreach (var eventType in eventTypes)
                        {
                            eventSubscriptionData.Filter.IncludedEventTypes.Add(eventType);
                        }

                        var result = await eventSubscriptions.CreateOrUpdateAsync(
                            global::Azure.WaitUntil.Completed,
                            subscriptionName,
                            eventSubscriptionData);

                        var provisioningState = result.Value.Data.ProvisioningState?.ToString();

                        // Persist to storage
                        var subscription = new EventGridSubscription
                        {
                            eventGridSubscriptionRef = Guid.NewGuid().AsRef<EventGridSubscription>(),
                            subscriptionName = subscriptionName,
                            endpoint = callbackUri.ToString(),
                            scopeResourceId = scopeResourceId,
                            eventTypes = eventTypes,
                            provisioningState = provisioningState,
                            lastSynced = DateTime.UtcNow
                        };

                        // Upsert - find existing by subscription name and scope
                        var existing = await typeof(EventGridSubscription)
                            .StorageGetAll()
                            .CastObjsAs<EventGridSubscription>()
                            .Where(s => s.subscriptionName == subscriptionName 
                                     && s.scopeResourceId == scopeResourceId)
                            .ToArrayAsync();

                        if (existing.Any())
                        {
                            var existingRef = existing.First().eventGridSubscriptionRef;
                            await existingRef.StorageUpdateAsync(
                                async (current, saveAsync) =>
                                {
                                    current.endpoint = callbackUri.ToString();
                                    current.eventTypes = eventTypes;
                                    current.provisioningState = provisioningState;
                                    current.lastSynced = DateTime.UtcNow;
                                    await saveAsync(current);
                                    return current;
                                },
                                () => subscription);
                            subscription.eventGridSubscriptionRef = existingRef;
                        }
                        else
                        {
                            await subscription.StorageCreateAsync(
                                discard => true,
                                () => true);
                        }

                        return onSuccess(subscription);
                    }
                    catch (Exception ex)
                    {
                        return onFailure($"Failed to create Event Grid subscription: {ex.Message}");
                    }
                },
                why => onFailure(why).AsTask());
        }

        /// <summary>
        /// Gets the current subscription status from Azure.
        /// </summary>
        /// <param name="scopeResourceId">The ARM resource ID to query subscriptions from</param>
        /// <param name="subscriptionName">Optional: specific subscription name to find; if null, returns first found</param>
        public static async Task<TResult> RefreshFromAzureAsync<TResult>(
            string scopeResourceId,
            string? subscriptionName,
            Func<EventGridSubscription, TResult> onFound,
            Func<TResult> onNotFound,
            Func<string, TResult> onFailure)
        {
            if (string.IsNullOrEmpty(scopeResourceId))
            {
                return onNotFound();
            }

            return await EastFive.Azure.AppSettings.AzureClientApplications.Default.CreateClientSecretCredential(
                async credential =>
                {
                    try
                    {
                        var armClient = new ArmClient(credential);

                        var scope = new global::Azure.Core.ResourceIdentifier(scopeResourceId);
                        var eventSubscriptions = armClient.GetEventSubscriptions(scope);

                        await foreach (var sub in eventSubscriptions.GetAllAsync())
                        {
                            // If subscriptionName specified, match it; otherwise return first
                            if (subscriptionName == null || sub.Data.Name == subscriptionName)
                            {
                                var destination = sub.Data.Destination as WebHookEventSubscriptionDestination;
                                
                                var subscription = new EventGridSubscription
                                {
                                    eventGridSubscriptionRef = Guid.NewGuid().AsRef<EventGridSubscription>(),
                                    subscriptionName = sub.Data.Name,
                                    endpoint = destination?.Endpoint?.ToString() ?? string.Empty,
                                    scopeResourceId = scopeResourceId,
                                    eventTypes = sub.Data.Filter?.IncludedEventTypes?.ToArray() ?? Array.Empty<string>(),
                                    provisioningState = sub.Data.ProvisioningState?.ToString(),
                                    lastSynced = DateTime.UtcNow
                                };

                                return onFound(subscription);
                            }
                        }

                        return onNotFound();
                    }
                    catch (Exception ex)
                    {
                        return onFailure($"Failed to get subscription: {ex.Message}");
                    }
                },
                why => onFailure(why).AsTask());
        }

        /// <summary>
        /// Lists all Event Grid subscriptions from local storage.
        /// </summary>
        public static IEnumerableAsync<EventGridSubscription> GetAllFromStorageAsync()
        {
            return typeof(EventGridSubscription)
                .StorageGetAll()
                .CastObjsAs<EventGridSubscription>();
        }

        /// <summary>
        /// Finds a subscription by scope and name from local storage.
        /// </summary>
        public static async Task<TResult> FindByNameAsync<TResult>(
            string scopeResourceId,
            string subscriptionName,
            Func<EventGridSubscription, TResult> onFound,
            Func<TResult> onNotFound)
        {
            var matches = await GetAllFromStorageAsync()
                .Where(s => s.subscriptionName == subscriptionName
                         && s.scopeResourceId == scopeResourceId)
                .ToArrayAsync();

            return matches.Any()
                ? onFound(matches.First())
                : onNotFound();
        }

        #endregion
    }
}

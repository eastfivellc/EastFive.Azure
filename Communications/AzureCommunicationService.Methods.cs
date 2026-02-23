#nullable enable

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Communication;

using EastFive;
using EastFive.Azure;
using EastFive.Azure.Communications;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Web.Configuration;
using EastFive.Azure.ResourceManagement;
using EastFive.Azure.EventGrid;
using System.Text.Json;

namespace EastFive.Azure.Communications
{
    public partial struct AzureCommunicationService : IProvideEventSubscription
    {
        #region Constants

        public const string IncomingCallsSubscriptionName = "incoming-calls-webhook";
        public static readonly string[] IncomingCallEventTypes = new[] { "Microsoft.Communication.IncomingCall" };

        public const string RecordingEventsSubscriptionName = "recording-events-webhook";
        public static readonly string[] RecordingEventTypes = new[] {
            "Microsoft.Communication.RecordingFileStatusUpdated",
            "Microsoft.Communication.RecordingStateChanged"
        };

        #endregion

        #region IProvideEventSubscription Implementation

        /// <summary>
        /// Provides the Event Grid subscription configuration for incoming calls.
        /// Requires a callback URI to be provided.
        /// </summary>
        public Task<TResult> ProvideEventSubscriptionAsync<TResult>(
            Uri callbackUri,
            Func<string, string, Uri, string[], TResult> onSubscriptionToEnsure,
            Func<string, TResult> onFailure)
        {
            // Copy instance member to local variable for lambda capture
            var currentResourceId = this.resourceId;
            
            if (string.IsNullOrEmpty(currentResourceId))
            {
                return onFailure("AzureCommunicationService has no resource ID. Call discover first.").AsTask();
            }

            return onSubscriptionToEnsure(
                currentResourceId,
                IncomingCallsSubscriptionName,
                callbackUri,
                IncomingCallEventTypes).AsTask();
        }

        /// <summary>
        /// Provides the Event Grid subscription configuration with async callback.
        /// </summary>
        // public async Task<TResult> ProvideEventSubscriptionAsync<TResult>(
        //     Uri callbackUri,
        //     Func<string, string, Uri, string[], Task<TResult>> onSubscriptionToEnsure,
        //     Func<string, Task<TResult>> onFailure)
        // {
        //     // Copy instance member to local variable for lambda capture
        //     var currentResourceId = this.resourceId;
            
        //     if (string.IsNullOrEmpty(currentResourceId))
        //     {
        //         return await onFailure("AzureCommunicationService has no resource ID. Call discover first.");
        //     }

        //     return await onSubscriptionToEnsure(
        //         currentResourceId,
        //         IncomingCallsSubscriptionName,
        //         callbackUri,
        //         IncomingCallEventTypes);
        // }

        /// <summary>
        /// IProvideEventSubscription implementation - throws because callback URI is required.
        /// Use the overload that accepts a callback URI.
        /// </summary>
        Task<TResult> IProvideEventSubscription.ProvideEventSubscriptionAsync<TResult>(
            Func<string, string, Uri, string[], TResult> onSubscriptionToEnsure,
            Func<string, TResult> onFailure)
        {
            return onFailure(
                "AzureCommunicationService requires a callback URI. " +
                "Use ProvideEventSubscriptionAsync(Uri callbackUri, ...) instead.").AsTask();
        }

        #endregion

        #region Storage Operations

        /// <summary>
        /// Gets all Azure Communication Services from storage.
        /// </summary>
        public static IEnumerableAsync<AzureCommunicationService> GetAllFromStorageAsync()
        {
            return typeof(AzureCommunicationService)
                .StorageGetAll()
                .CastObjsAs<AzureCommunicationService>();
        }

        /// <summary>
        /// Gets the first (typically only) ACS resource from storage.
        /// </summary>
        public static async Task<TResult> GetAsync<TResult>(
            Func<AzureCommunicationService, TResult> onFound,
            Func<TResult> onNotFound)
        {
            var resources = await GetAllFromStorageAsync().ToArrayAsync();
            return resources.Any()
                ? onFound(resources.First())
                : onNotFound();
        }

        #endregion

        #region Azure Discovery

        /// <summary>
        /// Discovers the Azure Communication Services resource by parsing the connection string
        /// and searching Azure Resource Manager.
        /// </summary>
        /// <param name="onDiscovered">Called with (acs, isNew) when the resource is found</param>
        /// <param name="onFailure">Called with error reason on failure</param>
        public static async Task<TResult> DiscoverAsync<TResult>(
            Func<AzureCommunicationService[], TResult> onDiscovered,
            Func<string, TResult> onFailure)
        {
            // Parse resource name from connection string
            return await EastFive.Azure.AppSettings.Communications.Default.CreateResourceManager(
                    EastFive.Azure.AppSettings.AzureClientApplications.Default,
                async (armClient, resourceName) =>
                {
                    var filter = "resourceType eq 'Microsoft.Communication/CommunicationServices'";
                    var acss = await armClient.GetSubscriptions()
                        .SelectMany(sub => sub.GetGenericResourcesAsync(
                            filter: filter))
                        .Where(resource => string.Equals(resource.Data.Name, resourceName, StringComparison.OrdinalIgnoreCase))
                        .ToEnumerableAsync()
                        .Select(
                            async resource =>
                            {
                                // Use CommunicationServiceResource to get the immutableResourceId
                                string? immutableResourceId = null;
                                try
                                {
                                    var acsResource = armClient.GetCommunicationServiceResource(resource.Id);
                                    var acsData = await acsResource.GetAsync();
                                    immutableResourceId = acsData.Value.Data.ImmutableResourceId?.ToString();
                                }
                                catch (Exception)
                                {
                                    // Fall back to null if we can't get the immutableResourceId
                                }

                                var acs = new AzureCommunicationService
                                    {
                                        azureCommunicationServiceRef = Guid.NewGuid().AsRef<AzureCommunicationService>(),
                                        resourceId = resource.Id.ToString(),
                                        immutableResourceId = immutableResourceId,
                                        resourceName = resource.Data.Name,
                                        resourceGroupName = resource.Id.ResourceGroupName ?? string.Empty,
                                        subscriptionId = resource.Id.SubscriptionId ?? string.Empty,
                                        location = resource.Data.Location.ToString(),
                                        provisioningState = resource.Data.ProvisioningState?.ToString(),
                                        lastSynced = DateTime.UtcNow
                                    };
                                
                                
                                return await await acs.StorageCreateAsync(
                                    entity => entity.Entity.AsTask(),
                                    () =>acs.azureCommunicationServiceRef.StorageGetAsync(x => x));
                            })
                        .Await()
                        .ToArrayAsync();
                    
                    return onDiscovered(acss);
                },
                why => onFailure(why).AsTask());
        }

        #endregion

        #region Event Grid Integration

        /// <summary>
        /// Ensures the Event Grid subscription for incoming calls is configured.
        /// </summary>
        /// <param name="callbackUri">The webhook endpoint to receive events</param>
        /// <param name="onSuccess">Called with the created/updated subscription</param>
        /// <param name="onFailure">Called with error reason on failure</param>
        public async Task<TResult> EnsureIncomingCallSubscriptionAsync<TResult>(
            Uri callbackUri,
            Func<EventGridSubscription, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return await await ProvideEventSubscriptionAsync(
                callbackUri,
                (scopeResourceId, subscriptionName, cbUri, eventTypes) =>
                    EventGridSubscription.EnsureAsync(
                        scopeResourceId,
                        subscriptionName,
                        cbUri,
                        eventTypes,
                        subscription => onSuccess(subscription),
                        error => onFailure(error)),
                reason => Task.FromResult(onFailure(reason)));
        }

        /// <summary>
        /// Ensures the Event Grid subscription for recording events is configured.
        /// </summary>
        /// <param name="callbackUri">The webhook endpoint to receive recording events</param>
        /// <param name="onSuccess">Called with the created/updated subscription</param>
        /// <param name="onFailure">Called with error reason on failure</param>
        public async Task<TResult> EnsureRecordingEventsSubscriptionAsync<TResult>(
            Uri callbackUri,
            Func<EventGridSubscription, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return await await ProvideEventSubscriptionAsync(
                callbackUri,
                async (scopeResourceId, subscriptionName, cbUri, eventTypes) =>
                    await EventGridSubscription.EnsureAsync(
                        scopeResourceId,
                        RecordingEventsSubscriptionName,
                        cbUri,
                        RecordingEventTypes,
                        subscription => onSuccess(subscription),
                        error => onFailure(error)),
                reason => Task.FromResult(onFailure(reason)));
        }

        #endregion
    }
}

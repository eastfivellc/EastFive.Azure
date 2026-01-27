#nullable enable

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Azure.Identity;
using Azure.ResourceManager;

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
        public async Task<TResult> ProvideEventSubscriptionAsync<TResult>(
            Uri callbackUri,
            Func<string, string, Uri, string[], Task<TResult>> onSubscriptionToEnsure,
            Func<string, Task<TResult>> onFailure)
        {
            // Copy instance member to local variable for lambda capture
            var currentResourceId = this.resourceId;
            
            if (string.IsNullOrEmpty(currentResourceId))
            {
                return await onFailure("AzureCommunicationService has no resource ID. Call discover first.");
            }

            return await onSubscriptionToEnsure(
                currentResourceId,
                IncomingCallsSubscriptionName,
                callbackUri,
                IncomingCallEventTypes);
        }

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
            Func<AzureCommunicationService, bool, TResult> onDiscovered,
            Func<string, TResult> onFailure)
        {
            // First check if already stored
            var existing = await GetAllFromStorageAsync().ToArrayAsync();
            if (existing.Any())
            {
                return onDiscovered(existing.First(), false);
            }

            // Parse resource name from connection string
            return await EastFive.Azure.AppSettings.Communications.Default.CreateResourceManager(
                    EastFive.Azure.AppSettings.AzureClientApplications.Default,
                async (armClient, resourceName) =>
                {
                    await foreach (var subscription in armClient.GetSubscriptions())
                    {
                        var filter = "resourceType eq 'Microsoft.Communication/CommunicationServices'";
                        
                        await foreach (var resource in subscription.GetGenericResourcesAsync(filter: filter))
                        {
                            if (string.Equals(resource.Data.Name, resourceName, StringComparison.OrdinalIgnoreCase))
                            {
                                var acs = new AzureCommunicationService
                                {
                                    azureCommunicationServiceRef = Guid.NewGuid().AsRef<AzureCommunicationService>(),
                                    resourceId = resource.Id.ToString(),
                                    resourceName = resource.Data.Name,
                                    resourceGroupName = resource.Id.ResourceGroupName ?? string.Empty,
                                    subscriptionId = subscription.Id.SubscriptionId ?? string.Empty,
                                    location = resource.Data.Location.ToString(),
                                    provisioningState = resource.Data.ProvisioningState?.ToString(),
                                    lastSynced = DateTime.UtcNow
                                };
                                
                                await acs.StorageCreateAsync(
                                    discard => true,
                                    () => true);
                                
                                return onDiscovered(acs, true);
                            }
                        }
                    }
                    
                    return onFailure(
                        $"Could not find ACS resource named '{resourceName}' in any accessible subscription. " +
                        "Ensure the Service Principal has Reader access to the subscription containing the ACS resource.");
                },
                why => onFailure(why).AsTask());
        }

        /// <summary>
        /// Discovers the Azure Communication Services resource with async callbacks.
        /// </summary>
        public static async Task<TResult> DiscoverAsync<TResult>(
            Func<AzureCommunicationService, bool, Task<TResult>> onDiscovered,
            Func<string, TResult> onFailure)
        {
            var innerResult = await DiscoverAsync<Task<TResult>>(
                (acs, isNew) => onDiscovered(acs, isNew),
                reason => Task.FromResult(onFailure(reason)));
            return await innerResult;
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
            return await ProvideEventSubscriptionAsync(
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

        #endregion



        /// <summary>
        /// Parses Event Grid events and dispatches to IHandleIncomingCall attributes on the application.
        /// </summary>
        private static async Task DispatchIncomingCallEventsAsync(
            string eventBody,
            EastFive.Api.HttpApplication httpApp)
        {
            using var jsonDoc = JsonDocument.Parse(eventBody);
            var root = jsonDoc.RootElement;

            // Event Grid sends events as an array
            if (root.ValueKind != JsonValueKind.Array)
                return;

            foreach (var eventElement in root.EnumerateArray())
            {
                if (!eventElement.TryGetProperty("eventType", out var eventTypeElement))
                    continue;

                var eventType = eventTypeElement.GetString();
                if (eventType != "Microsoft.Communication.IncomingCall")
                    continue;

                if (!eventElement.TryGetProperty("data", out var dataElement))
                    continue;

                await DispatchSingleIncomingCallAsync(dataElement, httpApp);
            }
        }

        /// <summary>
        /// Dispatches a single incoming call to IHandleIncomingCall attributes on the application.
        /// Uses the Attribute Interface pattern to discover and chain handlers.
        /// </summary>
        private static async Task DispatchSingleIncomingCallAsync(
            JsonElement data,
            EastFive.Api.HttpApplication httpApp)
        {
            // Extract required fields from incoming call event
            if (!data.TryGetProperty("incomingCallContext", out var incomingCallContextElement))
                return;

            var incomingCallContext = incomingCallContextElement.GetString();
            if (string.IsNullOrEmpty(incomingCallContext))
                return;

            // Get the phone numbers involved
            string? toPhoneNumber = null;
            string? fromPhoneNumber = null;

            if (data.TryGetProperty("to", out var toElement) &&
                toElement.TryGetProperty("phoneNumber", out var toPhoneElement) &&
                toPhoneElement.TryGetProperty("value", out var toValueElement))
            {
                toPhoneNumber = toValueElement.GetString();
            }

            if (data.TryGetProperty("from", out var fromElement) &&
                fromElement.TryGetProperty("phoneNumber", out var fromPhoneElement) &&
                fromPhoneElement.TryGetProperty("value", out var fromValueElement))
            {
                fromPhoneNumber = fromValueElement.GetString();
            }

            if (string.IsNullOrEmpty(toPhoneNumber) || string.IsNullOrEmpty(fromPhoneNumber))
                return;

            // Look up the AcsPhoneNumber for this call
            var acsPhoneNumber = await toPhoneNumber
                .StorageGetBy((AcsPhoneNumber p) => p.phoneNumber)
                .FirstAsync(
                    phone => (AcsPhoneNumber?)phone,
                    () => (AcsPhoneNumber?)null);

            // Discover IHandleIncomingCall attributes on the application and invoke them
            // Uses the same Aggregate pattern as IHandleRoutes/IHandleMethods
            await httpApp.GetType()
                .GetAttributesInterface<IHandleIncomingCall>(inherit: true, multiple: true)
                .Aggregate<IHandleIncomingCall, IncomingCallHandlingDelegate<bool>>(
                    // Innermost handler - default behavior when no handler processes the call
                    (ctx, to, from, phone) => Task.FromResult(false),
                    // Wrap each handler around the previous callback
                    (continuation, handler) =>
                        (ctx, to, from, phone) =>
                            handler.HandleIncomingCallAsync(ctx, to, from, phone, continuation))
                .Invoke(incomingCallContext, toPhoneNumber, fromPhoneNumber, acsPhoneNumber);
        }
    }
}

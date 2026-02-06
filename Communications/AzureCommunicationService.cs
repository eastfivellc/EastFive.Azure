#nullable enable

using System;
using System.Text.Json;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Azure.EventGrid;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

using Newtonsoft.Json;

namespace EastFive.Azure.Communications
{
    #region IHandleIncomingEvent Interface

    /// <summary>
    /// Priority constants for event handlers.
    /// Higher values = earlier execution. Negative values = handler is skipped.
    /// </summary>
    public static class EventHandlerPriority
    {
        /// <summary>
        /// Priority for subscription validation handler. Runs first.
        /// </summary>
        public const int Validation = int.MaxValue;

        /// <summary>
        /// Default priority for application handlers.
        /// </summary>
        public const int Default = 0;

        /// <summary>
        /// Return this to indicate the handler should not process this event.
        /// </summary>
        public const int DoNotHandle = -1;
    }

    /// <summary>
    /// Interface for attributes that handle incoming events from Azure Event Grid.
    /// Apply attributes implementing this interface to the application class (e.g., Startup)
    /// to receive incoming events dispatched by webhook endpoints.
    /// 
    /// Follows the Attribute Interface pattern used by IHandleRoutes, IHandleMethods, etc.
    /// </summary>
    public interface IHandleIncomingEvent
    {
        /// <summary>
        /// Determines whether this handler should process the event and at what priority.
        /// </summary>
        /// <param name="eventType">The event type string (e.g., "Microsoft.Communication.IncomingCall")</param>
        /// <param name="eventData">The parsed "data" property from the event</param>
        /// <param name="fullEvent">The full event JSON element</param>
        /// <returns>
        /// Negative value: handler does not handle this event (skipped).
        /// Zero or positive: handler processes this event. Higher values execute earlier.
        /// </returns>
        int DoesHandleEvent(string eventType, JsonElement eventData, JsonElement fullEvent);

        /// <summary>
        /// Handles an incoming event.
        /// Return directly to short-circuit (stop processing this and remaining events).
        /// Call continueExecution() to pass to next handler, then next event in batch.
        /// </summary>
        /// <param name="eventType">The event type string</param>
        /// <param name="eventData">The parsed "data" property from the event</param>
        /// <param name="fullEvent">The full event JSON element</param>
        /// <param name="request">The HTTP request, used to create responses via request.CreateResponse()</param>
        /// <param name="httpApp">The application instance</param>
        /// <param name="continueExecution">Call to pass to next handler/event; returns final response</param>
        Task<IHttpResponse> HandleEventAsync(
                string eventType,
                JsonElement eventData,
                JsonElement fullEvent,
                IHttpRequest request,
                EastFive.Api.HttpApplication httpApp,
            NoContentResponse onProcessed,
            BadRequestResponse onBadRequest,
            GeneralFailureResponse onFailure,
            Func<Task<IHttpResponse>> continueExecution);
    }

    #endregion

    /// <summary>
    /// Represents an Azure Communication Services resource.
    /// Auto-discovered from Azure and used for phone number management and Event Grid subscriptions.
    /// </summary>
    [StorageTable]
    public partial struct AzureCommunicationService : IReferenceable
    {
        #region Base Properties

        [JsonIgnore]
        public Guid id => azureCommunicationServiceRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 1)]
        public IRef<AzureCommunicationService> azureCommunicationServiceRef;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [LastModified]
        [JsonIgnore]
        public DateTime lastModified;

        #endregion

        #region Properties

        /// <summary>
        /// The full ARM resource ID.
        /// Example: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Communication/CommunicationServices/{name}
        /// </summary>
        public const string ResourceIdPropertyName = "resource_id";
        [ApiProperty(PropertyName = ResourceIdPropertyName)]
        [JsonProperty(PropertyName = ResourceIdPropertyName)]
        [Storage(Name = ResourceIdPropertyName)]
        [StorageQuery]
        public string resourceId;

        /// <summary>
        /// The immutable resource ID (GUID) assigned when the ACS resource was created.
        /// Used for validating ACS Call Automation webhook JWT tokens (audience claim).
        /// </summary>
        public const string ImmutableResourceIdPropertyName = "immutable_resource_id";
        [ApiProperty(PropertyName = ImmutableResourceIdPropertyName)]
        [JsonProperty(PropertyName = ImmutableResourceIdPropertyName)]
        [Storage(Name = ImmutableResourceIdPropertyName)]
        [StringLookupHashXX32(Characters = 1)]
        public string? immutableResourceId;

        /// <summary>
        /// The name of the Communication Services resource.
        /// </summary>
        public const string ResourceNamePropertyName = "resource_name";
        [ApiProperty(PropertyName = ResourceNamePropertyName)]
        [JsonProperty(PropertyName = ResourceNamePropertyName)]
        [Storage(Name = ResourceNamePropertyName)]
        public string resourceName;

        /// <summary>
        /// The resource group containing this resource.
        /// </summary>
        public const string ResourceGroupNamePropertyName = "resource_group_name";
        [ApiProperty(PropertyName = ResourceGroupNamePropertyName)]
        [JsonProperty(PropertyName = ResourceGroupNamePropertyName)]
        [Storage(Name = ResourceGroupNamePropertyName)]
        public string resourceGroupName;

        /// <summary>
        /// The Azure subscription ID containing this resource.
        /// </summary>
        public const string SubscriptionIdPropertyName = "subscription_id";
        [ApiProperty(PropertyName = SubscriptionIdPropertyName)]
        [JsonProperty(PropertyName = SubscriptionIdPropertyName)]
        [Storage(Name = SubscriptionIdPropertyName)]
        public string subscriptionId;

        /// <summary>
        /// The Azure location/region of the resource.
        /// </summary>
        public const string LocationPropertyName = "location";
        [ApiProperty(PropertyName = LocationPropertyName)]
        [JsonProperty(PropertyName = LocationPropertyName)]
        [Storage(Name = LocationPropertyName)]
        public string location;

        /// <summary>
        /// The provisioning state of the resource.
        /// </summary>
        public const string ProvisioningStatePropertyName = "provisioning_state";
        [ApiProperty(PropertyName = ProvisioningStatePropertyName)]
        [JsonProperty(PropertyName = ProvisioningStatePropertyName)]
        [Storage(Name = ProvisioningStatePropertyName)]
        public string? provisioningState;

        /// <summary>
        /// When this resource was last synced from Azure.
        /// </summary>
        public const string LastSyncedPropertyName = "last_synced";
        [ApiProperty(PropertyName = LastSyncedPropertyName)]
        [JsonProperty(PropertyName = LastSyncedPropertyName)]
        [Storage(Name = LastSyncedPropertyName)]
        public DateTime? lastSynced;

        #endregion
    }
}

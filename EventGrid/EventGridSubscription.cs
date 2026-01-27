#nullable enable

using System;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

using Newtonsoft.Json;

namespace EastFive.Azure.EventGrid
{
    /// <summary>
    /// Interface for Azure resources that can provide Event Grid subscription configuration.
    /// Implement this interface to enable automatic Event Grid subscription setup.
    /// </summary>
    public interface IProvideEventSubscription
    {
        /// <summary>
        /// Provides the configuration needed to create an Event Grid subscription.
        /// </summary>
        /// <typeparam name="TResult">The result type</typeparam>
        /// <param name="onSubscriptionToEnsure">Called with (scopeResourceId, subscriptionName, callbackUri, eventTypes) when configuration is available</param>
        /// <param name="onFailure">Called with error reason when the subscription cannot be configured</param>
        Task<TResult> ProvideEventSubscriptionAsync<TResult>(
            Func<string, string, Uri, string[], TResult> onSubscriptionToEnsure,
            Func<string, TResult> onFailure);
    }

    /// <summary>
    /// Represents a generic Event Grid subscription for any Azure resource.
    /// Can be used for ACS incoming calls, Storage events, IoT Hub, etc.
    /// </summary>
    [StorageTable]
    public partial struct EventGridSubscription : IReferenceable
    {
        #region Base Properties

        [JsonIgnore]
        public Guid id => eventGridSubscriptionRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 1)]
        public IRef<EventGridSubscription> eventGridSubscriptionRef;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [LastModified]
        [JsonIgnore]
        public DateTime lastModified;

        #endregion

        #region Properties

        /// <summary>
        /// The name of the Event Grid subscription in Azure.
        /// </summary>
        public const string SubscriptionNamePropertyName = "subscription_name";
        [ApiProperty(PropertyName = SubscriptionNamePropertyName)]
        [JsonProperty(PropertyName = SubscriptionNamePropertyName)]
        [Storage(Name = SubscriptionNamePropertyName)]
        public string subscriptionName;

        /// <summary>
        /// The webhook endpoint URL that receives events.
        /// </summary>
        public const string EndpointPropertyName = "endpoint";
        [ApiProperty(PropertyName = EndpointPropertyName)]
        [JsonProperty(PropertyName = EndpointPropertyName)]
        [Storage(Name = EndpointPropertyName)]
        public string endpoint;

        /// <summary>
        /// The Azure resource ID (scope) this subscription is attached to.
        /// Examples: ACS resource, Storage account, IoT Hub, etc.
        /// </summary>
        public const string ScopeResourceIdPropertyName = "scope_resource_id";
        [ApiProperty(PropertyName = ScopeResourceIdPropertyName)]
        [JsonProperty(PropertyName = ScopeResourceIdPropertyName)]
        [Storage(Name = ScopeResourceIdPropertyName)]
        public string scopeResourceId;

        /// <summary>
        /// The event types this subscription listens for.
        /// </summary>
        public const string EventTypesPropertyName = "event_types";
        [ApiProperty(PropertyName = EventTypesPropertyName)]
        [JsonProperty(PropertyName = EventTypesPropertyName)]
        [Storage(Name = EventTypesPropertyName)]
        public string[] eventTypes;

        /// <summary>
        /// The provisioning state from Azure (e.g., "Succeeded", "Failed").
        /// </summary>
        public const string ProvisioningStatePropertyName = "provisioning_state";
        [ApiProperty(PropertyName = ProvisioningStatePropertyName)]
        [JsonProperty(PropertyName = ProvisioningStatePropertyName)]
        [Storage(Name = ProvisioningStatePropertyName)]
        public string? provisioningState;

        /// <summary>
        /// When the subscription was created or last updated.
        /// </summary>
        public const string LastSyncedPropertyName = "last_synced";
        [ApiProperty(PropertyName = LastSyncedPropertyName)]
        [JsonProperty(PropertyName = LastSyncedPropertyName)]
        [Storage(Name = LastSyncedPropertyName)]
        public DateTime? lastSynced;

        #endregion
    }
}

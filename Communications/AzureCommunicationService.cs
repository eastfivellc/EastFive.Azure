#nullable enable

using System;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

using Newtonsoft.Json;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Delegate for incoming call handling continuation.
    /// Used to chain multiple IHandleIncomingCall handlers together.
    /// </summary>
    public delegate Task<TResult> IncomingCallHandlingDelegate<TResult>(
        string incomingCallContext,
        string toPhoneNumber,
        string fromPhoneNumber,
        AcsPhoneNumber? acsPhoneNumber);

    /// <summary>
    /// Interface for attributes that handle incoming calls from Azure Communication Services.
    /// Apply attributes implementing this interface to the application class (e.g., Startup)
    /// to receive incoming call events dispatched by AzureCommunicationService.
    /// 
    /// Follows the Attribute Interface pattern used by IHandleRoutes, IHandleMethods, etc.
    /// </summary>
    public interface IHandleIncomingCall
    {
        /// <summary>
        /// Handles an incoming call event.
        /// </summary>
        /// <typeparam name="TResult">The result type</typeparam>
        /// <param name="incomingCallContext">The opaque context string required to answer the call</param>
        /// <param name="toPhoneNumber">The ACS phone number that was called (E.164 format)</param>
        /// <param name="fromPhoneNumber">The caller's phone number (E.164 format)</param>
        /// <param name="acsPhoneNumber">The AcsPhoneNumber entity for the called number, if found</param>
        /// <param name="continueExecution">Call this delegate to pass to the next handler in the chain</param>
        Task<TResult> HandleIncomingCallAsync<TResult>(
            string incomingCallContext,
            string toPhoneNumber,
            string fromPhoneNumber,
            AcsPhoneNumber? acsPhoneNumber,
            IncomingCallHandlingDelegate<TResult> continueExecution);
    }

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

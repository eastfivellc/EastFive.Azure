#nullable enable

using System;
using Azure.Communication.PhoneNumbers;
using EastFive;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

using Newtonsoft.Json;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Represents a phone number from Azure Communication Services.
    /// These are synced from the Azure portal via the refresh endpoint.
    /// </summary>
    [StorageTable]
    public partial struct AcsPhoneNumber : IReferenceable
    {
        #region Base Properties

        [JsonIgnore]
        public Guid id => acsPhoneNumberRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 1)]
        public IRef<AcsPhoneNumber> acsPhoneNumberRef;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [LastModified]
        [JsonIgnore]
        public DateTime lastModified;

        #endregion

        #region Properties

        /// <summary>
        /// The phone number in E.164 format (e.g., +15551234567).
        /// This is the primary identifier from Azure Communication Services.
        /// </summary>
        public const string PhoneNumberPropertyName = "phone_number";
        [ApiProperty(PropertyName = PhoneNumberPropertyName)]
        [JsonProperty(PropertyName = PhoneNumberPropertyName)]
        [Storage(Name = PhoneNumberPropertyName)]
        [StorageQuery]
        [StringLookupHashXX32(Characters = 1)]
        public string phoneNumber;

        /// <summary>
        /// A friendly display name for this phone number.
        /// </summary>
        public const string DisplayNamePropertyName = "display_name";
        [ApiProperty(PropertyName = DisplayNamePropertyName)]
        [JsonProperty(PropertyName = DisplayNamePropertyName)]
        [Storage(Name = DisplayNamePropertyName)]
        public string displayName;

        /// <summary>
        /// The Azure resource ID for this phone number.
        /// Used for correlation with the Azure portal.
        /// </summary>
        public const string AcsResourceIdPropertyName = "acs_resource_id";
        [ApiProperty(PropertyName = AcsResourceIdPropertyName)]
        [JsonProperty(PropertyName = AcsResourceIdPropertyName)]
        [Storage(Name = AcsResourceIdPropertyName)]
        public string acsResourceId;

        /// <summary>
        /// Reference to the Azure Communication Services resource this phone number belongs to.
        /// </summary>
        public const string AzureCommunicationServiceRefPropertyName = "azure_communication_service";
        [JsonIgnore]
        [Storage(Name = AzureCommunicationServiceRefPropertyName)]
        public IRef<AzureCommunicationService>? azureCommunicationServiceRef;

        /// <summary>
        /// The capabilities of this phone number (e.g., "inbound", "outbound", "sms").
        /// </summary>
        public const string CapabilitiesPropertyName = "capabilities";
        [ApiProperty(PropertyName = CapabilitiesPropertyName)]
        [JsonProperty(PropertyName = CapabilitiesPropertyName)]
        [Storage(Name = CapabilitiesPropertyName)]
        public PhoneNumberCapability[] capabilities;

        /// <summary>
        /// The type of phone number (e.g., "geographic", "tollFree").
        /// </summary>
        public const string PhoneNumberTypePropertyName = "phone_number_type";
        [ApiProperty(PropertyName = PhoneNumberTypePropertyName)]
        [JsonProperty(PropertyName = PhoneNumberTypePropertyName)]
        [Storage(Name = PhoneNumberTypePropertyName)]
        public string phoneNumberType;

        /// <summary>
        /// The country code for this phone number (e.g., "US").
        /// </summary>
        public const string CountryCodePropertyName = "country_code";
        [ApiProperty(PropertyName = CountryCodePropertyName)]
        [JsonProperty(PropertyName = CountryCodePropertyName)]
        [Storage(Name = CountryCodePropertyName)]
        public string countryCode;

        #endregion
    }
}

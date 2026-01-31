#nullable enable

using System;
using EastFive;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

using Newtonsoft.Json;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Represents a generic phone call orchestrated via Azure Communication Services.
    /// Uses a participants array for flexible call workflows instead of hardcoded roles.
    /// </summary>
    [StorageTable]
    public partial struct AcsPhoneCall : IReferenceable
    {
        #region Base Properties

        [JsonIgnore]
        public Guid id => acsPhoneCallRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 2)]
        public IRef<AcsPhoneCall> acsPhoneCallRef;

        [ETag]
        [JsonIgnore]
        public string eTag;

        [LastModified]
        [JsonIgnore]
        public DateTime lastModified;

        #endregion

        #region Call Properties

        /// <summary>
        /// Reference to the ACS phone number hosting this call (conference line).
        /// </summary>
        public const string ConferencePhoneNumberPropertyName = "conference_phone_number";
        [ApiProperty(PropertyName = ConferencePhoneNumberPropertyName)]
        [JsonProperty(PropertyName = ConferencePhoneNumberPropertyName)]
        [Storage]
        public IRef<AcsPhoneNumber> conferencePhoneNumber;

        /// <summary>
        /// The ACS call connection ID. Used to correlate webhook events.
        /// </summary>
        public const string CallConnectionIdPropertyName = "call_connection_id";
        [ApiProperty(PropertyName = CallConnectionIdPropertyName)]
        [JsonProperty(PropertyName = CallConnectionIdPropertyName)]
        [Storage(Name = CallConnectionIdPropertyName)]
        [StringLookupHashXX32(Characters = 2, IgnoreNullWhiteSpace = true)]
        public string callConnectionId;

        /// <summary>
        /// The ACS server call ID. Used for recording and other server-side operations.
        /// </summary>
        public const string ServerCallIdPropertyName = "server_call_id";
        [ApiProperty(PropertyName = ServerCallIdPropertyName)]
        [JsonProperty(PropertyName = ServerCallIdPropertyName)]
        [Storage(Name = ServerCallIdPropertyName)]
        public string serverCallId;

        /// <summary>
        /// The correlation ID for tracking the call across systems.
        /// </summary>
        public const string CorrelationIdPropertyName = "correlation_id";
        [ApiProperty(PropertyName = CorrelationIdPropertyName)]
        [JsonProperty(PropertyName = CorrelationIdPropertyName)]
        [Storage(Name = CorrelationIdPropertyName)]
        public string correlationId;

        #endregion

        #region Participants

        /// <summary>
        /// Array of participants in this call.
        /// Participants are processed in order based on their order property and direction.
        /// </summary>
        public const string ParticipantsPropertyName = "participants";
        [ApiProperty(PropertyName = ParticipantsPropertyName)]
        [JsonProperty(PropertyName = ParticipantsPropertyName)]
        [Storage]
        public AcsCallParticipant[] participants;

        #endregion

        #region Recording Properties

        /// <summary>
        /// The ACS recording ID for this call.
        /// </summary>
        public const string RecordingIdPropertyName = "recording_id";
        [ApiProperty(PropertyName = RecordingIdPropertyName)]
        [JsonProperty(PropertyName = RecordingIdPropertyName)]
        [Storage(Name = RecordingIdPropertyName)]
        public string recordingId;

        /// <summary>
        /// The current state of the recording.
        /// </summary>
        public const string RecordingStatePropertyName = "recording_state";
        [ApiProperty(PropertyName = RecordingStatePropertyName)]
        [JsonProperty(PropertyName = RecordingStatePropertyName)]
        [Storage(Name = RecordingStatePropertyName)]
        public RecordingState recordingState;

        /// <summary>
        /// The URL where the recording content can be downloaded.
        /// </summary>
        public const string RecordingContentLocationPropertyName = "recording_content_location";
        [ApiProperty(PropertyName = RecordingContentLocationPropertyName)]
        [JsonProperty(PropertyName = RecordingContentLocationPropertyName)]
        [Storage(Name = RecordingContentLocationPropertyName)]
        public string recordingContentLocation;

        /// <summary>
        /// The URL where the recording metadata can be retrieved.
        /// </summary>
        public const string RecordingMetadataLocationPropertyName = "recording_metadata_location";
        [ApiProperty(PropertyName = RecordingMetadataLocationPropertyName)]
        [JsonProperty(PropertyName = RecordingMetadataLocationPropertyName)]
        [Storage(Name = RecordingMetadataLocationPropertyName)]
        public string recordingMetadataLocation;

        #endregion

        #region Error Tracking

        /// <summary>
        /// Error message if the call or any participant failed.
        /// </summary>
        public const string ErrorMessagePropertyName = "error_message";
        [ApiProperty(PropertyName = ErrorMessagePropertyName)]
        [JsonProperty(PropertyName = ErrorMessagePropertyName)]
        [Storage(Name = ErrorMessagePropertyName)]
        public string errorMessage;

        #endregion
    }
}

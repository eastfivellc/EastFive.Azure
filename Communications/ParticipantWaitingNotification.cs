#nullable enable

using System;
using Newtonsoft.Json;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Notification sent when AcsPhoneCall is waiting for an inbound participant to call.
    /// </summary>
    public struct ParticipantWaitingNotification
    {
        /// <summary>
        /// Event type identifier. Always "ParticipantWaiting".
        /// </summary>
        [JsonProperty(PropertyName = "eventType")]
        public string eventType;

        /// <summary>
        /// Timestamp when notification was generated (UTC).
        /// </summary>
        [JsonProperty(PropertyName = "timestamp")]
        public DateTime timestamp;

        /// <summary>
        /// ID of the AcsPhoneCall waiting for the participant.
        /// </summary>
        [JsonProperty(PropertyName = "acsPhoneCallId")]
        public Guid acsPhoneCallId;

        /// <summary>
        /// Information about the participant that needs to call in.
        /// </summary>
        [JsonProperty(PropertyName = "participant")]
        public ParticipantInfo participant;

        /// <summary>
        /// The phone number the participant should call (E.164 format).
        /// </summary>
        [JsonProperty(PropertyName = "conferencePhoneNumber")]
        public string conferencePhoneNumber;

        /// <summary>
        /// Correlation ID for tracking across systems.
        /// </summary>
        [JsonProperty(PropertyName = "correlationId")]
        public string correlationId;

        /// <summary>
        /// Information about a participant waiting to call in.
        /// </summary>
        public struct ParticipantInfo
        {
            /// <summary>
            /// Participant's phone number (E.164 format).
            /// </summary>
            [JsonProperty(PropertyName = "phoneNumber")]
            public string phoneNumber;

            /// <summary>
            /// Participant's label (e.g., "customer", "agent").
            /// </summary>
            [JsonProperty(PropertyName = "label")]
            public string label;

            /// <summary>
            /// Participant's order in the call sequence.
            /// </summary>
            [JsonProperty(PropertyName = "order")]
            public int order;
        }
    }
}

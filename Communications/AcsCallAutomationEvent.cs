#nullable enable

using System;
using System.Runtime.Serialization;

using Newtonsoft.Json;

using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Api;
using EastFive.Persistence.Azure.StorageTables;
using DocumentFormat.OpenXml.Features;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Unified struct representing all Azure Communication Services call automation webhook events.
    /// Different event types populate different subsets of these fields.
    /// </summary>
    [StorageTable]
    public struct AcsCallAutomationEvent: IReferenceable
    {
        [JsonIgnore]
        public Guid id => @ref.id;
        
        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 1)]
        public IRef<AcsCallAutomationEvent> @ref;
        
        public const string AcsPhoneCallPropertyName = "acs_phone_call";
        [ApiProperty(PropertyName = AcsPhoneCallPropertyName)]
        [JsonProperty(PropertyName = AcsPhoneCallPropertyName)]
        [IdHashXX32Lookup(Characters = 4)]
        [Storage]
        public IRef<AcsPhoneCall> acsPhoneCall;

        [JsonIgnore]
        [Storage]
        public EventType eventType;

        #region Common Fields (All Events)

        /// <summary>
        /// Unique identifier for the call connection (required in all events).
        /// Used to correlate events with AcsPhoneCall records.
        /// </summary>
        [JsonProperty(PropertyName = "callConnectionId")]
        [Storage]
        public string callConnectionId;

        /// <summary>
        /// ACS API version (e.g., "2024-09-15").
        /// Present in all events.
        /// </summary>
        [JsonProperty(PropertyName = "version")]
        [Storage]
        public string version;

        /// <summary>
        /// Server-side call identifier (base64 encoded).
        /// Used for recording and server-side operations.
        /// Present in most events.
        /// </summary>
        [JsonProperty(PropertyName = "serverCallId")]
        [Storage]
        public string? serverCallId;

        /// <summary>
        /// Correlation ID for end-to-end tracking across systems.
        /// Present in most events.
        /// </summary>
        [JsonProperty(PropertyName = "correlationId")]
        [Storage]
        public string? correlationId;

        /// <summary>
        /// Operation context for correlating operations with events.
        /// Set via AddParticipantOptions.OperationContext or CreateCallOptions.OperationContext.
        /// Used to match events to specific participants via participant.id.ToString().
        /// Present in most events.
        /// </summary>
        [JsonProperty(PropertyName = "operationContext")]
        [Storage]
        public string? operationContext;

        public Guid OperationContextGuid =>
            Guid.TryParse(operationContext, out var operationContextGuid)
                ? operationContextGuid
                : Guid.Empty;

        #endregion

        #region Event-Specific Fields

        /// <summary>
        /// Invitation ID returned by AddParticipantAsync.
        /// CRITICAL: Used to match AddParticipant events to the correct AcsCallParticipant.
        /// Only present in: AddParticipantSucceeded, AddParticipantFailed
        /// </summary>
        [JsonProperty(PropertyName = "invitationId")]
        [Storage]
        public string? invitationId;

        /// <summary>
        /// Result information with status codes and diagnostic messages.
        /// Contains error details for failure events or completion status for success events.
        /// Present in: CallDisconnected, AddParticipantFailed, CallTransferFailed,
        ///            RecognizeFailed, RecognizeCanceled, PlayFailed, PlayCanceled, ParticipantsUpdated
        /// </summary>
        [JsonProperty(PropertyName = "resultInformation")]
        [Storage]
        public ResultInformation? resultInformation;

        /// <summary>
        /// Array of participants currently in the call with their status.
        /// Each participant includes identifier, mute status, and hold status.
        /// Only present in: ParticipantsUpdated
        /// </summary>
        [JsonProperty(PropertyName = "participants")]
        [Storage]
        public AcsEventParticipant[]? participants;

        /// <summary>
        /// Sequence number for tracking event order.
        /// Only present in: ParticipantsUpdated
        /// </summary>
        [JsonProperty(PropertyName = "sequenceNumber")]
        [Storage]
        public int? sequenceNumber;

        #endregion

        public TResult CheckValid<TResult>(EventType eventType,
            Func<TResult> onValid, Func<string, TResult> onInvalid)
        {
            if (callConnectionId.IsNullOrWhiteSpace())
                return onInvalid("Missing callConnectionId.");

            if(eventType != EventType.ParticipantsUpdated)
            {
                if (OperationContextGuid == Guid.Empty)
                    return onInvalid("Invalid operationContext.");
            }
            return onValid();
        }
    }

    public enum EventType
    {
        [EnumMember(Value = "Microsoft.Communication.CallConnected")]
        CallConnected,

        [EnumMember(Value = "Microsoft.Communication.CallDisconnected")]
        CallDisconnected,

        [EnumMember(Value = "Microsoft.Communication.AddParticipantSucceeded")]
        AddParticipantSucceeded,

        [EnumMember(Value = "Microsoft.Communication.AddParticipantFailed")]
        AddParticipantFailed,

        [EnumMember(Value = "Microsoft.Communication.CallTransferAccepted")]
        CallTransferAccepted,

        [EnumMember(Value = "Microsoft.Communication.CallTransferFailed")]
        CallTransferFailed,

        [EnumMember(Value = "Microsoft.Communication.RecognizeCompleted")]
        RecognizeCompleted,

        [EnumMember(Value = "Microsoft.Communication.RecognizeFailed")]
        RecognizeFailed,

        [EnumMember(Value = "Microsoft.Communication.RecognizeCanceled")]
        RecognizeCanceled,

        [EnumMember(Value = "Microsoft.Communication.PlayCompleted")]
        PlayCompleted,

        [EnumMember(Value = "Microsoft.Communication.PlayFailed")]
        PlayFailed,

        [EnumMember(Value = "Microsoft.Communication.PlayCanceled")]
        PlayCanceled,

        [EnumMember(Value = "Microsoft.Communication.ParticipantsUpdated")]
        ParticipantsUpdated,

        [EnumMember(Value = "Microsoft.Communication.AnswerFailed")]
        AnswerFailed,

        [EnumMember(Value = "Microsoft.Communication.MoveParticipantSucceeded")]
        MoveParticipantSucceeded,

        [EnumMember(Value = "Microsoft.Communication.MoveParticipantFailed")]
        MoveParticipantFailed,
    }

    /// <summary>
    /// Result information for ACS call automation events.
    /// Contains status codes and diagnostic messages for event outcomes.
    /// </summary>
    public struct ResultInformation
    {
        /// <summary>
        /// Primary result code (e.g., 200 for success, 4xx/5xx for errors).
        /// </summary>
        [JsonProperty(PropertyName = "code")]
        [Storage]
        public int code;

        /// <summary>
        /// Sub-code providing additional detail.
        /// Examples: 560000 for normal call end, specific error codes for failures.
        /// </summary>
        [JsonProperty(PropertyName = "subCode")]
        [Storage]
        public int subCode;

        /// <summary>
        /// Human-readable diagnostic message.
        /// Examples: "The conversation has ended.", error descriptions.
        /// </summary>
        [JsonProperty(PropertyName = "message")]
        [Storage]
        public string message;
    }

    /// <summary>
    /// Represents a participant in an ACS event (not to be confused with AcsCallParticipant).
    /// This is the participant structure returned by Azure in webhook events.
    /// </summary>
    public struct AcsEventParticipant
    {
        /// <summary>
        /// The participant's identifier (phone number, communication user, etc.).
        /// </summary>
        [JsonProperty(PropertyName = "identifier")]
        [Storage]
        public ParticipantIdentifier identifier;

        /// <summary>
        /// Whether the participant is currently muted.
        /// </summary>
        [JsonProperty(PropertyName = "isMuted")]
        [Storage]
        public bool isMuted;

        /// <summary>
        /// Whether the participant is currently on hold.
        /// </summary>
        [JsonProperty(PropertyName = "isOnHold")]
        [Storage]
        public bool isOnHold;
    }

    /// <summary>
    /// Identifier for a participant in an ACS call.
    /// Can represent different types: phone number, communication user, etc.
    /// </summary>
    public struct ParticipantIdentifier
    {
        /// <summary>
        /// Raw identifier string (e.g., "4:+18083478896" for phone, "8:acs:..." for communication user).
        /// </summary>
        [JsonProperty(PropertyName = "rawId")]
        [Storage]
        public string rawId;

        /// <summary>
        /// Type of identifier: "phoneNumber", "communicationUser", etc.
        /// </summary>
        [JsonProperty(PropertyName = "kind")]
        [Storage]
        public string kind;

        /// <summary>
        /// Phone number details if kind is "phoneNumber".
        /// </summary>
        [JsonProperty(PropertyName = "phoneNumber")]
        [Storage]
        public EventPhoneNumberIdentifier? phoneNumber;

        /// <summary>
        /// Communication user details if kind is "communicationUser".
        /// </summary>
        [JsonProperty(PropertyName = "communicationUser")]
        [Storage]
        public EventCommunicationUserIdentifier? communicationUser;
    }

    /// <summary>
    /// Phone number identifier details from ACS events.
    /// </summary>
    public struct EventPhoneNumberIdentifier
    {
        /// <summary>
        /// Phone number in E.164 format (e.g., "+18083478896").
        /// </summary>
        [JsonProperty(PropertyName = "value")]
        [Storage]
        public string value;
    }

    /// <summary>
    /// Communication user identifier details from ACS events.
    /// </summary>
    public struct EventCommunicationUserIdentifier
    {
        /// <summary>
        /// Communication user ID (e.g., "8:acs:6d3c12b6-42ae-4fb2-bc1e-b2bc34608aac_24f65776-b888-4965-8071-812743e702fe").
        /// </summary>
        [JsonProperty(PropertyName = "id")]
        [Storage]
        public string id;
    }
}

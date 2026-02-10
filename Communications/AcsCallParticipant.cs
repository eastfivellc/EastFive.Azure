#nullable enable

using System;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Represents a participant in an ACS phone call.
    /// Participants can be inbound (awaited) or outbound (dialed) and are processed in order.
    /// </summary>
    public struct AcsCallParticipant
    {
        [Storage]
        public Guid id;

        /// <summary>
        /// Phone number in E.164 format (e.g., +15551234567).
        /// </summary>
        [Storage]
        public string phoneNumber;

        /// <summary>
        /// Direction: Outbound (system calls) or Inbound (system waits for call).
        /// </summary>
        [Storage]
        public CallDirection direction;

        /// <summary>
        /// Current status of this participant in the call.
        /// </summary>
        [Storage]
        public ParticipantStatus status;

        /// <summary>
        /// ACS invitation ID for matching events to participants.
        /// Populated when AddParticipantAsync is called.
        /// </summary>
        [Storage]
        public string invitationId;

        /// <summary>
        /// Order in call sequence (0-based).
        /// Lower numbers are processed first.
        /// </summary>
        [Storage]
        public int order;

        /// <summary>
        /// Label for identifying participant (e.g., "agent", "customer", "support").
        /// </summary>
        [Storage]
        public string label;

        /// <summary>
        /// If true, participant will be muted when they connect.
        /// </summary>
        [Storage]
        public bool muteOnConnect;

        [Storage]
        public bool isRequired;

        /// <summary>
        /// Store this so that when the incoming call arrives we can use it on pickup
        /// </summary>
        [Storage]
        public string incomingCallContext;
    }
}

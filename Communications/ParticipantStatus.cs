#nullable enable

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Status of a participant in a call.
    /// </summary>
    public enum ParticipantStatus
    {
        /// <summary>Participant not yet processed.</summary>
        None,

        /// <summary>Invitation sent, waiting for answer.</summary>
        Inviting,

        BlockedOnNextParticipantAdded,

        Joining,

        /// <summary>
        /// Participant has been notified to call into the conference bridge but the call has not been received.
        /// </summary>
        Notified,

        /// <summary>Participant is connected to the call.</summary>
        Connected,

        /// <summary>Participant has disconnected.</summary>
        Disconnected,

        Removed,

        /// <summary>Failed to add participant.</summary>
        Failed,
    }
}

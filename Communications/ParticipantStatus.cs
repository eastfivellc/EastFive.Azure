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

        BlockedOnNextOutbound,

        Joining,

        /// <summary>Participant is connected to the call.</summary>
        Connected,

        /// <summary>Participant has disconnected.</summary>
        Disconnected,

        /// <summary>Failed to add participant.</summary>
        Failed,
    }
}

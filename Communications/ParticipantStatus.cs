#nullable enable

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Status of a participant in a call.
    /// </summary>
    public enum ParticipantStatus
    {
        /// <summary>Participant not yet processed.</summary>
        None = 0,

        /// <summary>Invitation sent, waiting for answer.</summary>
        Inviting = 1,

        /// <summary>Participant is connected to the call.</summary>
        Connected = 2,

        /// <summary>Participant has disconnected.</summary>
        Disconnected = 3,

        /// <summary>Failed to add participant.</summary>
        Failed = 4,
    }
}

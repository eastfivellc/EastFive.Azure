#nullable enable

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Direction of a phone call relative to the system.
    /// </summary>
    public enum CallDirection
    {
        /// <summary>Outbound call initiated by the system (system calls out).</summary>
        Outbound = 0,

        /// <summary>Inbound call received by the system (system receives call).</summary>
        Inbound = 1,

        /// <summary>Inbound call that doesn't get answered until the next call picks up.</summary>
        InboundBlockedOnNextOutbound = 2,
    }
}

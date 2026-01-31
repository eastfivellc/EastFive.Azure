#nullable enable

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// State of call recording.
    /// </summary>
    public enum RecordingState
    {
        /// <summary>No recording.</summary>
        None = 0,

        /// <summary>Recording is starting.</summary>
        Starting = 1,

        /// <summary>Recording is active.</summary>
        Active = 2,

        /// <summary>Recording is paused.</summary>
        Paused = 3,

        /// <summary>Recording has stopped.</summary>
        Stopped = 4,

        /// <summary>Recording failed.</summary>
        Failed = 5,
    }
}

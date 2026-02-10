#nullable enable

using System;
using System.Threading.Tasks;

using EastFive.Api;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Interface for attributes that handle all ACS call processing:
    /// event dispatch, call sequencing, and incoming call handling.
    /// Apply attributes implementing this interface to the application class (e.g., Startup).
    /// 
    /// Follows the Attribute Interface pattern used by IHandleIncomingEvent, IHandleRoutes, etc.
    /// </summary>
    public interface IProcessCallEvent
    {
        /// <summary>
        /// Processes an ACS call automation event for the given phone call.
        /// </summary>
        Task<TResult> ProcessCallEventAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallAutomationEvent callAutomationEvent,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<TResult> onIgnored,
            Func<string, TResult> onFailure);

        /// <summary>
        /// Processes the call sequence by identifying the next participant to handle
        /// and invoking the appropriate processing method.
        /// </summary>
        Task<TResult> ProcessCallSequenceAsync<TResult>(
                AcsPhoneCall phoneCall,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure);

        /// <summary>
        /// Handles an incoming call from a participant expected to join the conference.
        /// </summary>
        Task<TResult> HandleParticipantCallingAsync<TResult>(
                AcsPhoneCall phoneCall,
                string incomingCallContext,
                string toPhoneNumber,
                string fromPhoneNumber,
                string correlationId,
                AcsPhoneNumber acsPhoneNumber,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure);
    }
}

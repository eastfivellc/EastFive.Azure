#nullable enable

using System;
using System.Threading.Tasks;

using EastFive.Api;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Interface for attributes that identify and process participants in an ACS phone call.
    /// Apply attributes implementing this interface to the application class (e.g., Startup)
    /// to handle participant processing during call sequences.
    /// 
    /// Follows the Attribute Interface pattern used by IHandleIncomingEvent, IHandleRoutes, etc.
    /// </summary>
    public interface IProcessParticipant
    {
        /// <summary>
        /// Identifies which participant (if any) this handler wants to process from the phone call.
        /// </summary>
        /// <param name="phoneCall">The current phone call with its participants array</param>
        /// <returns>
        /// The participant to process, or null if this handler does not handle any participant in the call.
        /// </returns>
        AcsCallParticipant? IdentifyParticipantToProcess(AcsPhoneCall phoneCall);

        /// <summary>
        /// Processes the identified participant.
        /// Called only when IdentifyParticipantToProcess returned a non-null participant.
        /// </summary>
        /// <param name="phoneCall">The current phone call</param>
        /// <param name="participant">The participant identified by IdentifyParticipantToProcess</param>
        /// <param name="conferencePhoneNumber">The conference phone number for the call</param>
        /// <param name="request">The HTTP request context</param>
        /// <param name="httpApp">The application instance for attribute discovery and context</param>
        /// <param name="onProcessed">Callback when participant is successfully processed</param>
        /// <param name="onFailure">Callback when processing fails</param>
        Task<TResult> ProcessParticipantAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallParticipant participant,
                AcsPhoneNumber conferencePhoneNumber,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure);
    }
}

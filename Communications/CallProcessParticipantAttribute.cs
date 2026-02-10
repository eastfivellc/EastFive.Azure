#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Extensions;
using EastFive.Linq;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Handles call-level participant processing for ACS phone calls:
    /// 1. Outbound participants with status == None — initiates an outbound call to add them.
    /// 2. Blocked inbound participants (BlockedOnNextOutbound) — answers them once all
    ///    outbound participants have reached a terminal state (Connected/Disconnected/Failed).
    /// 
    /// Outbound participants are prioritized so they connect before blocked inbound
    /// participants are answered.
    /// 
    /// Apply to the application class (e.g., Startup) to register as a participant processor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class CallProcessParticipantAttribute : Attribute, IProcessParticipant
    {
        /// <summary>
        /// Identifies the next participant to process, with outbound calls taking priority
        /// over answering blocked inbound calls.
        /// </summary>
        public AcsCallParticipant? IdentifyParticipantToProcess(AcsPhoneCall phoneCall)
        {
            // Priority 1: Next outbound participant that hasn't been called yet
            var nextOutbound = phoneCall.participants
                .Where(p => p.status == ParticipantStatus.None)
                .Where(p => p.direction == CallDirection.Outbound)
                .OrderBy(p => p.order)
                .Select(p => (AcsCallParticipant?)p)
                .FirstOrDefault();

            if (nextOutbound.HasValue)
                return nextOutbound;

            // Priority 2: Blocked inbound participant ready to be answered
            // Only when no outbound participants are still in-progress
            var hasUnfinishedOutbound = phoneCall.participants
                .Any(p => p.direction == CallDirection.Outbound
                       && p.status != ParticipantStatus.Connected
                       && p.status != ParticipantStatus.Disconnected
                       && p.status != ParticipantStatus.Failed);

            if (hasUnfinishedOutbound)
                return null;

            return phoneCall.participants
                .Where(p => p.status == ParticipantStatus.BlockedOnNextOutbound)
                .OrderBy(p => p.order)
                .Select(p => (AcsCallParticipant?)p)
                .FirstOrDefault();
        }

        /// <summary>
        /// Processes the identified participant:
        /// - Outbound + None → initiates an outbound call via CallParticipantToAddThemToCallAsync
        /// - BlockedOnNextOutbound → answers the waiting inbound call via AnswerAndUpdateCall
        /// </summary>
        public Task<TResult> ProcessParticipantAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallParticipant participant,
                AcsPhoneNumber conferencePhoneNumber,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            if (participant.direction == CallDirection.Outbound
                && participant.status == ParticipantStatus.None)
            {
                return phoneCall.CallParticipantToAddThemToCallAsync(
                        conferencePhoneNumber,
                        participant,
                        request,
                        httpApp,
                    onAdded: onProcessed,
                    onFailure: onFailure);
            }

            // BlockedOnNextOutbound — answer the waiting inbound call
            return phoneCall.AnswerAndUpdateCall(
                    participant,
                    participant.incomingCallContext,
                    participant.phoneNumber,
                    conferencePhoneNumber,
                    request,
                onAnswered: onProcessed,
                onFailure: onFailure);
        }
    }
}

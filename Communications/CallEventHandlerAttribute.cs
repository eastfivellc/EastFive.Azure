#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;

using Azure.Communication;
using Azure.Communication.CallAutomation;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Collections;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Handles ACS call automation events by dispatching to the appropriate handler method,
    /// and provides default participant processing for outbound calls and blocked inbound calls.
    /// Apply to the application class (e.g., Startup) to enable call event and participant processing.
    /// 
    /// Override IdentifyParticipantToProcess and ProcessParticipantAsync to customize participant handling.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class CallEventHandlerAttribute : Attribute, IProcessCallEvent
    {
        public async Task<TResult> ProcessCallEventAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallAutomationEvent callAutomationEvent,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<TResult> onIgnored,
            Func<string, TResult> onFailure)
        {
            switch (callAutomationEvent.eventType)
            {
                case EventType.CallConnected:
                    return await CallConnectedAsync<TResult>(phoneCall,
                            callAutomationEvent.OperationContextGuid,
                            callAutomationEvent.callConnectionId,
                            callAutomationEvent.serverCallId,
                            request, httpApp,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.CallDisconnected:
                    return await HandleCallDisconnectedAsync(phoneCall, callAutomationEvent,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.AddParticipantSucceeded:
                    return await HandleParticipantAddedAsync(phoneCall,
                            callAutomationEvent.OperationContextGuid, request, httpApp,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.AddParticipantFailed:
                    return await HandleAddParticipantFailedAsync(phoneCall, callAutomationEvent,
                        (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.CallTransferAccepted:
                case EventType.CallTransferFailed:
                    // Can be handled if transfer functionality is needed
                    return onIgnored();

                case EventType.RecognizeCompleted:
                case EventType.RecognizeFailed:
                case EventType.RecognizeCanceled:
                    // Can be handled for DTMF/speech recognition
                    return onIgnored();

                case EventType.PlayCompleted:
                case EventType.PlayFailed:
                case EventType.PlayCanceled:
                    // Can be handled for media playback
                    return onIgnored();

                case EventType.ParticipantsUpdated:
                    return await HandleParticipantsUpdatedAsync(phoneCall, callAutomationEvent,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.MoveParticipantSucceeded:
                    return await HandleMoveParticipantSucceededAsync(phoneCall,
                            callAutomationEvent.OperationContextGuid, request, httpApp,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.MoveParticipantFailed:
                    return await HandleMoveParticipantFailedAsync(phoneCall, callAutomationEvent,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.AnswerFailed:
                    return await HandleAddParticipantFailedAsync(phoneCall, callAutomationEvent,
                            (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                            onFailure: (reason) => onFailure(reason));
            }
            return onIgnored();
        }

        #region Event Handlers

        private async Task<TResult> CallConnectedAsync<TResult>(
                AcsPhoneCall phoneCall,
                Guid operationContextGuid,
                string eventCallConnectionId,
                string? serverCallIdStringMaybe,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            // Check if this is an inbound call joining an existing conference
            // If so, we need to move the participant from their answered call to the conference
            var isInboundJoiningConference = phoneCall.callConnectionId.HasBlackSpace() &&
                                              eventCallConnectionId != phoneCall.callConnectionId;

            if (isInboundJoiningConference)
            {
                return await MoveParticipantToConferenceAsync(phoneCall,
                        operationContextGuid, eventCallConnectionId,
                    onMoved: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                    onFailure: (reason) => onFailure(reason));
            }

            return await await phoneCall.acsPhoneCallRef.StorageUpdateAsyncAsync2(
                async (current, saveAsync) =>
                {
                    if(serverCallIdStringMaybe is not null)
                        current.serverCallId = serverCallIdStringMaybe;

                    current.TryUpdatingParticipant(operationContextGuid,
                        participantToUpdate =>
                        {
                            participantToUpdate.status = ParticipantStatus.Connected;
                            return participantToUpdate;
                        },
                        out var participant);

                    return await saveAsync(current);
                },
                async (updatedPhoneCall) =>
                {
                    // Start recording
                    return await await updatedPhoneCall.StartRecordingAsync(
                        onRecordingStarted: (phoneCallWithRecording) =>
                        {
                            return ProcessCallSequenceAsync(phoneCallWithRecording,
                                    request, httpApp,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        },
                        onRecordingFailure: (reason, phoneCallWithFailedRecordingInfo) =>
                        {
                            return ProcessCallSequenceAsync(phoneCallWithFailedRecordingInfo,
                                    request, httpApp,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        },
                        onFailure: (reason) =>
                        {
                            return ProcessCallSequenceAsync(updatedPhoneCall,
                                    request, httpApp,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        });
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted.").AsTask());
        }

        private async Task<TResult> HandleParticipantsUpdatedAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallAutomationEvent callAutomationEvent,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var currentParticipantIds = callAutomationEvent.participants
                .Select(p => p.identifier.rawId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var disonnectedParticipants = phoneCall.participants
                .Where(p => p.status == ParticipantStatus.Connected)
                .Where(
                    p =>
                    {
                        return callAutomationEvent.participants
                            .Where(
                                participant =>
                                {
                                    var isMatch = IsParticipantMatch(p, participant);
                                    return isMatch;
                                })
                            .None();
                    })
                .ToArray();

            return await disonnectedParticipants
                .Aggregate(
                    phoneCall.AsTask(),
                    async (currentPhoneCallTask, participant) =>
                    {
                        var currentPhoneCall = await currentPhoneCallTask;
                        // Handle the disconnected participant
                        var nextPhoneCall = await HandleParticipantDisconnectedAsync(currentPhoneCall, participant,
                            onProcessed: updatedPhoneCall => updatedPhoneCall,
                            onFailure: why => currentPhoneCall);
                        return nextPhoneCall;
                    },
                    async phoneCallTask => onProcessed(await phoneCallTask));

            bool IsParticipantMatch(AcsCallParticipant callParticipant, AcsEventParticipant eventParticipant)
            {
                if (callParticipant.direction == CallDirection.Outbound || callParticipant.direction == CallDirection.Inbound)
                {
                    return eventParticipant.identifier.rawId == $"4:{callParticipant.phoneNumber}";
                }
                return eventParticipant.identifier.rawId == callParticipant.phoneNumber;
            }
        }

        private async Task<TResult> HandleParticipantDisconnectedAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallParticipant participant,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            return await phoneCall.acsPhoneCallRef.StorageUpdateAsync(
                async (current, saveAsync) =>
                {
                    current.participants = current.participants
                        .Select(p =>
                        {
                            if (p.id == participant.id)
                            {
                                p.status = ParticipantStatus.Disconnected;
                            }
                            return p;
                        })
                        .ToArray();

                    await saveAsync(current);
                    return onProcessed(current);
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted."));
        }

        private async Task<TResult> HandleCallDisconnectedAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallAutomationEvent eventData,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            return await phoneCall.acsPhoneCallRef.StorageUpdateAsync(
                async (current, saveAsync) =>
                {
                    if(eventData.callConnectionId != current.callConnectionId)
                    {
                        // This is a disconnection from an inbound call that was merged into  the conference call
                        return onProcessed(current);
                    }

                    current.participants = current.participants
                        .Select(
                            (participant) =>
                            {
                                participant.status = ParticipantStatus.Disconnected;
                                return participant;
                            })
                        .ToArray();

                    await saveAsync(current);
                    return onProcessed(current);
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted."));
        }

        private async Task<TResult> HandleParticipantAddedAsync<TResult>(
                AcsPhoneCall phoneCall,
                Guid operationContextGuid,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            return await await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                (current) =>
                {
                    current.TryUpdatingParticipant(operationContextGuid,
                        participantToUpdate =>
                        {
                            participantToUpdate.status = ParticipantStatus.Connected;
                            return participantToUpdate;
                        },
                        out var participantUpdated);
                    return current;
                },
                async (updatedPhoneCall) =>
                {
                    var mutedParticipants = await updatedPhoneCall.participants
                        .Where(participant => participant.id == operationContextGuid)
                        .Where(participant => participant.muteOnConnect)
                        .Select(
                            async participant =>
                            {
                                await updatedPhoneCall.MuteParticipantAsync(participant.phoneNumber);
                                return participant;
                            })
                        .AsyncEnumerable()
                        .ToArrayAsync();


                    return await await phoneCall.conferencePhoneNumber
                        .StorageGetAsync(
                            async (conferencePhoneNumber) =>
                            {
                                return await updatedPhoneCall.participants
                                    .Where(participant => participant.status == ParticipantStatus.BlockedOnNextOutbound)
                                    .Aggregate(
                                        updatedPhoneCall.AsTask(),
                                        async (phoneCallCurrentTask, participantToUnblock) =>
                                        {
                                            var phoneCallCurrent = await phoneCallCurrentTask;
                                            return await phoneCall.AnswerAndUpdateCall(participantToUnblock,
                                                            participantToUnblock.incomingCallContext, participantToUnblock.phoneNumber,
                                                            conferencePhoneNumber,
                                                            request,
                                                        onAnswered:(updatedPhoneCall) =>
                                                        {
                                                            return updatedPhoneCall;
                                                        },
                                                        (reason) =>
                                                        {
                                                            return phoneCallCurrent;
                                                        });
                                        },
                                        async (phoneCallPostUnblockingTask) =>
                                        {
                                            var phoneCallPostUnblocking = await phoneCallPostUnblockingTask;
                                            return await ProcessCallSequenceAsync(phoneCallPostUnblocking,
                                                    request, httpApp,
                                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                                onFailure: (reason) => onFailure(reason));

                                        });
                            },
                            () => onFailure("Conference phone number not found.").AsTask());
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted.").AsTask());
        }

        private async Task<TResult> HandleAddParticipantFailedAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallAutomationEvent eventData,
            Func<AcsPhoneCall, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var participantId = eventData.OperationContextGuid;
            return await phoneCall.acsPhoneCallRef.StorageUpdateAsync(
                async (current, saveAsync) =>
                {
                    if (eventData.resultInformation.HasValue)
                        current.errorMessage = eventData.resultInformation.Value.message;
                    if (!current.TryUpdatingParticipant(participantId,
                        participantToUpdate =>
                        {
                            participantToUpdate.status = ParticipantStatus.Failed;
                            return participantToUpdate;
                        },
                        out var participant))
                    {
                        await saveAsync(current);
                        return onFailure("Missing invitationId in AddParticipantFailed event.");
                    }
                    await saveAsync(current);
                    return onSuccess(current);
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted."));
        }

        /// <summary>
        /// Handles the MoveParticipantSucceeded event when a participant is successfully
        /// moved from an answered inbound call to the existing conference.
        /// </summary>
        private async Task<TResult> HandleMoveParticipantSucceededAsync<TResult>(
                AcsPhoneCall phoneCall,
                Guid operationContextGuid,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            return await await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                (current) =>
                {
                    current.TryUpdatingParticipant(operationContextGuid,
                        participantToUpdate =>
                        {
                            participantToUpdate.status = ParticipantStatus.Connected;
                            return participantToUpdate;
                        },
                        out var participantUpdated);
                    return current;
                },
                async (updatedPhoneCall) =>
                {
                    // Mute participant if configured
                    var mutedParticipants = await updatedPhoneCall.participants
                        .Where(participant => participant.id == operationContextGuid)
                        .Where(participant => participant.muteOnConnect)
                        .Select(
                            async participant =>
                            {
                                await updatedPhoneCall.MuteParticipantAsync(participant.phoneNumber);
                                return participant;
                            })
                        .AsyncEnumerable()
                        .ToArrayAsync();

                    // Continue processing the call sequence
                    return await ProcessCallSequenceAsync(updatedPhoneCall,
                            request, httpApp,
                        onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                        onFailure: (reason) => onFailure(reason));
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted.").AsTask());
        }

        /// <summary>
        /// Handles the MoveParticipantFailed event when moving a participant to the conference fails.
        /// </summary>
        private async Task<TResult> HandleMoveParticipantFailedAsync<TResult>(
                AcsPhoneCall phoneCall,
                AcsCallAutomationEvent eventData,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var participantId = eventData.OperationContextGuid;
            return await phoneCall.acsPhoneCallRef.StorageUpdateAsync(
                async (current, saveAsync) =>
                {
                    if (eventData.resultInformation.HasValue)
                        current.errorMessage = eventData.resultInformation.Value.message;

                    current.TryUpdatingParticipant(participantId,
                        participantToUpdate =>
                        {
                            participantToUpdate.status = ParticipantStatus.Failed;
                            return participantToUpdate;
                        },
                        out var participant);

                    await saveAsync(current);
                    return onProcessed(current);
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted."));
        }

        #endregion

        #region Call Orchestration

        /// <summary>
        /// Moves a participant from their answered inbound call to the existing conference.
        /// Called when CallConnected fires for an inbound call while a conference is active.
        /// </summary>
        private async Task<TResult> MoveParticipantToConferenceAsync<TResult>(
                AcsPhoneCall phoneCall,
                Guid operationContextGuid, string fromCallConnectionId,
            Func<AcsPhoneCall, TResult> onMoved,
            Func<string, TResult> onFailure)
        {
            // Find the participant by operationContext (participant.id)
            var participantMaybe = phoneCall.participants
                .Where(p => p.id == operationContextGuid)
                .FirstOrDefault();
            
            if (participantMaybe.phoneNumber.IsNullOrWhiteSpace())
                return onFailure($"Participant {operationContextGuid} not found for move operation.");
            
            return await EastFive.Azure.AppSettings.Communications.Default
                .CreateAutomationClient(
                    async client =>
                    {
                        try
                        {
                            var conferenceCallConnection = client.GetCallConnection(phoneCall.callConnectionId);
                            var participantToMove = new PhoneNumberIdentifier(participantMaybe.phoneNumber);
                            
                            var moveOptions = new MoveParticipantsOptions(
                                targetParticipants: new CommunicationIdentifier[] { participantToMove },
                                fromCall: fromCallConnectionId)
                            {
                                OperationContext = operationContextGuid.ToString(),
                            };
                            
                            // Move participant from the answered call to the conference
                            // This will generate MoveParticipantSucceeded/Failed events
                            await conferenceCallConnection.MoveParticipantsAsync(moveOptions);
                            
                            // Update status to indicate move is in progress
                            return await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                                (current) =>
                                {
                                    current.TryUpdatingParticipant(operationContextGuid,
                                        participantToUpdate =>
                                        {
                                            participantToUpdate.status = ParticipantStatus.Joining;
                                            return participantToUpdate;
                                        },
                                        out var participant);
                                    return current;
                                },
                                (updatedPhoneCall) => onMoved(updatedPhoneCall),
                                onNotFound: () => onFailure("AcsPhoneCall deleted."));
                        }
                        catch (Exception ex)
                        {
                            return onFailure($"Failed to move participant: {ex.Message}");
                        }
                    },
                    why => onFailure(why).AsTask());
        }

        /// <summary>
        /// Processes the call sequence by identifying the next participant to handle
        /// and invoking the appropriate processing method via virtual methods.
        /// </summary>
        public async Task<TResult> ProcessCallSequenceAsync<TResult>(
                AcsPhoneCall phoneCall,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            return await await phoneCall.conferencePhoneNumber.StorageGetAsync(
                async conferencePhoneNumber =>
                {
                    var participant = IdentifyParticipantToProcess(phoneCall);
                    if (!participant.HasValue)
                        return onProcessed(phoneCall);

                    return await ProcessParticipantAsync(
                            phoneCall,
                            participant.Value,
                            conferencePhoneNumber,
                            request,
                            httpApp,
                        onProcessed: onProcessed,
                        onFailure: onFailure);
                },
                () => onFailure("Conference phone number not found.").AsTask());
        }

        /// <summary>
        /// Handles an incoming call from a participant expected to join the conference.
        /// </summary>
        public async Task<TResult> HandleParticipantCallingAsync<TResult>(
                AcsPhoneCall phoneCall,
                string incomingCallContext, string toPhoneNumber, string fromPhoneNumber, string correlationId,
                AcsPhoneNumber acsPhoneNumber,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            if (string.IsNullOrEmpty(incomingCallContext))
                return onFailure("Missing context for incoming call.");
            
            return await phoneCall.participants
                .Where((participant) => participant.phoneNumber == fromPhoneNumber)
                .First(
                    async (participant, next) =>
                    {
                        if(participant.direction == CallDirection.InboundBlockedOnNextOutbound)
                        {
                            return await await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                                (current) =>
                                {
                                    current.participants = current.participants
                                        .UpdateWhere(
                                            (p) => p.id == participant.id,
                                            (p) =>
                                            {
                                                p.status = ParticipantStatus.BlockedOnNextOutbound;
                                                p.incomingCallContext = incomingCallContext;
                                                return p;
                                            })
                                        .ToArray();
                                    return current;
                                },
                                async (current) =>
                                {
                                    // Since we are not answering the call until the next item is processed,
                                    // there will not be a next event to move the processing forward
                                    // so go ahead and process the next call item
                                    return await ProcessCallSequenceAsync(current,
                                            request, httpApp,
                                        onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                        onFailure: (reason) => onFailure(reason));
                                },
                                onNotFound:() => onFailure("AcsPhoneCall deleted.").AsTask());
                        }

                        return await phoneCall.AnswerAndUpdateCall<TResult>(
                                participant,
                                incomingCallContext, fromPhoneNumber,
                                acsPhoneNumber,
                                request,
                            onAnswered: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                            onFailure: (reason) => onFailure(reason));
                    },
                    () =>
                    {
                        return onFailure("Participant not found for ParticipantCalling event.").AsTask();
                    });
        }

        #endregion

        #region Participant Processing

        /// <summary>
        /// Identifies the next participant to process, with outbound calls taking priority
        /// over answering blocked inbound calls.
        /// Override to customize participant identification logic.
        /// </summary>
        public virtual AcsCallParticipant? IdentifyParticipantToProcess(AcsPhoneCall phoneCall)
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
        /// Override to customize participant processing behavior.
        /// </summary>
        public virtual Task<TResult> ProcessParticipantAsync<TResult>(
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

        #endregion
    }
}

#nullable enable

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Azure;
using Azure.Communication;
using Azure.Communication.CallAutomation;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Collections;
using EastFive.Collections.Generic;
using EastFive.Api.Serialization.Json;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Communications
{
    public partial struct AcsPhoneCall
    {
        #region Event Processing

        /// <summary>
        /// Processes ACS call automation events.
        /// Generic event handler that updates participants array based on events.
        /// </summary>
        public async Task<TResult> ProcessCallEventAsync<TResult>(
                AcsCallAutomationEvent callAutomationEvent,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<TResult> onIgnored,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            switch (callAutomationEvent.eventType)
            {
                case EventType.CallConnected:
                    return await phoneCall.CallConnectedAsync<TResult>(
                            callAutomationEvent.OperationContextGuid, 
                            callAutomationEvent.callConnectionId,
                            callAutomationEvent.serverCallId, 
                            request, httpApp,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case  EventType.CallDisconnected:
                    return await phoneCall.HandleCallDisconnectedAsync(callAutomationEvent,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.AddParticipantSucceeded:
                    return await phoneCall.HandleParticipantAddedAsync(
                            callAutomationEvent.OperationContextGuid, request, httpApp,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.AddParticipantFailed:
                        return await phoneCall.HandleAddParticipantFailedAsync(callAutomationEvent,
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
                    return await phoneCall.HandleParticipantsUpdatedAsync(callAutomationEvent,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.MoveParticipantSucceeded:
                    return await phoneCall.HandleMoveParticipantSucceededAsync(
                            callAutomationEvent.OperationContextGuid, request, httpApp,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.MoveParticipantFailed:
                    return await phoneCall.HandleMoveParticipantFailedAsync(callAutomationEvent,
                        onProcessed: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                        onFailure: (reason) => onFailure(reason));

                case EventType.AnswerFailed:
                    return await phoneCall.HandleAddParticipantFailedAsync(callAutomationEvent,
                            (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                            onFailure: (reason) => onFailure(reason));
            }
            return onIgnored();
        }

        #endregion

        #region Event Handlers

        private async Task<TResult> CallConnectedAsync<TResult>(
                Guid operationContextGuid, 
                string eventCallConnectionId,
                string? serverCallIdStringMaybe,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            
            // Check if this is an inbound call joining an existing conference
            // If so, we need to move the participant from their answered call to the conference
            var isInboundJoiningConference = phoneCall.callConnectionId.HasBlackSpace() && 
                                              eventCallConnectionId != phoneCall.callConnectionId;
            
            if (isInboundJoiningConference)
            {
                return await phoneCall.MoveParticipantToConferenceAsync(
                        operationContextGuid, eventCallConnectionId,
                    onMoved: (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                    onFailure: (reason) => onFailure(reason));
            }
            
            return await await this.acsPhoneCallRef.StorageUpdateAsyncAsync2(
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
                            return phoneCallWithRecording.ProcessCallSequenceAsync(
                                    request, httpApp,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        },
                        onRecordingFailure: (reason, phoneCallWithFailedRecordingInfo) =>
                        {
                            return phoneCallWithFailedRecordingInfo.ProcessCallSequenceAsync(
                                    request, httpApp,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        },
                        onFailure: (reason) =>
                        {
                            return updatedPhoneCall.ProcessCallSequenceAsync(
                                    request, httpApp,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        });
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted.").AsTask());
        }

        /// <summary>
        /// Moves a participant from their answered inbound call to the existing conference.
        /// Called when CallConnected fires for an inbound call while a conference is active.
        /// </summary>
        private async Task<TResult> MoveParticipantToConferenceAsync<TResult>(
                Guid operationContextGuid, string fromCallConnectionId,
            Func<AcsPhoneCall, TResult> onMoved,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            
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

        private async Task<TResult> HandleParticipantsUpdatedAsync<TResult>(
                AcsCallAutomationEvent callAutomationEvent,

            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var currentParticipantIds = callAutomationEvent.participants
                .Select(p => p.identifier.rawId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var phoneCall = this;
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
                        var nextPhoneCall = await currentPhoneCall.HandleParticipantDisconnectedAsync(participant,
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

        private async Task<TResult> HandleParticipantDisconnectedAsync<TResult>(AcsCallParticipant participant,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            return await this.acsPhoneCallRef.StorageUpdateAsync(
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

        private async Task<TResult> HandleCallDisconnectedAsync<TResult>(AcsCallAutomationEvent eventData,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            return await this.acsPhoneCallRef.StorageUpdateAsync(
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

        private async Task<TResult> HandleParticipantAddedAsync<TResult>(Guid operationContextGuid,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            return await await this.acsPhoneCallRef.StorageUpdateAsync2(
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
                                                            participantToUnblock.incomingCallContext,  participantToUnblock.phoneNumber,
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
                                            return await phoneCallPostUnblocking.ProcessCallSequenceAsync(
                                                    request, httpApp,
                                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                                onFailure: (reason) => onFailure(reason));

                                        });
                            },
                            () => onFailure("Conference phone number not found.").AsTask());
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted.").AsTask());
        }

        public async Task<TResult> HandleParticipantCallingAsync<TResult>(
                string incomingCallContext, string toPhoneNumber, string fromPhoneNumber, string correlationId,
                AcsPhoneNumber acsPhoneNumber,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            if (string.IsNullOrEmpty(incomingCallContext))
                return onFailure("Missing context for incoming call.");
            
            var phoneCall = this;
            return await phoneCall.participants
                .Where((participant) =>participant.phoneNumber == fromPhoneNumber)
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
                                    return await current.ProcessCallSequenceAsync(
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

        internal async Task<TResult> AnswerAndUpdateCall<TResult>(AcsCallParticipant participant,
                string incomingCallContext, string fromPhoneNumber,
                AcsPhoneNumber acsPhoneNumber,
                IHttpRequest request,
            Func<AcsPhoneCall, TResult> onAnswered,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            return await await phoneCall.AnswerCallAsync(
                    participant, acsPhoneNumber,
                    incomingCallContext, fromPhoneNumber,
                    request,
                onAnswered: async (answerResult) =>
                {
                    return await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                        (current) =>
                        {
                            current.participants = current.participants
                                .UpdateWhere(
                                    (p) => p.id == participant.id,
                                    (p) =>
                                    {
                                        // Mark as Inviting - will be Connected after move completes
                                        // or directly Connected if this is the anchor call
                                        p.status = ParticipantStatus.Inviting;
                                        return p;
                                    })
                                .ToArray();
                            return current;
                        },
                        (current) =>
                        {
                            return onAnswered(current);
                        },
                        onNotFound:() => onFailure("AcsPhoneCall deleted."));
                },
                onAnswerFailed: (reason) =>
                {
                    return onFailure(reason).AsTask();
                });
        }

        private async Task<TResultAnswerCall> AnswerCallAsync<TResultAnswerCall>(
                AcsCallParticipant participant, AcsPhoneNumber acsPhoneNumber,
                string incomingCallContext, string fromPhoneNumber,
                IHttpRequest request,
            Func<AnswerCallResult?, TResultAnswerCall> onAnswered,
            Func<string, TResultAnswerCall> onAnswerFailed)
        {
            var phoneCall = this;
            var aiPhoneCallApiResources = new RequestQuery<AcsPhoneCall>(request);
            return await EastFive.Azure.AppSettings.Communications.Default
                .CreateAutomationClient(
                    async client =>
                    {
                        var callbackUri = aiPhoneCallApiResources
                            .HttpAction(EventsActionName)
                            .ById(phoneCall.acsPhoneCallRef)
                            .CompileRequest()
                            .RequestUri;

                        try
                        {
                            // Answer the incoming call - this creates a new CallConnection
                            // If a conference exists, the CallConnected event handler will
                            // detect the different callConnectionId and move the participant
                            var answerResult = await client.AnswerCallAsync(
                                new AnswerCallOptions(incomingCallContext, callbackUri)
                                {
                                    OperationContext = participant.id.ToString(),
                                });
                            
                            if (!answerResult.TryGetValue(out var answerResultValue))
                                return onAnswerFailed("Failed to answer the incoming call.");
                            
                            return onAnswered(answerResultValue);
                        }
                        catch (Exception ex)
                        {
                            // Log error - call will be missed
                            return onAnswerFailed(ex.Message);
                        }
                    },
                    why => onAnswerFailed(why).AsTask());
        }

        private async Task<TResult> HandleAddParticipantFailedAsync<TResult>(AcsCallAutomationEvent eventData,
            Func<AcsPhoneCall, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var participantId = eventData.OperationContextGuid;
            return await this.acsPhoneCallRef.StorageUpdateAsync(
                async (current, saveAsync) =>
                {
                    if (eventData.resultInformation.HasValue)
                        current.errorMessage =  eventData.resultInformation.Value.message;
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
                Guid operationContextGuid,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            return await await this.acsPhoneCallRef.StorageUpdateAsync2(
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
                    return await updatedPhoneCall.ProcessCallSequenceAsync(
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
                AcsCallAutomationEvent eventData,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var participantId = eventData.OperationContextGuid;
            return await this.acsPhoneCallRef.StorageUpdateAsync(
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
        /// Processes the call sequence by discovering IProcessParticipant attributes,
        /// asking each which participant it wants to handle, and invoking the first match.
        /// </summary>
        public async Task<TResult> ProcessCallSequenceAsync<TResult>(
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;

            var handlers = httpApp.GetType()
                .GetAttributesInterface<IProcessParticipant>(inherit: true, multiple: true);

            return await await phoneCall.conferencePhoneNumber.StorageGetAsync(
                async conferencePhoneNumber =>
                {
                    return await handlers
                        .Select(handler =>
                        {
                            var participant = handler.IdentifyParticipantToProcess(phoneCall);
                            return (handler, participant);
                        })
                        .Where(x => x.participant.HasValue)
                        .First(
                            async (match, next) =>
                            {
                                return await match.handler.ProcessParticipantAsync(
                                        phoneCall,
                                        match.participant!.Value,
                                        conferencePhoneNumber,
                                        request,
                                        httpApp,
                                    onProcessed: onProcessed,
                                    onFailure: onFailure);
                            },
                            () =>
                            {
                                // No handler identified a participant to process
                                return onProcessed(phoneCall).AsTask();
                            });
                },
                () => onFailure("Conference phone number not found.").AsTask());
        }

        #endregion

        #region Participant Operations

        /// <summary>
        /// Adds a participant to the call.
        /// </summary>
        internal async Task<TResult> CallParticipantToAddThemToCallAsync<TResult>(
                AcsPhoneNumber conferencePhoneNumber,
                AcsCallParticipant participant,
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onAdded,
            Func<string, TResult> onFailure)
        {
            // Call connection ID is null if call not yet connected
            // In that case, we cannot add participants
            // Look to AIPhoneCall POST for example of creating the call

            var phoneCall = this;
            return await EastFive.Azure.AppSettings.Communications.Default
                .CreateAutomationClient(
                    async client =>
                    {
                        var callerIdNumber = new PhoneNumberIdentifier(conferencePhoneNumber.phoneNumber);
                        var participantToAdd = new PhoneNumberIdentifier(participant.phoneNumber);
                        var callInvite = new CallInvite(participantToAdd, callerIdNumber);

                        if(phoneCall.callConnectionId.IsNullOrWhiteSpace())
                        {
                            var aiPhoneCallApiResources = new RequestQuery<AcsPhoneCall>(request);
                            var callbackUri = aiPhoneCallApiResources
                                .HttpAction(EventsActionName)
                                .ById(phoneCall.acsPhoneCallRef)
                                .CompileRequest()
                                .RequestUri;
                            
                            var createCallOptions = new CreateCallOptions(callInvite, callbackUri)
                            {
                                OperationContext = participant.id.ToString(),
                            };
                            var createCallResult = await client.CreateCallAsync(createCallOptions);
                            var callConnectionProperties = createCallResult.Value.CallConnectionProperties;

                            // Update the resource with call details
                            return await phoneCall.acsPhoneCallRef.StorageUpdateAsync(
                                async (current, saveAsync) =>
                                {
                                    current.participants = current.participants
                                        .UpdateWhere(
                                            (p) => p.id == participant.id,
                                            (p) =>
                                            {
                                                p.invitationId = string.Empty; // Will be set when added
                                                p.status =  ParticipantStatus.Inviting;
                                                return p;
                                            })
                                        .ToArray();

                                    current.callConnectionId = callConnectionProperties.CallConnectionId;
                                    current.serverCallId = callConnectionProperties.ServerCallId;
                                    current.correlationId = callConnectionProperties.CorrelationId;
                                    current.recordingState = RecordingState.None;
                                    await saveAsync(current);

                                    if(!current.HasBlockedOnNextOutboundParticipant())
                                        return onAdded(current);
                                    
                                    return await current.ProcessCallSequenceAsync(
                                            request, httpApp,
                                            onAdded,
                                            onFailure);
                                },
                                onNotFound: () => onFailure("AcsPhoneCall deleted."));
                        }

                        var callConnection = client.GetCallConnection(phoneCall.callConnectionId);
                        var addParticipantOptions = new AddParticipantOptions(callInvite)
                        {
                            OperationContext = participant.id.ToString(),
                        };

                        var retries = 10;
                        while (retries > 0)
                        {
                            try
                            {
                                var result = await callConnection.AddParticipantAsync(addParticipantOptions);
                                // Store invitation ID
                                return await await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                                    (current) =>
                                    {
                                        current.participants = current.participants
                                            .UpdateWhere(
                                                (p) => p.id == participant.id,
                                                (p) =>
                                                {
                                                    p.invitationId = result.Value.InvitationId;
                                                    p.status =  ParticipantStatus.Inviting;
                                                    return p;
                                                })
                                            .ToArray();
                                        return current;
                                    },
                                    async (updatedPhoneCall) =>
                                    {
                                        if(!updatedPhoneCall.HasBlockedOnNextOutboundParticipant())
                                            return onAdded(updatedPhoneCall);
                                        
                                        return await updatedPhoneCall.ProcessCallSequenceAsync(
                                                request, httpApp,
                                                onAdded,
                                                onFailure);
                                    },
                                    onNotFound: () => onFailure("AcsPhoneCall not found.").AsTask());
                            }
                            catch (global::Azure.RequestFailedException ex)
                            {
                                if (ex.Status == 400 && ex.ErrorCode == "8501")
                                {
                                    retries--;
                                    await Task.Delay(500);
                                    continue;
                                }
                                if (ex.Status == 400 && ex.ErrorCode == "8523")
                                {
                                    // Participant already in call
                                    return await await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                                        (current) =>
                                        {
                                            current.participants = current.participants
                                                .UpdateWhere(
                                                    (p) => p.id == participant.id,
                                                    (p) =>
                                                    {
                                                        p.invitationId = string.Empty; // Will be set when added
                                                        p.status = ParticipantStatus.Connected;
                                                        return p;
                                                    })
                                                .ToArray();
                                            return current;
                                        },
                                        (updatedPhoneCall) =>
                                        {
                                            // Fake Callback
                                            return updatedPhoneCall.HandleParticipantAddedAsync(
                                                participant.id,
                                                request, httpApp,
                                                onProcessed: (finalPhoneCall) => onAdded(finalPhoneCall),
                                                onFailure: (reason) => onFailure(reason));
                                            // Since no callback will be sent, process next participant
                                            // return updatedPhoneCall.ProcessCallSequenceAsync(
                                            //         request,
                                            //     onProcessed: (finalPhoneCall) => onAdded(finalPhoneCall),
                                            //     onFailure: (reason) => onFailure(reason));
                                        },
                                        onNotFound: () => onFailure("AcsPhoneCall not found.").AsTask());
                                }
                                if (ex.Status == 404 && ex.ErrorCode == "8522")
                                {
                                    // Call is over
                                    return await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                                        (current) =>
                                        {
                                            current.participants = current.participants
                                                .Select(
                                                    (p) =>
                                                    {
                                                        p.status = ParticipantStatus.Disconnected;
                                                        return p;
                                                    })
                                                .ToArray();
                                            return current;
                                        },
                                        (updatedPhoneCall) => onAdded(updatedPhoneCall),
                                        onNotFound: () => onFailure("AcsPhoneCall not found."));
                                }
                                return onFailure(ex.Message);
                            }
                        }
                        return onFailure("Failed to add participant after multiple retries.");
                    },
                    why => onFailure(why).AsTask());
        }

        private async Task MuteParticipantAsync(string phoneNumber)
        {
            var phoneCall = this;
            await EastFive.Azure.AppSettings.Communications.Default
                .CreateAutomationClient<Task<bool>>(
                    async client =>
                    {
                        var callConnection = client.GetCallConnection(phoneCall.callConnectionId);
                        var participant = new PhoneNumberIdentifier(phoneNumber);
                        await callConnection.MuteParticipantAsync(participant);
                        return true;
                    },
                    _ => Task.FromResult(false));
        }

        #endregion

        #region Recording

        private  async Task<TResult> StartRecordingAsync<TResult>(
            Func<AcsPhoneCall, TResult> onRecordingStarted,
            Func<string, AcsPhoneCall, TResult> onRecordingFailure,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            if (string.IsNullOrEmpty(phoneCall.serverCallId))
                return onFailure("ServerCallId is not set.");

            return await EastFive.Azure.AppSettings.Communications.Default
                .CreateAutomationClient(
                    async client =>
                    {
                        try
                        {
                            var recordingOptions = new StartRecordingOptions(new ServerCallLocator(phoneCall.serverCallId))
                            {
                                RecordingContent = RecordingContent.Audio,
                                RecordingChannel = RecordingChannel.Mixed,
                                RecordingFormat = RecordingFormat.Mp3,
                            };

                            var recordingResult = await client.GetCallRecording().StartAsync(recordingOptions);

                            return await phoneCall.acsPhoneCallRef.StorageUpdateAsync(
                                async (current, saveAsync) =>
                                {
                                    current.recordingId = recordingResult.Value.RecordingId;
                                    current.recordingState = RecordingState.Starting;
                                    await saveAsync(current);
                                    return onRecordingStarted(current);
                                },
                                onNotFound: () => onFailure("AcsPhoneCall deleted."));
                        }
                        catch (Exception ex)
                        {
                            // Log recording failure but don't fail the call
                            return await phoneCall.acsPhoneCallRef.StorageUpdateAsync(
                                async (current, saveAsync) =>
                                {
                                    current.recordingState = RecordingState.Failed;
                                    await saveAsync(current);
                                    return onRecordingFailure(ex.Message, current);
                                },
                                onNotFound: () => onFailure("AcsPhoneCall deleted."));
                        }
                    },
                    (why) => onFailure(why).AsTask());
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Extracts the invitation ID from an ACS event.
        /// Used to match AddParticipant events to specific participant add operations.
        /// </summary>
        private static string? ExtractInvitationId(JsonElement eventRoot)
        {
            if (eventRoot.TryGetProperty("invitationId", out var invIdElement))
                return invIdElement.GetString();
            return null;
        }

        private bool HasBlockedOnNextOutboundParticipant()
        {
            return this.participants
                .Any(participant => participant.status == ParticipantStatus.Inviting &&
                    participant.direction == CallDirection.InboundBlockedOnNextOutbound);
        }

        private bool TryUpdatingParticipant(Guid participantId, 
            Func<AcsCallParticipant, AcsCallParticipant> updateParticipant,
            out AcsCallParticipant updatedParticipant)
        {
            var found = false;
            var foundParticipant = default(AcsCallParticipant);
            this.participants = this.participants
                .Select(participant =>
                {
                    if (participant.id == participantId)
                    {
                        found = true;
                        foundParticipant = updateParticipant(participant);
                        return foundParticipant;
                    }
                    return participant;
                })
                .ToArray();
            updatedParticipant = foundParticipant;
            return found;
        }

        #endregion
    }
}

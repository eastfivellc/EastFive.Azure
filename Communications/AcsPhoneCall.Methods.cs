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
        /// Processes ACS call automation events by delegating to IProcessCallEvent attributes.
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
            return await httpApp.GetType()
                .GetAttributesInterface<IProcessCallEvent>(inherit: true, multiple: true)
                .First(
                    async (handler, next) => await handler.ProcessCallEventAsync(
                        phoneCall, callAutomationEvent, request, httpApp,
                        onProcessed, onIgnored, onFailure),
                    () => onIgnored().AsTask());
        }

        #endregion

        #region Event Handlers


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

        #endregion

        #region Call Orchestration

        /// <summary>
        /// Initiates the call sequence by delegating to IProcessCallEvent attributes.
        /// </summary>
        public async Task<TResult> InitiateCallSequenceAsync<TResult>(
                IHttpRequest request,
                HttpApplication httpApp,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            return await httpApp.GetType()
                .GetAttributesInterface<IProcessCallEvent>(inherit: true, multiple: true)
                .First(
                    async (handler, next) => await handler.ProcessCallSequenceAsync(
                        phoneCall, request, httpApp,
                        onProcessed, onFailure),
                    () => onProcessed(phoneCall).AsTask());
        }

        #endregion

        #region Participant Operations

        internal async Task<TResult> CallParticipantAsync<TResult>(
                AcsPhoneNumber conferencePhoneNumber,
                AcsCallParticipant participant,
                IHttpRequest request,
            Func<AcsPhoneCall, TResult> onAdded,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            if(phoneCall.IsConnected)
            {
                // Call connection ID is null if call not yet connected
                // In that case, we cannot add participants
                throw new InvalidOperationException("Call in progress, participant should be added, not called.");
            }
            return await EastFive.Azure.AppSettings.Communications.Default
                .CreateAutomationClient(
                    async client =>
                    {
                        var callerIdNumber = new PhoneNumberIdentifier(conferencePhoneNumber.phoneNumber);
                        var participantToAdd = new PhoneNumberIdentifier(participant.phoneNumber);
                        var callInvite = new CallInvite(participantToAdd, callerIdNumber);

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

                                    return onAdded(current);
                                },
                                onNotFound: () => onFailure("AcsPhoneCall deleted."));
                    },
                    why => onFailure(why).AsTask());
        }

        /// <summary>
        /// Adds a participant to the call.
        /// </summary>
        public async Task<TResult> AddParticipantAsync<TResult>(
                AcsPhoneNumber conferencePhoneNumber,
                AcsCallParticipant participant,
            Func<AcsPhoneCall, TResult> onAdded,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            if(!phoneCall.IsConnected)
            {
                // Call connection ID is null if call not yet connected
                // In that case, we cannot add participants
                throw new InvalidOperationException("Cannot add participant to call that has not been created.");
            }

            return await EastFive.Azure.AppSettings.Communications.Default
                .CreateAutomationClient(
                    async client =>
                    {
                        var callerIdNumber = new PhoneNumberIdentifier(conferencePhoneNumber.phoneNumber);
                        var participantToAdd = new PhoneNumberIdentifier(participant.phoneNumber);
                        var callInvite = new CallInvite(participantToAdd, callerIdNumber);

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
                                        return onAdded(updatedPhoneCall);
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
                                    return await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                                        (current) =>
                                        {
                                            current.participants = current.participants
                                                .UpdateWhere(
                                                    (p) => p.id == participant.id,
                                                    (p) =>
                                                    {
                                                        p.status = ParticipantStatus.Connected;
                                                        return p;
                                                    })
                                                .ToArray();
                                            return current;
                                        },
                                        (updatedPhoneCall) =>
                                        {
                                            return onAdded(updatedPhoneCall);
                                        },
                                        onNotFound: () => onFailure("AcsPhoneCall not found."));
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

        internal async Task MuteParticipantAsync(string phoneNumber)
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

        public async Task<TResult> KickoutParticipantAsync<TResult>(AcsCallParticipant acsCallParticipant,
            Func<AcsPhoneCall, TResult> onKickedOut,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;
            return await EastFive.Azure.AppSettings.Communications.Default
                .CreateAutomationClient(
                    async client =>
                    {
                        var callConnection = client.GetCallConnection(phoneCall.callConnectionId);
                        var particpantsAvailable = await callConnection.GetParticipantsAsync();
                        return await particpantsAvailable.Value
                            .Where(p => p.Identifier is PhoneNumberIdentifier pn && pn.PhoneNumber == acsCallParticipant.phoneNumber)
                            .First(
                                async (participant, next) =>
                                {
                                    try
                                    {
                                        var removalResponseMaybe = await callConnection.RemoveParticipantAsync(participant.Identifier);
                                        if(!removalResponseMaybe.HasValue)
                                            return onFailure("Failed to remove participant: No response from service.");
                                        
                                        var removalResponse = removalResponseMaybe.Value;
                                        return await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                                            (current) =>
                                            {
                                                current.participants = current.participants
                                                    .UpdateWhere(
                                                        (p) => p.id == acsCallParticipant.id,
                                                        (p) =>
                                                        {
                                                            p.status = ParticipantStatus.Removed;
                                                            return p;
                                                        })
                                                    .ToArray();
                                                return current;
                                            },
                                            (updatedPhoneCall) =>
                                            {
                                                return onKickedOut(updatedPhoneCall);
                                            },
                                            onNotFound: () => onFailure("AcsPhoneCall wasl deleted."));
                                    }
                                    catch (RequestFailedException ex)
                                    {
                                        if (ex.Status == 404 && ex.ErrorCode == "8522")
                                        {
                                            // Participant is already removed
                                            return onKickedOut(phoneCall);
                                        }
                                        return onFailure(ex.Message);
                                    }
                                },
                                () =>
                                {
                                    return onFailure($"Participant with phone number {acsCallParticipant.phoneNumber} not found in call.").AsTask();
                                });
                    },
                    why => onFailure(why).AsTask());
        }

        #endregion

        #region Recording

        internal async Task<TResult> StartRecordingAsync<TResult>(
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

        internal bool TryUpdatingParticipant(Guid participantId, 
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

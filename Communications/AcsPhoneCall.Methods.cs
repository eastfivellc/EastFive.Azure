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
                string eventType,
                JsonElement eventData,
                JsonElement fullEvent,
                IHttpRequest request,
            Func<TResult> onProcessed,
            Func<string, TResult> onUnknownEventType,
            Func<string, TResult> onFailure)
        {
            // Extract call connection ID
            if (!eventData.TryGetProperty("callConnectionId", out var callConnectionIdElement))
                return onFailure("Missing callConnectionId in event data.");

            var callConnectionId = callConnectionIdElement.GetString();
            if (string.IsNullOrEmpty(callConnectionId))
                return onFailure("Empty callConnectionId in event data.");
            
            var phoneCall = this;
            switch (eventType)
            {
                case "Microsoft.Communication.CallConnected":
                                return await phoneCall.HandleCallConnectedAsync(eventData, request,
                                    onProcessed: (updatedPhoneCall) => onProcessed(),
                                    onFailure: (reason) => onFailure(reason));

                            case "Microsoft.Communication.CallDisconnected":
                                await phoneCall.HandleCallDisconnectedAsync(eventData);
                                return onProcessed();

                            case "Microsoft.Communication.AddParticipantSucceeded":
                                return await phoneCall.HandleParticipantAddedAsync(eventData, request,
                                    onProcessed: (updatedPhoneCall) => onProcessed(),
                                    onFailure: (reason) => onFailure(reason));

                            case "Microsoft.Communication.AddParticipantFailed":
                                await phoneCall.HandleAddParticipantFailedAsync(eventData);
                                return onProcessed();
                            
                            case "Microsoft.Communication.CallTransferAccepted":
                            case "Microsoft.Communication.CallTransferFailed":
                                // Can be handled if transfer functionality is needed
                                return onProcessed();

                            case "Microsoft.Communication.RecognizeCompleted":
                            case "Microsoft.Communication.RecognizeFailed":
                            case "Microsoft.Communication.RecognizeCanceled":
                                // Can be handled for DTMF/speech recognition
                                return onProcessed();

                case "Microsoft.Communication.PlayCompleted":
                case "Microsoft.Communication.PlayFailed":
                case "Microsoft.Communication.PlayCanceled":
                    // Can be handled for media playback
                    return onProcessed();
                
                case "Microsoft.Communication.ParticipantsUpdated":
                    // Can be handled for participant updates
                    return onProcessed();
            }
            return onUnknownEventType(eventType);
        }

        #endregion

        #region Event Handlers

        private async Task<TResult> HandleCallConnectedAsync<TResult>(JsonElement root,
                IHttpRequest request,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            if (!root.TryGetProperty("operationContext", out var operationContextElement))
                return onFailure("Missing operationContext in CallConnected event.");
            var operationContextString = operationContextElement.GetString();
            if (!Guid.TryParse(operationContextString, out var operationContextGuid))
                return onFailure("Invalid operationContext in CallConnected event.");

            return await await this.acsPhoneCallRef.StorageUpdateAsync2(
                (current) =>
                {
                    current.participants = current.participants
                        .UpdateWhere(
                            (participant) => participant.id == operationContextGuid,
                            (participant) =>
                            {
                                participant.status = ParticipantStatus.Connected;
                                return participant;
                            })
                        .ToArray();
                    if(root.TryGetProperty("serverCallId", out var serverCallIdElement))
                    {
                        if(serverCallIdElement.ValueKind == JsonValueKind.String)
                        {
                            var serverCallIdString = serverCallIdElement.GetString();
                            if(serverCallIdString is not null)
                                current.serverCallId = serverCallIdString;
                        }
                    }
                    return current;
                },
                async (updatedPhoneCall) =>
                {
                    // Start recording
                    return await await updatedPhoneCall.StartRecordingAsync(
                        onRecordingStarted: (phoneCallWithRecording) =>
                        {
                            return phoneCallWithRecording.ProcessCallSequenceAsync(
                                    request,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        },
                        onRecordingFailure: (reason, phoneCallWithFailedRecordingInfo) =>
                        {
                            return phoneCallWithFailedRecordingInfo.ProcessCallSequenceAsync(
                                    request,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        },
                        onFailure: (reason) =>
                        {
                            return updatedPhoneCall.ProcessCallSequenceAsync(
                                    request,
                                onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                                onFailure: (reason) => onFailure(reason));
                        });
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted.").AsTask());
        }

        private async Task HandleCallDisconnectedAsync(JsonElement root)
        {
            if (!root.TryGetProperty("operationContext", out var operationContextElement))
                return;
            var operationContextString = operationContextElement.GetString();
            if (!Guid.TryParse(operationContextString, out var operationContextGuid))
                return;
            
            // {"resultInformation":{"code":200,"subCode":560000,"message":"The conversation has ended. DiagCode: 0#560000.@"},"version":"2024-09-15","operationContext":"177c348f-82bf-440a-b4f4-e9f5a89b081b","callConnectionId":"12005680-8e72-4bf5-8a56-743bb402853f","serverCallId":"aHR0cHM6Ly9hcGkuZmxpZ2h0cHJveHkuc2t5cGUuY29tL2FwaS92Mi9jcC9jb252LXVzZWEtMDEtcHJvZC1ha3MuY29udi5za3lwZS5jb20vY29udi9MRkdQVWVITFQwMk5NejhKMzhkS2ZnP2k9MTAtMTI4LTY0LTE3NyZlPTYzOTA1Mzk5Njc4NDQ5NTkwNA==","correlationId":"a0619022-0b2b-4ff2-a622-ff6aaee5c60a","publicEventType":"Microsoft.Communication.CallDisconnected"}
            await this.acsPhoneCallRef.StorageUpdateAsync(
                async (current, saveAsync) =>
                {
                    current.participants = current.participants
                        .UpdateWhere(
                            (participant) => participant.id == operationContextGuid,
                            (participant) =>
                            {
                                participant.status = ParticipantStatus.Disconnected;
                                return participant;
                            })
                        .ToArray();

                    await saveAsync(current);
                    return true;
                },
                onNotFound: () => false);
        }

        private async Task<TResult> HandleParticipantAddedAsync<TResult>(JsonElement root,
                IHttpRequest request,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var invitationId = ExtractInvitationId(root);
            if (string.IsNullOrEmpty(invitationId))
                return onFailure("Missing invitationId in AddParticipantSucceeded event.");
            
            var phoneCall = this;
            return await await this.acsPhoneCallRef.StorageUpdateAsync2(
                (current) =>
                {
                    // Find participant by invitation ID
                    current.participants = current.participants
                        .UpdateWhere(
                            (participant) =>participant.invitationId == invitationId,
                            (participant) =>
                            {
                                participant.status = ParticipantStatus.Connected;
                                return participant;
                            })
                        .ToArray();
                    return current;
                },
                async (updatedPhoneCall) =>
                {
                    var mutedParticipants = await updatedPhoneCall.participants
                        .Where(participant => participant.invitationId == invitationId)
                        .Where(participant => participant.muteOnConnect)
                        .Select(
                            async participant => 
                            {
                                await updatedPhoneCall.MuteParticipantAsync(participant.phoneNumber);
                                return participant;
                            })
                        .AsyncEnumerable()
                        .ToArrayAsync();
                    return await updatedPhoneCall.ProcessCallSequenceAsync(
                            request,
                        onProcessed: (finalPhoneCall) => onProcessed(finalPhoneCall),
                        onFailure: (reason) => onFailure(reason));
                },
                onNotFound: () => onFailure("AcsPhoneCall deleted.").AsTask());
        }

        private async Task HandleAddParticipantFailedAsync(JsonElement root)
        {
            var invitationId = ExtractInvitationId(root);
            if (string.IsNullOrEmpty(invitationId))
                return;

            string? errorMessage = null;
            if (root.TryGetProperty("resultInformation", out var resultInfo) &&
                resultInfo.TryGetProperty("message", out var messageElement))
            {
                errorMessage = messageElement.GetString();
            }

            await this.acsPhoneCallRef.StorageUpdateAsync(
                async (current, saveAsync) =>
                {
                    // Find participant by invitation ID
                    for (int i = 0; i < current.participants.Length; i++)
                    {
                        if (current.participants[i].invitationId == invitationId)
                        {
                            current.participants[i].status = ParticipantStatus.Failed;
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(errorMessage))
                        current.errorMessage = errorMessage;

                    await saveAsync(current);
                    return true;
                },
                onNotFound: () => false);
        }

        #endregion

        #region Call Orchestration

        /// <summary>
        /// Processes the call sequence by finding the next unprocessed participant
        /// and initiating outbound calls as needed.
        /// </summary>
        public async Task<TResult> ProcessCallSequenceAsync<TResult>(
                IHttpRequest request,
            Func<AcsPhoneCall, TResult> onProcessed,
            Func<string, TResult> onFailure)
        {
            var phoneCall = this;

            // Find next participant to process (lowest order, status = None)
            return await this.participants
                .Where(participant => participant.status == ParticipantStatus.None)
                .OrderBy(participant => participant.order)
                .First(
                    async (nextParticipant, next) =>
                    {
                        return await await phoneCall.conferencePhoneNumber.StorageGetAsync(
                            async conferencePhoneNumber =>
                            {
                                // Only initiate outbound calls - inbound participants wait for events
                                if (nextParticipant.direction == CallDirection.Outbound)
                                    return await phoneCall.AddParticipantAsync(
                                        conferencePhoneNumber,
                                        nextParticipant,
                                        request,
                                        onAdded:(updatedPhoneCall) => onProcessed(updatedPhoneCall),
                                        (reason) => onFailure(reason));
                                
                                // Notify external system that we're waiting for this inbound participant
                                if (nextParticipant.callbackUrl.IsNotDefaultOrNull() &&
                                    nextParticipant.notificationStatusCode == 0)
                                {
                                    if (conferencePhoneNumber.phoneNumber.IsNullOrWhiteSpace())
                                        return onFailure("Conference phone number is not set.");
                                    
                                    return await phoneCall.NotifyWaitingParticipantAsync(
                                        nextParticipant,
                                        conferencePhoneNumber.phoneNumber,
                                        (updatedPhoneCall) => onProcessed(updatedPhoneCall),
                                        (reason) => onFailure(reason));
                                }

                                return onProcessed(phoneCall);

                            },
                            () => onFailure("Conference phone number not found.").AsTask());
                    },
                    () =>
                    {
                        // No more participants to process, processing successful
                        return onProcessed(phoneCall).AsTask();
                    });
        }

        #endregion

        #region Participant Operations

        /// <summary>
        /// Adds a participant to the call.
        /// </summary>
        private async Task<TResult> AddParticipantAsync<TResult>(
                AcsPhoneNumber conferencePhoneNumber,
                AcsCallParticipant participant,
                IHttpRequest request,
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
                                                        p.status = ParticipantStatus.Inviting;
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
                                return await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                                    (current) =>
                                    {
                                        current.participants = current.participants
                                            .UpdateWhere(
                                                (p) => p.id == participant.id,
                                                (p) =>
                                                {
                                                    p.invitationId = result.Value.InvitationId;
                                                    p.status = ParticipantStatus.Inviting;
                                                    return p;
                                                })
                                            .ToArray();
                                        return current;
                                    },
                                    (updatedPhoneCall) => onAdded(updatedPhoneCall),
                                    onNotFound: () => onFailure("AcsPhoneCall not found."));
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
                                            // Since no callback will be sent, process next participant
                                            return updatedPhoneCall.ProcessCallSequenceAsync(
                                                    request,
                                                onProcessed: (finalPhoneCall) => onAdded(finalPhoneCall),
                                                onFailure: (reason) => onFailure(reason));
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

        /// <summary>
        /// Sends a callback notification when waiting for an inbound participant to call.
        /// Non-blocking operation that logs failures but doesn't fail the call.
        /// </summary>
        private async Task<TResult> NotifyWaitingParticipantAsync<TResult>(
            AcsCallParticipant participant,
            string conferencePhoneNumber,
            Func<AcsPhoneCall,TResult> onNotified,
            Func<string, TResult> onFailure)
        {
            if (participant.callbackUrl.IsDefaultOrNull())
                return onFailure("Participant callback URL is not set.");

            if (participant.notificationStatusCode > 0)
                return onNotified(this); // Already notified (has HTTP status code)

            var phoneCall = this;

            return await await MakeWebHookCall(
                onCompleted: async (statusCode) =>
                {
                    return await phoneCall.acsPhoneCallRef.StorageUpdateAsync2(
                        (current) =>
                        {
                            current.participants = current.participants
                                .UpdateWhere(
                                    (p) => p.id == participant.id,
                                    (p) =>
                                    {
                                        p.notificationStatusCode = statusCode;
                                        return p;
                                    })
                                .ToArray();
                            return current;
                        },
                        (updated) =>
                        {
                            return onNotified(updated);
                        },
                        onNotFound: () => onFailure("AcsPhoneCall was deleted"));
                },
                onError: (reason) => onFailure(reason).AsTask());

            async Task<TResultInner> MakeWebHookCall<TResultInner>(
                Func<System.Net.HttpStatusCode, TResultInner> onCompleted,
                Func<string, TResultInner> onError)
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);

                        using (var request = new HttpRequestMessage(HttpMethod.Post, participant.callbackUrl))
                        {
                            var payload = new ParticipantWaitingNotification
                            {
                                eventType = "ParticipantWaiting",
                                timestamp = DateTime.UtcNow,
                                acsPhoneCallId = phoneCall.acsPhoneCallRef.id,
                                participant = new ParticipantWaitingNotification.ParticipantInfo
                                {
                                    phoneNumber = participant.phoneNumber,
                                    label = participant.label,
                                    order = participant.order
                                },
                                conferencePhoneNumber = conferencePhoneNumber,
                                correlationId = phoneCall.correlationId
                            };

                            var jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                            request.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                            using (var response = await client.SendAsync(request))
                            {
                                // Capture HTTP status code for storage
                                var statusCode = (int)response.StatusCode;
                                return onCompleted(response.StatusCode);
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    // Network error - use 0 to indicate failure (allows retry in future)
                    return onError(ex.Message);
                }
                catch (TaskCanceledException ex)
                {
                    // Timeout - use 0 to indicate failure
                    return onError(ex.Message);
                }
                catch (Exception ex)
                {
                    // Other errors - use 0 to indicate failure
                    return onError(ex.Message);
                }
            }
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

        #endregion
    }
}

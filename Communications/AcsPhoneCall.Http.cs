#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;

namespace EastFive.Azure.Communications
{
    [FunctionViewController(
        Route = "AcsPhoneCall",
        ContentType = "x-application/acs-phone-call")]
    public partial struct AcsPhoneCall
    {
        #region GET

        /// <summary>
        /// Get a specific ACS phone call by ID.
        /// </summary>
        [HttpGet]
        public static Task<IHttpResponse> GetByIdAsync(
                [QueryParameter(CheckFileName = true, Name = IdPropertyName)]
                IRef<AcsPhoneCall> acsPhoneCallRef,
            ContentTypeResponse<AcsPhoneCall> onFound,
            NotFoundResponse onNotFound)
        {
            return acsPhoneCallRef.StorageGetAsync(
                (phoneCall) => onFound(phoneCall),
                () => onNotFound());
        }

        /// <summary>
        /// Get all ACS phone calls.
        /// </summary>
        [HttpGet]
        public static IHttpResponse GetAllAsync(
            MultipartAsyncResponse<AcsPhoneCall> onSuccess,
            GeneralFailureResponse onFailure)
        {
            var phoneCalls = typeof(AcsPhoneCall)
                .StorageGetAll()
                .CastObjsAs<AcsPhoneCall>();

            return onSuccess(phoneCalls);
        }

        #endregion

        #region POST

        /// <summary>
        /// Create a new ACS phone call with participants.
        /// </summary>
        [HttpPost]
        public static async Task<IHttpResponse> CreateAsync(
                [Property(Name = IdPropertyName)]IRef<AcsPhoneCall> acsPhoneCallRef,
                [Property(Name = ConferencePhoneNumberPropertyName)]IRef<AcsPhoneNumber> conferencePhoneNumber,
                [Property(Name = ParticipantsPropertyName)]AcsCallParticipant[] participants,
                [PropertyOptional(Name = CorrelationIdPropertyName)]
                string? correlationId,
                [Resource]AcsPhoneCall phoneCall,
            CreatedBodyResponse<AcsPhoneCall> onCreated,
            AlreadyExistsResponse onAlreadyExists,
            GeneralFailureResponse onFailure)
        {
            phoneCall.participants = participants
                .UpdateWhere(
                    (participant) => participant.id.IsDefault(),
                    (participant) =>
                    {
                        participant.id = Guid.NewGuid();
                        participant.status = ParticipantStatus.None;
                        participant.invitationId = string.Empty;
                        participant.callbackUrl = default;
                        return participant;
                    })
                .ToArray();
            return await phoneCall.StorageCreateAsync(
                (discard) => onCreated(phoneCall),
                () => onAlreadyExists());
        }

        #endregion

        #region DELETE

        /// <summary>
        /// Delete an ACS phone call.
        /// </summary>
        [HttpDelete]
        public static async Task<IHttpResponse> DeleteAsync(
                [QueryParameter(CheckFileName = true, Name = IdPropertyName)]
                IRef<AcsPhoneCall> acsPhoneCallRef,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound)
        {
            return await acsPhoneCallRef.StorageDeleteAsync(
                (discard) => onDeleted(),
                () => onNotFound());
        }

        #endregion

        #region Events

        public const string EventsActionName = "events";
        /// <summary>
        /// Handles incoming ACS call automation events.
        /// This is the webhook endpoint that ACS calls when events occur.
        /// </summary>
        [HttpAction(EventsActionName)]
        public static async Task<IHttpResponse> HandleIncomingEventAsync(
                [QueryId]IRef<AcsPhoneCall> acsPhoneCallRef,
                EastFive.Api.IHttpRequest request,
            NoContentResponse onProcessed,
            NotFoundResponse onNotFound,
            BadRequestResponse onBadRequest,
            GeneralFailureResponse onFailure)
        {
            var body = await request.ReadContentAsStringAsync();

            if (string.IsNullOrWhiteSpace(body))
                return onBadRequest().AddReason("Empty request body");

            // Parse JSON
            using var jsonDoc = JsonDocument.Parse(body);
            var root = jsonDoc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return onBadRequest().AddReason("Expected array of events");

            return await await acsPhoneCallRef.StorageGetAsync(
                async (phoneCall) =>
                {
                    // Process each event
                    foreach (var eventElement in root.EnumerateArray())
                    {
                        if (!eventElement.TryGetProperty("type", out var eventType))
                            continue;

                        var eventTypeString = eventType.GetString();
                        if (string.IsNullOrEmpty(eventTypeString))
                            continue;

                        if (!eventElement.TryGetProperty("data", out var eventData))
                            continue;

                        // Process the event
                        await phoneCall.ProcessCallEventAsync(
                                eventTypeString,
                                eventData,
                                eventElement,
                                request,
                            onProcessed: () => true,
                            onUnknownEventType: (eventType) => true,
                            onFailure: (reason) => true);
                    }

                    return onProcessed();
                },
                () => onNotFound().AsTask());
        }

        #endregion
    }
}

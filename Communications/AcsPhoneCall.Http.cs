#nullable enable

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Serialization;

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
                EastFive.Api.Security security,
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
                EastFive.Api.Security security,
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
                EastFive.Api.Security security,
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

                        var unformattedNumber = participant.phoneNumber;
                        var digitsOnly = new string(unformattedNumber.Where(char.IsDigit).ToArray());
                        var formattedNumber = digitsOnly.Length == 10
                                                        ? $"+1{digitsOnly}"
                                                        : $"+{digitsOnly}";

                        participant.phoneNumber = formattedNumber;
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
        [SuperAdminClaim]
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
        [AzureServiceToken]
        public static async Task<IHttpResponse> HandleIncomingEventAsync(
                [QueryId]IRef<AcsPhoneCall> acsPhoneCallRef,
                AzureApplication httpApp,
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
                    AcsPhoneCall finalPhoneCall = await root
                        .EnumerateArray()
                        .TrySelectWith(
                            (JsonElement eventElement, out EventType eventType) =>
                            {
                                if (!eventElement.TryGetProperty("type", out var eventTypeProperty))
                                {
                                    eventType = default;
                                    return false;
                                }
                                var eventTypeStringValue = eventTypeProperty.GetString();
                                return TryParseFromEnumMember(eventTypeStringValue, out eventType);

                                bool TryParseFromEnumMember<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
                                {
                                    foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
                                    {
                                        var attr = field.GetCustomAttribute<EnumMemberAttribute>();
                                        if (attr?.Value == value)
                                        {
                                            result = (TEnum)field.GetValue(null)!;
                                            return true;
                                        }
                                    }
                                    result = default;
                                    return false;
                                }
                            })
                        //.Where((eventType) => eventType.Item2 != EventType.ParticipantsUpdated)
                        .TrySelect(
                            ((JsonElement, EventType) eventElementAndTypeTpl, out AcsCallAutomationEvent acsCallAutomationEvent) =>
                            {
                                var (eventElement, eventType) = eventElementAndTypeTpl;
                                if (!eventElement.TryGetProperty("data", out var eventData))
                                {
                                    acsCallAutomationEvent = default;
                                    return false;
                                }

                                var eventDataString = eventData.GetRawText();
                                acsCallAutomationEvent = Newtonsoft.Json.JsonConvert
                                    .DeserializeObject<AcsCallAutomationEvent>(eventDataString);
                                acsCallAutomationEvent.@ref = acsCallAutomationEvent.sequenceNumber.HasValue?
                                     phoneCall.acsPhoneCallRef.id
                                    .ComposeGuid(acsCallAutomationEvent.sequenceNumber.Value)
                                    .AsRef<AcsCallAutomationEvent>()
                                    :
                                    Ref<AcsCallAutomationEvent>.NewRef();
                                acsCallAutomationEvent.acsPhoneCall = phoneCall.acsPhoneCallRef;
                                acsCallAutomationEvent.eventType = eventType;

                                return acsCallAutomationEvent.CheckValid(
                                        eventType,
                                    onValid: () =>
                                    {
                                        return true;
                                    },
                                    onInvalid: (reason) =>
                                    {
                                        return false;
                                    });
                            })
                        .Aggregate(
                            phoneCall.AsTask(),
                            async (currentPhoneCallTask, callAutomationEvent) =>
                            {
                                var currentPhoneCall = await currentPhoneCallTask;

                                var savingTask = callAutomationEvent.StorageCreateOrReplaceAsync(
                                    (didCreate, resource) =>
                                    {
                                        return resource;
                                    });
                                // Process the event
                                var returnValue = await currentPhoneCall.ProcessCallEventAsync(
                                                callAutomationEvent,
                                                request,
                                                httpApp,
                                            onProcessed: (updatedPhoneCall) => updatedPhoneCall,
                                            onIgnored: () => currentPhoneCall,
                                            onFailure: (reason) => currentPhoneCall);
                                AcsCallAutomationEvent _ = await savingTask;
                                return returnValue;
                            });
                    return onProcessed();
                },
                () => onNotFound().AsTask());
        }

        #endregion
    }
}

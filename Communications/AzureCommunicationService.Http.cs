#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Azure.EventGrid;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Reflection;


namespace EastFive.Azure.Communications
{
    [FunctionViewController(
        Route = nameof(AzureCommunicationService),
        ContentType = "x-application/azure-communication-service")]
    public partial struct AzureCommunicationService
    {
        /// <summary>
        /// Gets all Azure Communication Services from storage.
        /// </summary>
        [HttpGet]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> GetAllAsync(
            MultipartAsyncResponse<AzureCommunicationService> onFound)
        {
            var resources = GetAllFromStorageAsync();
            return onFound(resources);
        }

        /// <summary>
        /// Gets a specific Azure Communication Service by ID.
        /// </summary>
        [HttpGet]
        [SuperAdminClaim]
        public static Task<IHttpResponse> GetByIdAsync(
                [QueryParameter(CheckFileName = true, Name = IdPropertyName)]
                IRef<AzureCommunicationService> azureCommunicationServiceRef,
            ContentTypeResponse<AzureCommunicationService> onFound,
            NotFoundResponse onNotFound)
        {
            return azureCommunicationServiceRef.StorageGetAsync(
                acs => onFound(acs),
                () => onNotFound());
        }

        /// <summary>
        /// Discovers Azure Communication Services from Azure Resource Manager.
        /// Parses the resource name from the configured connection string and searches ARM.
        /// Idempotent - returns existing if already discovered.
        /// </summary>
        [HttpAction("discover")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> DiscoverHttpAsync(
            ContentTypeResponse<AzureCommunicationService> onDiscovered,
            GeneralFailureResponse onFailure)
        {
            return await DiscoverAsync(
                (acs, isNew) => onDiscovered(acs),
                error => onFailure(error));
        }

        /// <summary>
        /// Ensures the Event Grid subscription for incoming calls is configured.
        /// This will:
        /// 1. Discover the ACS resource if not already known
        /// 2. Create or update the Event Grid subscription for incoming calls
        /// </summary>
        [HttpAction("ensure-incoming-calls")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> EnsureIncomingCallsAsync(
                RequestMessage<AzureCommunicationService> acsEndpoint,
            ContentTypeResponse<EventGridSubscription> onSuccess,
            GeneralFailureResponse onFailure)
        {
            var callbackUri = acsEndpoint
                .HttpAction("incoming")
                .CompileRequest()
                .RequestUri;

            return await DiscoverAsync<IHttpResponse>(
                async (acs, isNew) =>
                {
                    return await acs.EnsureIncomingCallSubscriptionAsync(
                        callbackUri,
                        subscription => onSuccess(subscription),
                        error => onFailure(error));
                },
                error => onFailure(error));
        }

        #region Incoming Call Webhook

        /// <summary>
        /// Webhook endpoint for Azure Event Grid incoming call events.
        /// Configure this URL in Azure Event Grid subscription for "Microsoft.Communication.IncomingCall".
        /// Dispatches to IHandleIncomingCall implementations based on phone number configuration.
        /// </summary>
        [HttpAction("incoming")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> HandleIncomingCallAsync(
                EastFive.Api.HttpApplication httpApp,
                EastFive.Api.IHttpRequest request,
            NoContentResponse onProcessed,
            ContentTypeResponse<object> onValidation,
            BadRequestResponse onBadRequest,
            GeneralFailureResponse onFailure)
        {
            try
            {
                var body = await request.ReadContentAsStringAsync();

                if (string.IsNullOrWhiteSpace(body))
                    return onBadRequest().AddReason("Empty request body");

                // Handle Event Grid subscription validation handshake
                using var jsonDoc = JsonDocument.Parse(body);
                var root = jsonDoc.RootElement;

                // Check for subscription validation event (Event Grid sends this when subscribing)
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var firstEvent = root[0];
                    if (firstEvent.TryGetProperty("eventType", out var eventTypeElement))
                    {
                        var eventType = eventTypeElement.GetString();
                        if (eventType == "Microsoft.EventGrid.SubscriptionValidationEvent")
                        {
                            if (firstEvent.TryGetProperty("data", out var dataElement) &&
                                dataElement.TryGetProperty("validationCode", out var validationCodeElement))
                            {
                                var validationCode = validationCodeElement.GetString();
                                // Return validation response with the validation code
                                return onValidation(new { validationResponse = validationCode });
                            }
                        }
                    }
                }

                // Parse and dispatch incoming call events
                await DispatchIncomingCallEventsAsync(body, httpApp);

                return onProcessed();
            }
            catch (Exception ex)
            {
                return onFailure($"Failed to process incoming call: {ex.Message}");
            }
        }

        #endregion
    }
}

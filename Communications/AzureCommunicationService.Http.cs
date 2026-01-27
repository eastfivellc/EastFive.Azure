#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using EastFive;
using EastFive.Api;
using EastFive.Api.Azure;
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
        // [SuperAdminClaim]
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

        #region Incoming Event Webhook

        /// <summary>
        /// Webhook endpoint for Azure Event Grid events.
        /// Dispatches events to IHandleIncomingEvent implementations based on event type and priority.
        /// Handles subscription validation, incoming calls, and other event types.
        /// </summary>
        [HttpAction("incoming")]
        public static async Task<IHttpResponse> HandleIncomingEventAsync(
                AzureApplication httpApp,
                EastFive.Api.IHttpRequest request,
            NoContentResponse onProcessed,
            BadRequestResponse onBadRequest,
            GeneralFailureResponse onFailure)
        {
            try
            {
                var body = await request.ReadContentAsStringAsync();

                if (string.IsNullOrWhiteSpace(body))
                    return onBadRequest().AddReason("Empty request body");

                // Parse JSON once
                using var jsonDoc = JsonDocument.Parse(body);
                var root = jsonDoc.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                    return onBadRequest().AddReason("Expected array of events");
                
                var handlers = httpApp.GetType()
                    .GetAttributesInterface<IHandleIncomingEvent>(inherit: true, multiple: true)
                    .ToArray();
                    

                // Build array of events to process
                return await root
                    .EnumerateArray()
                    .SelectMany(
                        (eventElement) => 
                        {
                            var noResult = Array.Empty<(int priority, Func<Func<Task<IHttpResponse>>, Task<IHttpResponse>> )>();
                            if(!eventElement.TryGetProperty("eventType", out var eventType))
                                return noResult;
                            var eventTypeString = eventType.GetString();
                            if(eventTypeString.IsNullOrWhiteSpace())
                                return noResult;
                            if(!eventElement.TryGetProperty("data", out var eventData))
                                return noResult;

                            var handlerPairs = handlers
                                .Select(
                                    handler =>
                                    {
                                        var priority = handler.DoesHandleEvent(eventTypeString, eventData, eventElement);
                                        Func<Func<Task<IHttpResponse>>, Task<IHttpResponse>> chain = (onContinueExecution) => handler.HandleEventAsync(
                                            eventTypeString, eventData, eventElement, request, httpApp,
                                            onProcessed, onBadRequest, onFailure,
                                            onContinueExecution);
                                        return (priority, chain);
                                    })
                                .Where(x => x.priority >= 0)
                                .ToArray();

                            return handlerPairs;
                        })
                    .ToArray()
                    .OrderByDescending(ev => ev.priority)
                    .First(
                        (evPair, next) => evPair.Item2(next),
                        () => onProcessed().AsTask());
            }
            catch (Exception ex)
            {
                return onFailure($"Failed to process event: {ex.Message}");
            }
        }

        /// <summary>
        /// Recursively dispatches events through the handler chain.
        /// Each event is processed by handlers ordered by priority (descending).
        /// Calling continueExecution moves to the next handler, then the next event.
        /// Returning directly short-circuits all remaining processing.
        /// </summary>
        // private static async Task<IHttpResponse> DispatchEventsAsync(
        //     JsonElement[] events,
        //     int eventIndex,
        //     IHttpRequest request,
        //     HttpApplication httpApp,
        //     NoContentResponse onProcessed,
        //     BadRequestResponse onBadRequest,
        //     GeneralFailureResponse onFailure)
        // {
        //     // Base case: all events processed
        //     if (eventIndex >= events.Length)
        //         return onProcessed();

        //     var eventElement = events[eventIndex];
        //     var eventType = eventElement.GetProperty("eventType").GetString() ?? string.Empty;
        //     var eventData = eventElement.GetProperty("data");

        //     // Get all handlers that want to process this event (priority >= 0)
        //     // Order by priority descending (highest first)
        //     var handlers = httpApp.GetType()
        //         .GetAttributesInterface<IHandleIncomingEvent>(inherit: true, multiple: true)
        //         .Select(handler => (handler, priority: handler.DoesHandleEvent(eventType, eventData, eventElement)))
        //         .Where(x => x.priority >= 0)
        //         .OrderByDescending(x => x.priority)
        //         .Select(x => x.handler)
        //         .ToArray();

        //     // Build handler chain using Aggregate pattern
        //     // Innermost: log unhandled event, then continue to next event
        //     Func<Task<IHttpResponse>> chain = async () =>
        //     {
        //         // Log that no handler processed this event
        //         httpApp.Logger.LogTrace($"Unhandled event type: {eventType}");
                
        //         // Continue to next event
        //         return await DispatchEventsAsync(events, eventIndex + 1, request, httpApp, onProcessed);
        //     };

        //     // Wrap each handler around the chain (in reverse order so highest priority executes first)
        //     foreach (var handler in handlers.Reverse())
        //     {
        //         var capturedHandler = handler;
        //         var capturedChain = chain;
        //         chain = () => capturedHandler.HandleEventAsync(
        //             eventType, eventData, eventElement, request, httpApp, capturedChain);
        //     }

        //     // Execute the chain
        //     return await chain();
        // }

        #endregion
    }
}

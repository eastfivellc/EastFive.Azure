#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Auth;
using EastFive.Azure.Communications;
using EastFive.Azure.Persistence;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Reflection;


namespace EastFive.Azure.EventGrid
{
    [FunctionViewController(
        Route = "EventGridSubscription",
        ContentType = "x-application/eventgrid-subscription")]
    public partial struct EventGridSubscription
    {
        /// <summary>
        /// Lists all Event Grid subscriptions from local storage.
        /// </summary>
        [HttpGet]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> GetAllAsync(
            MultipartAsyncResponse<EventGridSubscription> onFound)
        {
            var subscriptions = GetAllFromStorageAsync();
            return onFound(subscriptions);
        }

        /// <summary>
        /// Gets a specific Event Grid subscription by scope and name.
        /// When refresh=true, queries Azure directly; otherwise returns from local storage.
        /// </summary>
        [HttpGet]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> GetByNameAsync(
                [QueryParameter(Name = ScopeResourceIdPropertyName)] string scopeResourceId,
                [QueryParameter(Name = SubscriptionNamePropertyName)] string subscriptionName,
                [QueryParameter(Name = "refresh")] bool refresh,
            ContentTypeResponse<EventGridSubscription> onFound,
            NotFoundResponse onNotFound,
            GeneralFailureResponse onFailure)
        {
            if (refresh)
            {
                return await RefreshFromAzureAsync(
                    scopeResourceId,
                    subscriptionName,
                    subscription => onFound(subscription),
                    () => onNotFound(),
                    error => onFailure(error));
            }

            return await FindByNameAsync(
                scopeResourceId,
                subscriptionName,
                subscription => onFound(subscription),
                () => onNotFound());
        }

        /// <summary>
        /// Creates or updates an Event Grid subscription.
        /// Idempotent - safe to call multiple times.
        /// </summary>
        /// <remarks>
        /// Required body fields:
        /// - scope_resource_id: ARM resource ID (e.g., "/subscriptions/.../Microsoft.Communication/CommunicationServices/myacs")
        /// - subscription_name: Unique name for the subscription
        /// - endpoint: Webhook URL to receive events
        /// - event_types: Array of event types to subscribe to
        /// </remarks>
        [HttpPost]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> CreateAsync(
                [Property(Name = ScopeResourceIdPropertyName)] string scopeResourceId,
                [Property(Name = SubscriptionNamePropertyName)] string subscriptionName,
                [Property(Name = EndpointPropertyName)] Uri endpoint,
                [Property(Name = EventTypesPropertyName)] string[] eventTypes,
            CreatedResponse onCreated,
            AlreadyExistsReferencedResponse onAlreadyExists,
            GeneralFailureResponse onFailure)
        {
            return await EnsureAsync(
                scopeResourceId,
                subscriptionName,
                endpoint,
                eventTypes,
                subscription => onCreated(),
                error => onFailure(error));
        }

        #region Unified Event Grid Webhook

        /// <summary>
        /// Unified webhook endpoint for ALL Azure Event Grid events.
        /// Dispatches events to IHandleIncomingEvent implementations based on event type and priority.
        /// Handles subscription validation, incoming calls, recording events, and any future event types.
        /// Generic approach - works with any Azure resource that sends Event Grid events.
        /// </summary>
        [HttpAction("webhook")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> HandleIncomingEventAsync(
                AzureApplication httpApp,
                EastFive.Api.IHttpRequest request,
            NoContentResponse onProcessed,
            BadRequestResponse onBadRequest,
            GeneralFailureResponse onFailure)
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

        #endregion

        #region Auto-Registration

        /// <summary>
        /// Auto-registers all Event Grid subscriptions for attributes on this application.
        /// Scans for all IRegisterEventSubscription attributes, discovers their event source providers,
        /// and creates Event Grid subscriptions.
        /// </summary>
        [HttpAction("auto-register")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> AutoRegisterEventSubscriptionsAsync(
                RequestMessage<EventGridSubscription> eventGridEndpoint,
                AzureApplication httpApp,
            MultipartAsyncResponse<EventGridSubscription> onSuccess,
            GeneralFailureResponse onFailure)
        {
            var callbackUri = eventGridEndpoint
                .HttpAction("webhook")
                .CompileRequest()
                .RequestUri;

            return httpApp.GetType()
                .GetAttributesInterface<IRegisterEventSubscription>(inherit: true, multiple: true)
                .Select(
                    async registrator =>
                    {
                        return await registrator.GetSubscriptionRegistrationsAsync(
                            (scopeResourceId, subscriptionName, eventTypes) =>
                            {
                                return EnsureAsync(
                                    scopeResourceId,
                                    subscriptionName,
                                    callbackUri,
                                    eventTypes,
                                    onSuccess:(egs) => egs,
                                    onFailure:(error) => default(EventGridSubscription?));
                            },
                            subs => subs,
                            error => new EventGridSubscription?[0]);
                    })
                .AsyncEnumerable()
                .SelectMany()
                .SelectWhereHasValue()
                .HttpResponse(onSuccess);
        }

        public delegate Task<TRegistration> EnsureRegistrationAsyncDelegate<TRegistration>(
            string scopeResourceId,
            string subscriptionName,
            string[] eventTypes);

        #endregion
    }
}

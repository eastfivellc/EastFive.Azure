#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Linq.Async;


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
    }
}

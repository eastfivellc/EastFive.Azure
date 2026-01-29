#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Azure.EventGrid;
using EastFive.Extensions;
using EastFive.Linq.Async;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Base class for handling recording events from Azure Communication Services.
    /// Implements IHandleIncomingEvent and IRegisterEventSubscription for self-registration.
    ///
    /// Inherit from this class and override HandleRecordingEventAsync to implement
    /// custom recording event logic.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public abstract class RecordingEventHandlerAttribute : Attribute,
        IHandleIncomingEvent,
        IRegisterEventSubscription
    {
        private const string RecordingStateChangedEvent = "Microsoft.Communication.RecordingStateChanged";
        private const string RecordingFileStatusUpdatedEvent = "Microsoft.Communication.RecordingFileStatusUpdated";

        /// <summary>
        /// The priority at which this handler processes recording events.
        /// Override to change execution order relative to other handlers.
        /// Default is EventHandlerPriority.Default (0).
        /// </summary>
        protected virtual int Priority => EventHandlerPriority.Default;

        /// <summary>
        /// Returns the configured priority for recording events.
        /// Returns -1 for all other event types.
        /// </summary>
        public int DoesHandleEvent(string eventType, JsonElement eventData, JsonElement fullEvent)
        {
            return eventType == RecordingStateChangedEvent || eventType == RecordingFileStatusUpdatedEvent
                ? Priority
                : EventHandlerPriority.DoNotHandle;
        }

        /// <summary>
        /// Parses the recording event data and delegates to the strongly-typed handler.
        /// </summary>
        public async Task<IHttpResponse> HandleEventAsync(
                string eventType,
                JsonElement eventData,
                JsonElement fullEvent,
                IHttpRequest request,
                EastFive.Api.HttpApplication httpApp,
            NoContentResponse onProcessed,
            BadRequestResponse onBadRequest,
            GeneralFailureResponse onFailure,
            Func<Task<IHttpResponse>> continueExecution)
        {
            // Call the strongly-typed handler
            return await HandleRecordingEventAsync(
                eventType,
                eventData,
                request,
                httpApp,
                continueExecution);
        }

        /// <summary>
        /// Override this method to handle recording events with strongly-typed parameters.
        /// </summary>
        /// <param name="eventType">The event type (RecordingStateChanged or RecordingFileStatusUpdated)</param>
        /// <param name="eventData">The event data JSON element</param>
        /// <param name="request">The HTTP request, used to create responses via request.CreateResponse()</param>
        /// <param name="httpApp">The application instance</param>
        /// <param name="continueExecution">Call to pass to next handler/event; returns final response</param>
        /// <returns>
        /// Return directly to short-circuit (stop processing remaining handlers and events).
        /// Call and return continueExecution() to pass to the next handler.
        /// </returns>
        protected abstract Task<IHttpResponse> HandleRecordingEventAsync(
            string eventType,
            JsonElement eventData,
            IHttpRequest request,
            HttpApplication httpApp,
            Func<Task<IHttpResponse>> continueExecution);

        #region IRegisterEventSubscription Implementation

        /// <summary>
        /// The type of event source provider this attribute requires.
        /// Used for reporting purposes only.
        /// Recording events are emitted from Azure Communication Services.
        /// </summary>
        public Type EventSourceProviderType => typeof(AzureCommunicationService);

        /// <summary>
        /// Returns the subscription configuration for recording events.
        /// Discovers the ACS provider and returns subscription details.
        /// </summary>
        public async Task<TResult> GetSubscriptionRegistrationsAsync<TRegistration,TResult>(
            EventGridSubscription.EnsureRegistrationAsyncDelegate<TRegistration> ensureRegistrationAsync,
            Func<TRegistration[], TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return await await AzureCommunicationService.DiscoverAsync(
                async(acss) =>
                {
                    var registrations = await acss
                        .Select(
                            async acs =>
                            {
                                return await ensureRegistrationAsync(acs.resourceId,
                                    AzureCommunicationService.RecordingEventsSubscriptionName,
                                    AzureCommunicationService.RecordingEventTypes);
                            })
                        .AsyncEnumerable()
                        .ToArrayAsync();
                    return onSuccess(registrations);
                },
                error => onFailure(error).AsTask());
        }

        #endregion
    }
}

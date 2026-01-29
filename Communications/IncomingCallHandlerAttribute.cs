#nullable enable

using System;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Azure.EventGrid;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Base class for handling incoming call events from Azure Communication Services.
    /// Implements IHandleIncomingEvent and IRegisterEventSubscription for self-registration.
    ///
    /// Inherit from this class and override HandleIncomingCallAsync to implement
    /// custom incoming call logic.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public abstract class IncomingCallHandlerAttribute : Attribute,
        IHandleIncomingEvent,
        IRegisterEventSubscription
    {
        private const string IncomingCallEventType = "Microsoft.Communication.IncomingCall";

        /// <summary>
        /// The priority at which this handler processes incoming call events.
        /// Override to change execution order relative to other handlers.
        /// Default is EventHandlerPriority.Default (0).
        /// </summary>
        protected virtual int Priority => EventHandlerPriority.Default;

        /// <summary>
        /// Returns the configured priority for incoming call events.
        /// Returns -1 for all other event types.
        /// </summary>
        public int DoesHandleEvent(string eventType, JsonElement eventData, JsonElement fullEvent)
        {
            return eventType == IncomingCallEventType
                ? Priority
                : EventHandlerPriority.DoNotHandle;
        }

        /// <summary>
        /// Parses the incoming call event data and delegates to the strongly-typed handler.
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
            // Parse incoming call context
            if (!eventData.TryGetProperty("incomingCallContext", out var contextElement))
                return await continueExecution();

            var incomingCallContext = contextElement.GetString();
            if (string.IsNullOrEmpty(incomingCallContext))
                return await continueExecution();

            // Parse phone numbers
            var toPhoneNumber = ExtractPhoneNumber(eventData, "to");
            var fromPhoneNumber = ExtractPhoneNumber(eventData, "from");

            if (string.IsNullOrEmpty(toPhoneNumber) || string.IsNullOrEmpty(fromPhoneNumber))
                return await continueExecution();

            // Look up the AcsPhoneNumber entity
            var acsPhoneNumber = await toPhoneNumber
                .StorageGetBy((AcsPhoneNumber p) => p.phoneNumber)
                .FirstAsync(
                    phone => (AcsPhoneNumber?)phone,
                    () => (AcsPhoneNumber?)null);

            // Call the strongly-typed handler
            return await HandleIncomingCallAsync(
                incomingCallContext,
                toPhoneNumber,
                fromPhoneNumber,
                acsPhoneNumber,
                request,
                httpApp,
                continueExecution);
        }

        /// <summary>
        /// Override this method to handle incoming calls with strongly-typed parameters.
        /// </summary>
        /// <param name="incomingCallContext">The opaque context string required to answer the call</param>
        /// <param name="toPhoneNumber">The ACS phone number that was called (E.164 format)</param>
        /// <param name="fromPhoneNumber">The caller's phone number (E.164 format)</param>
        /// <param name="acsPhoneNumber">The AcsPhoneNumber entity for the called number, if found</param>
        /// <param name="request">The HTTP request, used to create responses via request.CreateResponse()</param>
        /// <param name="httpApp">The application instance</param>
        /// <param name="continueExecution">Call to pass to next handler/event; returns final response</param>
        /// <returns>
        /// Return directly to short-circuit (stop processing remaining handlers and events).
        /// Call and return continueExecution() to pass to the next handler.
        /// </returns>
        protected abstract Task<IHttpResponse> HandleIncomingCallAsync(
            string incomingCallContext,
            string toPhoneNumber,
            string fromPhoneNumber,
            AcsPhoneNumber? acsPhoneNumber,
            IHttpRequest request,
            HttpApplication httpApp,
            Func<Task<IHttpResponse>> continueExecution);

        /// <summary>
        /// Extracts a phone number from the event data.
        /// Handles the nested structure: { "to": { "phoneNumber": { "value": "+1234567890" } } }
        /// </summary>
        private static string? ExtractPhoneNumber(JsonElement data, string property)
        {
            if (data.TryGetProperty(property, out var element) &&
                element.TryGetProperty("phoneNumber", out var phoneElement) &&
                phoneElement.TryGetProperty("value", out var valueElement))
            {
                return valueElement.GetString();
            }
            return null;
        }

        #region IRegisterEventSubscription Implementation

        /// <summary>
        /// The type of event source provider this attribute requires.
        /// Used for reporting purposes only.
        /// Incoming calls are emitted from Azure Communication Services.
        /// </summary>
        public Type EventSourceProviderType => typeof(AzureCommunicationService);

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
                                    AzureCommunicationService.IncomingCallsSubscriptionName,
                                    AzureCommunicationService.IncomingCallEventTypes);
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

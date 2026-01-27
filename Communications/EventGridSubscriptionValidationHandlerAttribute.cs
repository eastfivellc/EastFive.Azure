#nullable enable

using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using EastFive.Api;

namespace EastFive.Azure.Communications
{
    /// <summary>
    /// Handles Azure Event Grid subscription validation events.
    /// Returns the validation response to complete the handshake.
    /// 
    /// Add this attribute to your application's Startup class (or base class)
    /// to enable Event Grid webhook subscription validation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class EventGridSubscriptionValidationHandlerAttribute : Attribute, IHandleIncomingEvent
    {
        private const string ValidationEventType = "Microsoft.EventGrid.SubscriptionValidationEvent";

        /// <summary>
        /// Returns maximum priority for validation events to ensure they are handled first.
        /// Returns -1 for all other event types.
        /// </summary>
        public int DoesHandleEvent(string eventType, JsonElement eventData, JsonElement fullEvent)
        {
            return eventType == ValidationEventType
                ? EventHandlerPriority.Validation
                : EventHandlerPriority.DoNotHandle;
        }

        /// <summary>
        /// Handles the subscription validation event by returning the validation response.
        /// Does not call continuation - validation response is returned immediately,
        /// stopping processing of any remaining events in the batch.
        /// </summary>
        public Task<IHttpResponse> HandleEventAsync(
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
            // Extract validation code from event data
            if (eventData.TryGetProperty("validationCode", out var validationCodeElement))
            {
                var validationCode = validationCodeElement.GetString();
                if (!string.IsNullOrEmpty(validationCode))
                {
                    // Return validation response using the request's CreateResponse extension
                    var response = request.CreateResponse(HttpStatusCode.OK, 
                        new { validationResponse = validationCode });
                    return Task.FromResult(response);
                }
            }

            // If we can't extract the validation code, continue to next handler
            // (though this shouldn't happen for valid validation events)
            return continueExecution();
        }
    }
}

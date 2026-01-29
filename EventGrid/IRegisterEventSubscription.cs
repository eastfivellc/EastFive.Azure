#nullable enable

using System;
using System.Threading.Tasks;

namespace EastFive.Azure.EventGrid
{


    /// <summary>
    /// Interface for attributes that register Event Grid subscriptions.
    /// Attributes implement this to declare their subscription requirements.
    /// </summary>
    public interface IRegisterEventSubscription
    {
        /// <summary>
        /// The type of event source provider this attribute requires.
        /// Used for reporting purposes only.
        /// E.g., typeof(AzureCommunicationService), typeof(StorageAccount), etc.
        /// </summary>
        Type EventSourceProviderType { get; }

        /// <summary>
        /// Returns the subscription configuration this attribute needs registered.
        /// Called during auto-registration. Attributes are responsible for discovering
        /// their own event source providers.
        /// </summary>
        /// <param name="callbackUri">The unified webhook endpoint URI</param>
        /// <returns>Subscription registration details, or null to skip registration</returns>
        Task<TResult> GetSubscriptionRegistrationsAsync<TRegistration,TResult>(
            EventGridSubscription.EnsureRegistrationAsyncDelegate<TRegistration> ensureRegistrationAsync,
            Func<TRegistration[], TResult> onSuccess,
            Func<string, TResult> onFailure);
    }

    /// <summary>
    /// Configuration details for an Event Grid subscription registration.
    /// </summary>
    public class SubscriptionRegistration
    {
        /// <summary>
        /// ARM resource ID to subscribe to (e.g., ACS resource, Storage account)
        /// </summary>
        public string ScopeResourceId { get; set; } = string.Empty;

        /// <summary>
        /// Unique name for this subscription within the scope
        /// </summary>
        public string SubscriptionName { get; set; } = string.Empty;

        /// <summary>
        /// Webhook endpoint URI to receive events
        /// </summary>
        public Uri CallbackUri { get; set; } = null!;

        /// <summary>
        /// Event types to subscribe to
        /// </summary>
        public string[] EventTypes { get; set; } = Array.Empty<string>();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Reflection;
using EastFive.Azure.Auth.CredentialProviders;

namespace EastFive.Azure.Auth
{
    /// <summary>
    /// Resolves authentication capabilities for any <see cref="IApplication"/>
    /// without requiring the application to implement <c>IAuthApplication</c> /
    /// <c>IAzureApplication</c>. Capabilities are discovered through the domain
    /// attribute-interface scan (<see cref="AttributeInterfaceScope.AttributeInterfacesInDomain{T}"/>),
    /// so a host only needs to declare the relevant provider attributes at
    /// <c>[assembly:]</c> scope.
    /// </summary>
    /// <remarks>
    /// These are extension methods on <see cref="IApplication"/>. Code paths that
    /// still use the concrete <c>AzureApplication</c> / <c>IAuthApplication</c>
    /// interface continue to bind to that type's instance members; only call sites
    /// whose parameter has been widened to <see cref="IApplication"/> resolve to
    /// these extensions.
    /// </remarks>
    public static class ApplicationAuthExtensions
    {
        #region Login providers (domain scan, lazy + cached)

        private static readonly object loginProvidersLock = new object();
        private static IDictionary<string, IProvideLogin> loginProvidersCache;

        /// <summary>
        /// All login providers keyed by <see cref="IProvideAuthorization.Method"/>,
        /// discovered by scanning the domain for <see cref="IProvideLoginProvider"/>
        /// attribute-interfaces and asynchronously loading each. The result is
        /// computed once and cached for the lifetime of the process.
        /// </summary>
        public static IDictionary<string, IProvideLogin> GetLoginProviders(this IApplication application)
        {
            if (loginProvidersCache != null)
                return loginProvidersCache;

            lock (loginProvidersLock)
            {
                if (loginProvidersCache != null)
                    return loginProvidersCache;

                loginProvidersCache = LoadLoginProvidersAsync().GetAwaiter().GetResult();
                return loginProvidersCache;
            }
        }

        private static async Task<IDictionary<string, IProvideLogin>> LoadLoginProvidersAsync()
        {
            var loginProviderProviders = AttributeInterfaceScope
                .AttributeInterfacesInDomain<IProvideLoginProvider>()
                .ToArray();

            var loginProviders = await loginProviderProviders
                .Select(
                    async loginProviderProvider =>
                    {
                        try
                        {
                            return await loginProviderProvider.ProvideLoginProviderAsync(
                                loginProvider => (true, loginProvider),
                                (why) => (false, default(IProvideLogin)));
                        }
                        catch (Exception)
                        {
                            return (false, default(IProvideLogin));
                        }
                    })
                .AsyncEnumerable()
                .SelectWhere()
                .ToArrayAsync();

            return loginProviders
                .Where(loginProvider => !loginProvider.IsDefaultOrNull())
                .Distinct(loginProvider => loginProvider.Method)
                .ToDictionary(loginProvider => loginProvider.Method);
        }

        /// <summary>
        /// Authorization providers keyed by <see cref="IProvideAuthorization.Method"/>.
        /// Derived from the login providers (<see cref="IProvideLogin"/> extends
        /// <see cref="IProvideAuthorization"/>).
        /// </summary>
        public static IDictionary<string, IProvideAuthorization> GetAuthorizationProviders(this IApplication application)
        {
            return application.GetLoginProviders()
                .ToDictionary(kvp => kvp.Key, kvp => (IProvideAuthorization)kvp.Value);
        }

        #endregion

        #region Account information provider (domain scan)

        /// <summary>
        /// The application's account-information provider, discovered by scanning
        /// the domain for an <see cref="IProvideAccountInformation"/>
        /// attribute-interface. Returns <c>null</c> when none is declared.
        /// </summary>
        public static IProvideAccountInformation GetAccountInformationProvider(this IApplication application)
        {
            return AttributeInterfaceScope
                .AttributeInterfacesInDomain<IProvideAccountInformation>()
                .FirstOrDefault();
        }

        #endregion

        #region Credential administration / integration authorization (defaults)

        /// <summary>
        /// Default credential-administration policy: the requesting session may
        /// administer a credential when it represents the actor in question, or
        /// when it is the configured super-admin account.
        /// </summary>
        public static Task<bool> CanAdministerCredentialAsync(this IApplication application,
            Guid actorInQuestion, SessionToken security)
        {
            if (security.accountIdMaybe.HasValue)
            {
                if (actorInQuestion == security.accountIdMaybe.Value)
                    return true.AsTask();
            }

            return application.IsAdminAsync(security);
        }

        /// <summary>
        /// Default super-admin check against
        /// <see cref="EastFive.Api.AppSettings.ActorIdSuperAdmin"/>.
        /// </summary>
        public static Task<bool> IsAdminAsync(this IApplication application, SessionToken security)
        {
            return EastFive.Web.Configuration.Settings.GetGuid(
                EastFive.Api.AppSettings.ActorIdSuperAdmin,
                (actorIdSuperAdmin) =>
                {
                    if (security.accountIdMaybe.HasValue)
                    {
                        if (actorIdSuperAdmin == security.accountIdMaybe.Value)
                            return true;
                    }
                    return false;
                },
                (why) => false).AsTask();
        }

        /// <summary>
        /// Default integration-authorization policy: an authorization may drive an
        /// integration when it is unbound or bound to the integration's account.
        /// </summary>
        public static Task<bool> ShouldAuthorizeIntegrationAsync(this IApplication application,
            XIntegration integration, Authorization authorization)
        {
            if (authorization.accountIdMaybe.HasValue)
                if (integration.accountId != authorization.accountIdMaybe.Value)
                    return false.AsTask();
            return true.AsTask();
        }

        #endregion

        #region Actor name details (optional domain scan)

        /// <summary>
        /// Resolves human-readable name details for an actor via an optional
        /// <see cref="IProvideActorNameDetails"/> declared in the domain. When no
        /// provider is present the lookup resolves to <paramref name="onActorNotFound"/>.
        /// </summary>
        public static Task<TResult> GetActorNameDetailsAsync<TResult>(this IApplication application,
                Guid actorId,
            Func<string, string, string, TResult> onActorFound,
            Func<TResult> onActorNotFound)
        {
            var provider = AttributeInterfaceScope
                .AttributeInterfacesInDomain<IProvideActorNameDetails>()
                .FirstOrDefault();

            if (provider.IsDefaultOrNull())
                return onActorNotFound().AsTask();

            return provider.GetActorNameDetailsAsync(actorId, onActorFound, onActorNotFound);
        }

        #endregion
    }
}

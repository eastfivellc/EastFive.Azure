using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// A pre-loaded storage record together with the driver it was loaded from.
    /// Constructed by parameter-binding attributes (e.g. <c>[StorageEntityFromQueryParam]</c>)
    /// before the controller method body runs. The id-source attribute is responsible for
    /// translating a missing row into the appropriate 4XX response (when <typeparamref name="T"/>
    /// is non-nullable) or for surfacing <c>default(T)</c> (when <typeparamref name="T"/>
    /// is nullable).
    /// </summary>
    /// <remarks>
    /// Pure data: lookup key, loaded entity, eTag, last-modified timestamp, and the
    /// driver scoped to the originating datastore. Constructed in two flavours:
    /// the partial form (key only, no entity yet) is produced by the loader
    /// pipeline's bind step and lives in the parameter slot until the loader's
    /// validator replaces it with the fully-populated form. <see cref="Entity"/>
    /// is <c>default(T)</c> on a partial; controller methods only ever observe
    /// the full form.
    /// Mutation and deletion are exposed as extension methods on
    /// <see cref="StorageEntityExtensions"/> rather than members on this class.
    /// </remarks>
    public sealed class StorageEntity<T>
        where T : IReferenceable
    {
        /// <summary>
        /// Lookup key. Drivers pattern-match on the concrete
        /// <see cref="IStorageKey{TEntity}"/> implementation to load the entity.
        /// </summary>
        public IStorageKey<T> Key { get; }

        /// <summary>The loaded entity. <c>default(T)</c> on a partial.</summary>
        public T Entity { get; }

        public string ETag { get; }

        public DateTimeOffset? LastModified { get; }

        /// <summary>
        /// Driver scoped to the datastore this entity was loaded from. All
        /// mutation and deletion extensions route through this driver so
        /// per-parameter datastore overrides are honored. <c>null</c> on a
        /// partial.
        /// </summary>
        public AzureTableDriverDynamic Driver { get; }

        /// <summary>
        /// Partial constructor: lookup key only, no entity loaded yet. Produced
        /// by the loader pipeline's bind step and lives in the parameter slot
        /// until the validator replaces it with the full form.
        /// </summary>
        internal StorageEntity(IStorageKey<T> key)
        {
            this.Key = key ?? throw new ArgumentNullException(nameof(key));
            this.Entity = default;
            this.ETag = null;
            this.LastModified = null;
            this.Driver = null;
        }

        internal StorageEntity(
            IStorageKey<T> key,
            T entity,
            string eTag,
            DateTimeOffset? lastModified,
            AzureTableDriverDynamic driver)
        {
            this.Key = key ?? throw new ArgumentNullException(nameof(key));
            this.Entity = entity;
            this.ETag = eTag;
            this.LastModified = lastModified;
            this.Driver = driver;
        }

        /// <summary>
        /// Test-only backend. When non-null,
        /// <see cref="StorageEntityExtensions.UpdateAsync{T, TResult}"/> and
        /// <see cref="StorageEntityExtensions.DeleteAsync{T, TResult}"/> route
        /// through the backend instead of <see cref="Driver"/>. Production
        /// code never sets this.
        /// </summary>
        internal IStorageEntityTestBackend<T> TestBackend { get; }

        private StorageEntity(
            IStorageKey<T> key,
            T entity,
            IStorageEntityTestBackend<T> testBackend)
        {
            this.Key = key ?? throw new ArgumentNullException(nameof(key));
            this.Entity = entity;
            this.ETag = "test-etag";
            this.LastModified = DateTimeOffset.UtcNow;
            this.Driver = null;
            this.TestBackend = testBackend ?? throw new ArgumentNullException(nameof(testBackend));
        }

        internal static StorageEntity<T> CreateWithTestBackend(
            IStorageKey<T> key,
            T entity,
            IStorageEntityTestBackend<T> testBackend)
            => new StorageEntity<T>(key, entity, testBackend);

        /// <summary>
        /// Loader pipeline step 2: run the async load against
        /// <paramref name="driver"/> and return a <see cref="ParameterMutation"/>
        /// that hands the validator chain a parameter list with
        /// <paramref name="ownerParameter"/>'s slot replaced by the loaded
        /// full <see cref="StorageEntity{T}"/>.
        ///
        /// On miss the closure short-circuits with 404, on driver failure 500;
        /// both use <paramref name="bindings"/> to render a client-friendly
        /// identifier (falling back to the storage-shaped key when bindings
        /// were not captured).
        ///
        /// Called by <c>StorageEntityLoaderHelpers.LoadEntityAsync</c> via
        /// generic dispatch over <typeparamref name="T"/>; not part of the
        /// surface that controller bodies use (they receive the loaded entity
        /// via parameter binding and read it through <see cref="Entity"/>).
        /// </summary>
        public static async Task<ParameterMutation> LoadByKeyAsync(
            AzureTableDriverDynamic driver,
            StorageEntity<T> partial,
            ParameterInfo ownerParameter,
            IReadOnlyList<KeyValuePair<string, object>> bindings,
            IHttpRequest routeData,
            System.Threading.CancellationToken cancellationToken)
        {
            // cancellationToken: no driver overload accepts it today. A
            // sibling short-circuit lets this load complete; the orchestrator
            // observes the orphan in finally.
            _ = cancellationToken;

            var key = (AzureStorageTableStorageKey<T>)partial.Key;
            var captured = bindings ?? Array.Empty<KeyValuePair<string, object>>();

            // Identifier shown to clients on 404 / 500. Prefer wire-level
            // bindings (e.g. "id=<guid>"); fall back to the storage-shaped
            // key when no bindings were captured.
            string Describe() => captured.Count > 0
                ? string.Join(", ", captured.Select(kvp => $"{kvp.Key}={kvp.Value}"))
                : (string.Equals(key.RowKey, key.PartitionKey, StringComparison.Ordinal)
                    ? key.RowKey
                    : $"row='{key.RowKey}', partition='{key.PartitionKey}'");

            return await driver.LoadStorageEntityAsync<T, ParameterMutation>(
                key,
                onFound: full => (req, parms, @continue) =>
                {
                    var updated = parms
                        .Select(kvp => kvp.Key == ownerParameter
                            ? new KeyValuePair<ParameterInfo, object>(ownerParameter, full)
                            : kvp)
                        .ToArray();
                    return @continue(req, updated);
                },
                onNotFound: () => (req, parms, @continue) =>
                    routeData
                        .CreateResponse(HttpStatusCode.NotFound)
                        .AddReason($"{typeof(T).Name} not found ({Describe()})")
                        .AsTask(),
                onFailure: (code, msg) => (req, parms, @continue) =>
                    routeData
                        .CreateResponse(HttpStatusCode.InternalServerError)
                        .AddReason($"storage failure loading {typeof(T).Name} ({Describe()}): [{code}] {msg}")
                        .AsTask());
        }
    }
}

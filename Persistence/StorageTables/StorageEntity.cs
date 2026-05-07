using System;

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
    }
}

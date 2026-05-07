using System;

using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// A deserialized request-body entity together with the driver scoped to the
    /// datastore that should receive it. Constructed by parameter-binding
    /// attributes (e.g. <c>[StorableEntityFromResource]</c>) before the
    /// controller method body runs and consumed by extensions on
    /// <see cref="StorableEntityExtensions"/> (<c>MutateEntity</c>,
    /// <c>StorageInsertAsync</c>).
    /// </summary>
    /// <remarks>
    /// Pure data: the deserialized entity plus the driver scoped to the
    /// originating datastore. Mirror of <see cref="StorageEntity{T}"/> for the
    /// write side: where the read-side type bundles a key + loaded entity + the
    /// driver it was loaded from, this type bundles a body-deserialized entity
    /// + the driver it should be written to. Mutation is exposed as fluent
    /// extension methods (<see cref="StorableEntityExtensions.MutateEntity{T}"/>)
    /// rather than members on this class.
    /// </remarks>
    public sealed class StorableEntity<T>
        where T : IReferenceable
    {
        /// <summary>The deserialized entity.</summary>
        public T Entity { get; }

        /// <summary>
        /// Driver scoped to the datastore this entity should be written to.
        /// All write extensions route through this driver so per-parameter
        /// datastore overrides are honored.
        /// </summary>
        public AzureTableDriverDynamic Driver { get; }

        internal StorableEntity(T entity, AzureTableDriverDynamic driver)
        {
            this.Entity = entity;
            this.Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <summary>
        /// Returns a new <see cref="StorableEntity{T}"/> with the given entity
        /// and the same driver. Used by
        /// <see cref="StorableEntityExtensions.MutateEntity{T}"/>.
        /// </summary>
        internal StorableEntity<T> WithEntity(T entity) => new StorableEntity<T>(entity, this.Driver);

        /// <summary>
        /// Loader pipeline factory: wrap a body-deserialized entity together
        /// with its scoped driver into a <see cref="StorableEntity{T}"/>.
        /// Called by <c>[StorableEntityFromResource]</c>'s assemble closure
        /// (via generic dispatch over <typeparamref name="T"/>); not part of
        /// the surface controller bodies use.
        /// </summary>
        public static StorableEntity<T> FromDeserialized(T entity, AzureTableDriverDynamic driver)
            => new StorableEntity<T>(entity, driver);
    }
}

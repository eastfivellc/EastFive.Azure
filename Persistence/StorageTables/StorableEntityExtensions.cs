using System;
using System.Threading.Tasks;

using EastFive.Persistence.Azure;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Azure.Persistence.StorageTables
{
    public static class StorableEntityExtensions
    {
        /// <summary>
        /// Replaces the entity inside a <see cref="StorableEntity{T}"/> with
        /// the result of <paramref name="mutate"/>, preserving the scoped
        /// driver. Returns a new <see cref="StorableEntity{T}"/>; the input is
        /// unchanged.
        /// </summary>
        public static StorableEntity<T> MutateEntity<T>(this StorableEntity<T> source, Func<T, T> mutate)
            where T : IReferenceable
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            if (mutate is null)
                throw new ArgumentNullException(nameof(mutate));
            return source.WithEntity(mutate(source.Entity));
        }

        /// <summary>
        /// Inserts the entity through the scoped driver. Mirrors the queryable
        /// surface of <c>StorageInsertAsync</c> on <see cref="System.Linq.IQueryable{T}"/>
        /// so per-parameter datastore overrides are honored.
        /// </summary>
        public static Task<TResult> StorageInsertAsync<T, TResult>(
            this StorableEntity<T> source,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists = default,
            params IHandleFailedModifications<TResult>[] onModificationFailures)
            where T : IReferenceable
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            return source.Driver.CreateAsync(source.Entity,
                onSuccess: (e, tr) => onCreated(),
                onAlreadyExists: onAlreadyExists,
                onModificationFailures: onModificationFailures);
        }

        /// <summary>
        /// Overload that exposes the materialized
        /// <see cref="IAzureStorageTableEntity{T}"/> on success, for parity
        /// with the queryable-bound <c>StorageInsertAsync</c> surface.
        /// </summary>
        public static Task<TResult> StorageInsertAsync<T, TResult>(
            this StorableEntity<T> source,
            Func<IAzureStorageTableEntity<T>, TResult> onCreated,
            Func<TResult> onAlreadyExists = default,
            params IHandleFailedModifications<TResult>[] onModificationFailures)
            where T : IReferenceable
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            return source.Driver.CreateAsync(source.Entity,
                onSuccess: (e, tr) => onCreated(e),
                onAlreadyExists: onAlreadyExists,
                onModificationFailures: onModificationFailures);
        }
    }
}

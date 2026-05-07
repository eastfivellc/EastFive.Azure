using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Azure.Persistence.StorageTables
{
    public static class StorageEntityExtensions
    {
        /// <summary>
        /// Loads a single record from the specified <paramref name="driver"/> using
        /// the storage-shaped <paramref name="key"/> and wraps it in a
        /// <see cref="StorageEntity{T}"/>. Canonical entry point used by
        /// parameter-binding loader attributes and by hand-wired callers during
        /// the storage injection rollout.
        /// </summary>
        public static Task<TResult> LoadStorageEntityAsync<T, TResult>(
            this AzureTableDriverDynamic driver,
            AzureStorageTableStorageKey<T> key,
            Func<StorageEntity<T>, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            where T : IReferenceable
        {
            return driver.FindByIdAsync<T, TResult>(key.RowKey, key.PartitionKey,
                (entity, tableResult) =>
                {
                    var eTag = tableResult?.Etag;
                    DateTimeOffset? lastModified = tableResult is null
                        ? default
                        : (tableResult.Result is ITableEntity te ? te.Timestamp : default(DateTimeOffset?));
                    var stored = new StorageEntity<T>(key, entity, eTag, lastModified, driver);
                    return onFound(stored);
                },
                onNotFound: onNotFound,
                onFailure: onFailure);
        }

        /// <summary>
        /// Convenience overload for callers that already hold an
        /// <see cref="IRef{TEntity}"/>: computes the storage key from the
        /// reference and forwards to the primary overload.
        /// </summary>
        public static Task<TResult> LoadStorageEntityAsync<T, TResult>(
            this AzureTableDriverDynamic driver,
            IRef<T> entityRef,
            Func<StorageEntity<T>, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            where T : IReferenceable
        {
            var (rowKey, partitionKey) = entityRef.StorageComputeRowAndPartitionKey();
            var key = new AzureStorageTableStorageKey<T>(rowKey, partitionKey);
            return driver.LoadStorageEntityAsync<T, TResult>(key, onFound, onNotFound, onFailure);
        }

        /// <summary>
        /// Mutates the underlying record. The <paramref name="onUpdate"/> callback receives the
        /// freshly-fetched record and a save delegate; calling <c>saveAsync(record)</c> inside the
        /// callback persists the change.
        /// </summary>
        /// <remarks>
        /// On an eTag conflict the underlying driver re-fetches the record and re-invokes
        /// <paramref name="onUpdate"/> automatically; the caller does not see conflicts. If the
        /// callback returns without invoking the save delegate, no write occurs.
        /// </remarks>
        public static Task<TResult> UpdateAsync<T, TResult>(
            this StorageEntity<T> stored,
            Func<T, Func<T, Task<IUpdateTableResult>>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default,
            IHandleFailedModifications<TResult>[] onModificationFailures = default)
            where T : IReferenceable
        {
            if (stored.TestBackend != null)
                return stored.TestBackend.UpdateAsync<TResult>(
                    stored.Entity,
                    onUpdate,
                    onNotFound ?? (() => default(TResult)));
            var key = (AzureStorageTableStorageKey<T>)stored.Key;
            return stored.Driver.UpdateAsync<T, TResult>(key.RowKey, key.PartitionKey,
                onUpdate: (entity, callback) =>
                {
                    return onUpdate(entity,
                        async (entityToSave) =>
                        {
                            var tr = await callback(entityToSave);
                            return new StorageUpdateTableResult(tr);
                        });
                },
                onNotFound: onNotFound,
                onModificationFailures: onModificationFailures);
        }

        /// <summary>
        /// Deletes the underlying record.
        /// </summary>
        public static Task<TResult> DeleteAsync<T, TResult>(
            this StorageEntity<T> stored,
            Func<T, TResult> onDeleted,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            where T : IReferenceable
        {
            if (stored.TestBackend != null)
                return stored.TestBackend.DeleteAsync<TResult>(
                    stored.Entity,
                    onDeleted,
                    onNotFound ?? (() => default(TResult)));
            var key = (AzureStorageTableStorageKey<T>)stored.Key;
            return stored.Driver.DeleteAsync<T, TResult>(key.RowKey, key.PartitionKey,
                onSuccess: onDeleted,
                onNotFound: onNotFound,
                onFailure: onFailure);
        }
    }
}


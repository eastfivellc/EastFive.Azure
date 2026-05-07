using System;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// Intercept point for test code that needs to construct a
    /// <see cref="StorageEntity{T}"/> backed by an in-memory record rather
    /// than a real <c>AzureTableDriverDynamic</c>. Production code never
    /// implements or observes this interface — the
    /// <see cref="StorageEntityExtensions.UpdateAsync{T, TResult}"/> /
    /// <see cref="StorageEntityExtensions.DeleteAsync{T, TResult}"/> extensions
    /// short-circuit through the backend when one is attached, otherwise they
    /// route through <see cref="StorageEntity{T}.Driver"/> exactly as before.
    /// </summary>
    public interface IStorageEntityTestBackend<T>
        where T : IReferenceable
    {
        Task<TResult> UpdateAsync<TResult>(
            T currentEntity,
            Func<T, Func<T, Task<EastFive.Azure.Persistence.IUpdateTableResult>>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound);

        Task<TResult> DeleteAsync<TResult>(
            T currentEntity,
            Func<T, TResult> onDeleted,
            Func<TResult> onNotFound);
    }

    /// <summary>
    /// Public factory used by test harnesses to construct a
    /// <see cref="StorageEntity{T}"/> wrapping an in-memory record. The
    /// returned instance behaves exactly like a driver-backed one for
    /// callers; mutations are intercepted by <paramref name="backend"/>.
    /// </summary>
    public static class StorageEntityTestSupport
    {
        public static StorageEntity<T> CreateForTesting<T>(
            IStorageKey<T> key,
            T entity,
            IStorageEntityTestBackend<T> backend)
            where T : IReferenceable
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (backend is null) throw new ArgumentNullException(nameof(backend));
            return StorageEntity<T>.CreateWithTestBackend(key, entity, backend);
        }
    }

    internal sealed class StorageTestUpdateResult : EastFive.Azure.Persistence.IUpdateTableResult
    {
        public StorageTestUpdateResult(string eTag) { this.ETag = eTag; }
        public string ETag { get; }
    }
}

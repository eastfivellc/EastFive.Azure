using System;
using System.Threading.Tasks;

using EastFive.Api;

namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// V3 PATCH glue between
    /// <see cref="EastFive.Api.Binding.MutateEntityAttribute"/>-bound
    /// <see cref="MutateResource{T}"/> delegates and the loaded
    /// <see cref="StorageEntity{T}"/>. Applies the mutator to the current
    /// entity inside the driver's eTag-retry loop and saves.
    /// </summary>
    public static class StorageEntityMutateExtensions
    {
        /// <summary>
        /// Applies <paramref name="mutate"/> to the loaded entity and persists
        /// the result. Convenience wrapper over
        /// <c>StorageEntityExtensions.UpdateAsync</c> that surfaces the mutated
        /// value to <paramref name="onUpdated"/>.
        /// </summary>
        public static Task<TResult> StorageMutateAsync<T, TResult>(
            this StorageEntity<T> stored,
            MutateResource<T> mutate,
            Func<T, TResult> onUpdated,
            Func<TResult> onNotFound = default)
            where T : IReferenceable
        {
            if (stored is null) throw new ArgumentNullException(nameof(stored));
            if (mutate is null) throw new ArgumentNullException(nameof(mutate));
            return stored.UpdateAsync<T, TResult>(
                onUpdate: async (entity, saveAsync) =>
                {
                    var mutated = mutate(entity);
                    await saveAsync(mutated);
                    return onUpdated(mutated);
                },
                onNotFound: onNotFound);
        }
    }
}

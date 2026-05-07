using System;

namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// Storage-shaped lookup key for an Azure Table Storage entity:
    /// the literal <see cref="RowKey"/> + <see cref="PartitionKey"/> pair
    /// the driver hands to <c>FindByIdAsync</c>.
    /// </summary>
    /// <remarks>
    /// This is the only key shape the table driver knows how to load, so
    /// <c>LoadStorageEntityAsync</c> can dispatch directly without runtime
    /// type-switching on <see cref="IStorageKey{TEntity}"/>. Other datastores
    /// (SQL, Cosmos, blob) get their own concrete <c>IStorageKey&lt;T&gt;</c>
    /// implementation paired with a driver overload that consumes it.
    ///
    /// Construct via the driver's <c>ComputeStorageKey</c> method rather than
    /// calling the constructor directly — the driver method encapsulates the
    /// row/partition computation.
    /// </remarks>
    public sealed class AzureStorageTableStorageKey<TEntity> : IStorageKey<TEntity>
    {
        public string RowKey { get; }

        public string PartitionKey { get; }

        public AzureStorageTableStorageKey(string rowKey, string partitionKey)
        {
            this.RowKey = rowKey ?? throw new ArgumentNullException(nameof(rowKey));
            this.PartitionKey = partitionKey ?? throw new ArgumentNullException(nameof(partitionKey));
        }
    }
}


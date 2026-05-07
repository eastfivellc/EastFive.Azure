namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// Marker for a storage-shaped lookup key for entity type
    /// <typeparamref name="TEntity"/>. Each driver knows the concrete key
    /// shape it can serve and exposes a <c>LoadStorageEntityAsync</c>
    /// overload that takes that shape directly:
    /// <list type="bullet">
    ///   <item><see cref="AzureStorageTableStorageKey{TEntity}"/> — row+partition
    ///         pair consumed by <c>AzureTableDriverDynamic</c>.</item>
    ///   <item>(future) SQL primary key, Cosmos id+partition, blob name,
    ///         composite, etc.</item>
    /// </list>
    /// MIXING CONCERN: this interface is intentionally storage-agnostic at
    /// the type level so <see cref="StorageEntity{TEntity}.Key"/> can stay
    /// driver-agnostic in the public surface. Drivers cast to the concrete
    /// type they need internally; a key produced for the wrong driver fails
    /// the cast at load time.
    /// </summary>
    public interface IStorageKey<TEntity>
    {
    }
}

using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Azure.Persistence.StorageTables
{
    /// <summary>
    /// Test-only factory exposing the otherwise-internal
    /// <see cref="StorableEntity{T}"/> constructor so test code can build a
    /// write-side entity bound to a real
    /// <see cref="AzureTableDriverDynamic"/>. Production code never calls
    /// this; production binding goes through
    /// <c>[StorableEntityFromResource]</c> instead.
    /// </summary>
    public static class StorableEntityTestSupport
    {
        public static StorableEntity<T> CreateLive<T>(AzureTableDriverDynamic driver, T entity)
            where T : IReferenceable
            => new StorableEntity<T>(entity, driver);
    }
}

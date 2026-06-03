using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Binding
{
    /// <summary>Scope marker for Azure Table Storage row persistence. Members
    /// declared bindable in this scope (e.g. via <c>[Column]</c> /
    /// <c>[StorageProperty]</c>) participate in row read/write.</summary>
    public sealed class StorageRow : IMemberScope { }
}

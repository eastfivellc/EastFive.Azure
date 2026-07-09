using System.Runtime.CompilerServices;

using EastFive.Api.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Registers the storage-flavoured <see cref="EastFive.Serialization.Binding.ITypeBinder"/>s
    /// with the V3 dispatcher's <see cref="TypeBinderRegistry"/> at module load.
    /// </summary>
    internal static class StorageBinderModuleInitializer
    {
        #pragma warning disable CA2255 // intentional: framework-level binder registration
        [ModuleInitializer]
        #pragma warning restore CA2255
        internal static void RegisterBinders()
        {
            TypeBinderRegistry.Register(new StorableEntityBinder());
            TypeBinderRegistry.Register(new StorageQueryBinder());
            TypeBinderRegistry.Register(new StorageEntityBinder());
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using EastFive.Api.Binding;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// <see cref="ITypeBinder"/> that materializes a parameter typed
    /// <c>IQueryable&lt;T&gt;</c> backed by a <see cref="StorageQuery{T}"/>.
    /// The <c>[StorageEntities]</c> selection attribute (rewritten in Phase E)
    /// supplies a <see cref="StorageBoundSource"/> carrying the scoped driver
    /// and an empty inner source; this binder probes for the wrapper and
    /// constructs <c>new StorageQuery&lt;T&gt;(driver)</c>.
    /// <para>
    /// Guarded by source identity: <see cref="CanBind"/> returns true for any
    /// closed <c>IQueryable&lt;T&gt;</c>, but <see cref="Read"/> only succeeds
    /// when the source is a <see cref="StorageBoundSource"/>.
    /// </para>
    /// </summary>
    public sealed class StorageQueryBinder : ITypeBinder
    {
        public bool CanBind(Type targetType) =>
            targetType is not null
            && targetType.IsGenericType
            && targetType.GetGenericTypeDefinition() == typeof(IQueryable<>);

        public async ValueTask<TResult> Read<TResult>(
            Type targetType,
            IBindingSource source,
            IBindingContext context,
            Func<object, TResult> onBound,
            Func<BindFailure, TResult> onFailure,
            Func<TResult> onNull = null)
        {
            var resourceType = targetType.GenericTypeArguments[0];
            var keyPath = context?.KeyPath ?? string.Empty;

            var probed = await source.GetValue<object>(
                path: keyPath,
                onObject: outer => outer is StorageBoundSource s
                    ? (object)s
                    : new BindFailure(
                        new ParseError("StorageQueryBinder expects a StorageBoundSource via onObject."),
                        targetType, keyPath),
                onFailure: f => (object)f);
            if (probed is BindFailure failure)
                return onFailure(failure);
            var storageSrc = (StorageBoundSource)probed;

            var queryType = typeof(StorageQuery<>).MakeGenericType(resourceType);
            var ctor = queryType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(AzureTableDriverDynamic) },
                modifiers: null);
            if (ctor is null)
                return onFailure(new BindFailure(
                    new ParseError($"{queryType.FullName} has no (AzureTableDriverDynamic) constructor."),
                    targetType, keyPath));

            var query = ctor.Invoke(new object[] { storageSrc.Driver });
            return onBound(query);
        }

        public void Write(Type sourceType, object value, IBindingSink sink, IBindingContext context) =>
            throw new NotSupportedException(
                $"{nameof(StorageQueryBinder)} is read-only.");
    }
}

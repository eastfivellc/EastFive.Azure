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
    /// The <c>[StorageEntities]</c> selection attribute emits a data-free
    /// <see cref="BindCall"/>; this binder ignores the source content and
    /// resolves the per-parameter <see cref="AzureTableDriverDynamic"/> lazily
    /// via the <see cref="ParameterSlot"/> on the binding context, then
    /// constructs <c>new StorageQuery&lt;T&gt;(driver)</c>.
    /// </summary>
    public sealed class StorageQueryBinder : ITypeBinder
    {
        public bool CanBind(Type targetType) =>
            targetType is not null
            && targetType.IsGenericType
            && targetType.GetGenericTypeDefinition() == typeof(IQueryable<>);

        public ValueTask<TResult> Read<TResult>(
            Type targetType,
            IBindingSource source,
            IBindingContext context,
            Func<object, TResult> onBound,
            Func<BindFailure, TResult> onFailure,
            Func<TResult> onNull = null)
        {
            var resourceType = targetType.GenericTypeArguments[0];
            var keyPath = context?.KeyPath ?? string.Empty;

            var parameter = (context?.Slot as ParameterSlot)?.Parameter;
            if (parameter is null)
                return new ValueTask<TResult>(onFailure(new BindFailure(
                    new ParseError(
                        $"{nameof(StorageQueryBinder)} requires a {nameof(ParameterSlot)} on the binding context " +
                        $"to resolve the per-parameter storage driver."),
                    targetType, keyPath)));

            var driver = StorageDriverScope.Resolve(parameter).GetDriver();

            var queryType = typeof(StorageQuery<>).MakeGenericType(resourceType);
            var ctor = queryType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(AzureTableDriverDynamic) },
                modifiers: null);
            if (ctor is null)
                return new ValueTask<TResult>(onFailure(new BindFailure(
                    new ParseError($"{queryType.FullName} has no (AzureTableDriverDynamic) constructor."),
                    targetType, keyPath)));

            var query = ctor.Invoke(new object[] { driver });
            return new ValueTask<TResult>(onBound(query));
        }

        public void Write(Type sourceType, object value, IBindingSink sink, IBindingContext context) =>
            throw new NotSupportedException(
                $"{nameof(StorageQueryBinder)} is read-only.");
    }
}

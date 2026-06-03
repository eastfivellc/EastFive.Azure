using System;
using System.Reflection;
using System.Threading.Tasks;

using EastFive.Api.Binding;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// <see cref="ITypeBinder"/> that materializes a parameter typed
    /// <c>StorableEntity&lt;T&gt;</c> from the request body.
    /// <para>
    /// Resolves the per-parameter <see cref="AzureTableDriverDynamic"/> via the
    /// <see cref="ParameterSlot"/> on the binding context — no driver smuggling
    /// through the binding source. Entity materialization recurses through
    /// <see cref="ITypeBindings.Bind"/>: <c>PocoBinder</c> (or any registered
    /// type-specific binder) walks the inner <see cref="IBindingSource"/> under
    /// the <see cref="EastFive.Api.Binding.Scopes.RequestBody"/> scope declared
    /// by <see cref="StorableEntityFromResourceAttribute"/>.
    /// </para>
    /// </summary>
    public sealed class StorableEntityBinder : ITypeBinder
    {
        public bool CanBind(Type targetType) =>
            StorageBindingHelpers.ExtractEntityType(targetType, typeof(StorableEntity<>)) != null;

        public ValueTask<TResult> Read<TResult>(
            Type targetType,
            IBindingSource source,
            IBindingContext context,
            Func<object, TResult> onBound,
            Func<BindFailure, TResult> onFailure,
            Func<TResult> onNull = null)
        {
            var entityType = StorageBindingHelpers.ExtractEntityType(targetType, typeof(StorableEntity<>));
            if (entityType is null)
                return new ValueTask<TResult>(onFailure(new BindFailure(
                    new UnsupportedTargetType(targetType), targetType, context?.KeyPath ?? string.Empty)));

            var keyPath = context?.KeyPath ?? string.Empty;

            var parameter = (context?.Slot as ParameterSlot)?.Parameter;
            if (parameter is null)
                return new ValueTask<TResult>(onFailure(new BindFailure(
                    new ParseError(
                        $"{nameof(StorableEntityBinder)} requires a {nameof(ParameterSlot)} on the binding context " +
                        $"to resolve the per-parameter storage driver."),
                    targetType, keyPath)));

            var driver = StorageDriverScope.Resolve(parameter).GetDriver();

            // Recurse into the request body via the registered type bindings.
            // PocoBinder (or any type-specific binder) walks the inner
            // IBindingSource under the RequestBody scope.
            return context.TypeBindings.Bind<TResult>(
                entityType, source, context,
                entity => entity is null
                    ? (onNull is not null
                        ? onNull()
                        : onFailure(new BindFailure(new NullValue(), targetType, keyPath)))
                    : onBound(WrapStorable(entity, entityType, driver)),
                onFailure,
                onNull);
        }

        private static object WrapStorable(
            object entity, Type entityType, AzureTableDriverDynamic driver)
        {
            var factory = typeof(StorableEntity<>)
                .MakeGenericType(entityType)
                .GetMethod(
                    nameof(StorableEntity<IReferenceable>.FromDeserialized),
                    BindingFlags.Public | BindingFlags.Static);
            return factory.Invoke(null, new object[] { entity, driver });
        }

        public void Write(Type sourceType, object value, IBindingSink sink, IBindingContext context) =>
            throw new NotSupportedException(
                $"{nameof(StorableEntityBinder)} is read-only — write side uses dedicated extensions.");
    }
}

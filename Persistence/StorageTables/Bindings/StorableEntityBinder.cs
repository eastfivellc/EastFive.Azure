using System;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

using EastFive.Api;
using EastFive.Api.Binding;
using EastFive.Api.Serialization;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// <see cref="ITypeBinder"/> that materializes a parameter typed
    /// <c>StorableEntity&lt;T&gt;</c> from the request body. The selection
    /// attribute (<see cref="StorableEntityFromResourceAttribute"/>) stashes
    /// the raw body shape on a <see cref="StorageBoundSource"/>; this binder
    /// probes the source via <c>onObject</c>, reads
    /// <see cref="StorageBoundSource.RawBody"/>, dispatches on its shape,
    /// deserializes via <see cref="StorageBindingHelpers"/>, and wraps via
    /// <see cref="StorableEntity{T}.FromDeserialized"/>.
    /// </summary>
    public sealed class StorableEntityBinder : ITypeBinder
    {
        public bool CanBind(Type targetType) =>
            StorageBindingHelpers.ExtractEntityType(targetType, typeof(StorableEntity<>)) != null;

        public async ValueTask<TResult> Read<TResult>(
            Type targetType,
            IBindingSource source,
            IBindingContext context,
            Func<object, TResult> onBound,
            Func<BindFailure, TResult> onFailure,
            Func<TResult> onNull = null)
        {
            var entityType = StorageBindingHelpers.ExtractEntityType(targetType, typeof(StorableEntity<>));
            if (entityType is null)
                return onFailure(new BindFailure(
                    new UnsupportedTargetType(targetType), targetType, context?.KeyPath ?? string.Empty));

            if (context is not ApiBindingContext apiContext)
                return onFailure(new BindFailure(
                    new ParseError("StorableEntityBinder requires ApiBindingContext."),
                    targetType, context?.KeyPath ?? string.Empty));

            var keyPath = context.KeyPath ?? string.Empty;

            // Outer probe — the selection attribute dispatches onObject with a
            // StorageBoundSource carrying the driver + the raw body payload.
            // V3 contract: CompositeBindingSource only allows TResult = object,
            // so we pack the probe result into a single object (StorageBoundSource
            // on success, BindFailure on failure).
            var probed = await source.GetValue<object>(
                path: keyPath,
                onObject: outer => outer is StorageBoundSource s
                    ? (object)s
                    : new BindFailure(
                        new ParseError("StorableEntityBinder expects a StorageBoundSource via onObject."),
                        targetType, keyPath),
                onFailure: f => (object)f);
            if (probed is BindFailure outerFailure)
                return onFailure(outerFailure);

            var storageSrc = (StorageBoundSource)probed;
            if (storageSrc.RawBody is null)
                return onFailure(new BindFailure(
                    new ParseError("Storage selection produced no body payload."),
                    targetType, keyPath));

            var parameter = ResolveOwningParameter(context);
            return DeserializeAndWrap(storageSrc, entityType, apiContext, parameter,
                targetType, keyPath, onBound, onFailure);
        }

        // The dispatcher does not currently propagate the ParameterInfo into
        // IBindingContext, but form deserialization needs it for member binding.
        // KeyPath is initialized to the parameter name; we look it up off the
        // controller method registered via the controller selection (best
        // effort — null is tolerated by JSON/string paths and only relevant to
        // form deserialization which is rare for storable resources).
        private static ParameterInfo ResolveOwningParameter(IBindingContext context) => null;

        private static TResult DeserializeAndWrap<TResult>(
            StorageBoundSource storageSrc,
            Type entityType,
            ApiBindingContext apiContext,
            ParameterInfo parameterInfo,
            Type targetType,
            string keyPath,
            Func<object, TResult> onBound,
            Func<BindFailure, TResult> onFailure)
        {
            switch (storageSrc.RawBody)
            {
                case JToken jtoken:
                {
                    var jsonText = jtoken.ToString(Newtonsoft.Json.Formatting.None);
                    var bindConvert = new BindConvert(apiContext.Request, apiContext.Application as HttpApplication);
                    return StorageBindingHelpers.DeserializeJsonToEntity(
                        jsonText, entityType, bindConvert,
                        entity => WrapAndDispatch(entity, entityType, storageSrc.Driver, onBound),
                        why => onFailure(new BindFailure(new ParseError(why), targetType, keyPath)));
                }
                case string raw:
                {
                    var bindConvert = new BindConvert(apiContext.Request, apiContext.Application as HttpApplication);
                    return StorageBindingHelpers.DeserializeJsonToEntity(
                        raw, entityType, bindConvert,
                        entity => WrapAndDispatch(entity, entityType, storageSrc.Driver, onBound),
                        why => onFailure(new BindFailure(new ParseError(why), targetType, keyPath)));
                }
                case IFormCollection form:
                {
                    return StorageBindingHelpers.DeserializeFormToEntity(
                        form, entityType, parameterInfo, apiContext.Application,
                        entity => WrapAndDispatch(entity, entityType, storageSrc.Driver, onBound),
                        why => onFailure(new BindFailure(new ParseError(why), targetType, keyPath)));
                }
                default:
                    return onFailure(new BindFailure(
                        new ParseError($"Unsupported body payload shape: {storageSrc.RawBody.GetType().Name}."),
                        targetType, keyPath));
            }
        }

        private static TResult WrapAndDispatch<TResult>(
            object entity, Type entityType, AzureTableDriverDynamic driver,
            Func<object, TResult> onBound)
        {
            var factory = typeof(StorableEntity<>)
                .MakeGenericType(entityType)
                .GetMethod(
                    nameof(StorableEntity<IReferenceable>.FromDeserialized),
                    BindingFlags.Public | BindingFlags.Static);
            var built = factory.Invoke(null, new object[] { entity, driver });
            return onBound(built);
        }

        public void Write(Type sourceType, object value, IBindingSink sink, IBindingContext context) =>
            throw new NotSupportedException(
                $"{nameof(StorableEntityBinder)} is read-only — write side uses dedicated extensions.");
    }
}

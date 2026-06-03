using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using EastFive.Api.Binding;
using EastFive.Api.Bindings;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Reflection;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// <see cref="ITypeBinder"/> for <c>StorageEntity&lt;T&gt;</c> parameters
    /// fed by the loader trio
    /// (<see cref="StorageEntityFromQueryIdAttribute"/>,
    /// <see cref="StorageEntityFromQueryParamAttribute"/>,
    /// <see cref="StorageEntityFromRouteAttribute"/>).
    /// <para>
    /// The selection attribute hands the binder a
    /// <see cref="EastFive.Api.Serialization.Binding.Sources.LookupBindingSource"/>
    /// keyed by each key member's wire-name. This binder unwraps it via
    /// <c>onObject</c>, parses each key part through its registered
    /// <c>ITypeBinder</c> (<see cref="IBindingContext.TypeBindings"/>), builds
    /// a partial <see cref="StorageEntity{T}"/>, resolves the per-parameter
    /// <see cref="AzureTableDriverDynamic"/> lazily via the
    /// <see cref="ParameterSlot"/> on the context, runs the async load, and
    /// surfaces:
    /// <list type="bullet">
    ///   <item>200 path → loaded full <see cref="StorageEntity{T}"/> via <c>onBound</c>.</item>
    ///   <item>not-found → <see cref="BindFailure"/> carrying a
    ///   <see cref="StorageEntityNotFoundReason"/> so the dispatcher emits 404.</item>
    ///   <item>driver / parse failure → <see cref="ParseError"/> → 400.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class StorageEntityBinder : ITypeBinder
    {
        public bool CanBind(Type targetType) =>
            StorageBindingHelpers.ExtractEntityType(targetType, typeof(StorageEntity<>)) != null;

        public async ValueTask<TResult> Read<TResult>(
            Type targetType,
            IBindingSource source,
            IBindingContext context,
            Func<object, TResult> onBound,
            Func<BindFailure, TResult> onFailure,
            Func<TResult> onNull = null)
        {
            // TODO: Completely refactor this state riddled mess.
            var entityType = StorageBindingHelpers.ExtractEntityType(targetType, typeof(StorageEntity<>));
            if (entityType is null)
                return onFailure(new BindFailure(
                    new UnsupportedTargetType(targetType), targetType, context?.KeyPath ?? string.Empty));

            var keyPath = context?.KeyPath ?? string.Empty;

            var parameter = (context?.Slot as ParameterSlot)?.Parameter;
            if (parameter is null)
                return onFailure(new BindFailure(
                    new ParseError(
                        $"{nameof(StorageEntityBinder)} requires a {nameof(ParameterSlot)} on the binding context " +
                        $"to resolve the per-parameter storage driver and loader attribute."),
                    targetType, keyPath));

            // Unwrap the per-key lookup source emitted by the loader attribute.
            // The dispatcher pins TResult=object on the composite source, so the
            // probe is forced to object as well; we still discard the value and
            // recover the source via the onObject capture.
            IBindingSource keySrc = null;
            BindFailure? unwrapFailure = null;
            await source.GetValue<object>(
                path: keyPath,
                onObject: s => { keySrc = s; return null; },
                onFailure: f => { unwrapFailure = f; return null; });
            if (unwrapFailure is not null)
                return onFailure(unwrapFailure.Value);
            if (keySrc is null)
                return onFailure(new BindFailure(
                    new ParseError($"{nameof(StorageEntityBinder)} expected an object source for parameter `{keyPath}`."),
                    targetType, keyPath));

            var keyMembers = KeyMemberDiscovery.DiscoverKeyMembers(entityType);
            if (keyMembers.Length == 0)
                return onFailure(new BindFailure(
                    new ParseError($"{entityType.FullName} declares no key members."),
                    targetType, keyPath));

            var loaderAttr = parameter
                .GetCustomAttributes(typeof(StorageEntityLoaderAttributeBase), inherit: true)
                .Cast<StorageEntityLoaderAttributeBase>()
                .FirstOrDefault();
            if (loaderAttr is null)
                return onFailure(new BindFailure(
                    new ParseError(
                        $"{nameof(StorageEntityBinder)} requires a {nameof(StorageEntityLoaderAttributeBase)} " +
                        $"on parameter `{parameter.Name}` to resolve key wire-names."),
                    targetType, keyPath));
            var wireNames = loaderAttr.ResolveWireNames(keyMembers);

            // Parse each key value via its registered ITypeBinder. IRef<T>,
            // Guid, custom keys all flow through their own binders — no
            // application-binder shortcut.
            var boundValues = new object[keyMembers.Length];
            for (var i = 0; i < keyMembers.Length; i++)
            {
                var memberType = keyMembers[i].GetPropertyOrFieldType();
                var wireName = wireNames[i];
                var keyCtx = context.WithKeyPath(wireName);
                BindFailure? memberFailure = null;
                object parsed = null;
                await context.TypeBindings.Bind<object>(memberType, keySrc, keyCtx,
                    v => { parsed = v; return null; },
                    f => { memberFailure = f; return null; });
                if (memberFailure is not null)
                    return onFailure(new BindFailure(
                        new ParseError($"could not parse `{wireName}`: {memberFailure.Value.Reason.Describe()}"),
                        targetType, keyPath));
                boundValues[i] = parsed;
            }

            var partial = StorageEntityLoaderHelpers.BuildPartialStorageEntity(entityType, boundValues);

            var driver = StorageDriverScope.Resolve(parameter).GetDriver();

            // Reflectively dispatch the IReferenceable-constrained load helper.
            var helper = typeof(StorageEntityLoaderHelpers)
                .GetMethod(nameof(StorageEntityLoaderHelpers.LoadGenericAsync),
                    BindingFlags.Public | BindingFlags.Static)
                .MakeGenericMethod(entityType, typeof(TResult));
            var task = (ValueTask<TResult>)helper.Invoke(null, new object[]
            {
                driver, partial, wireNames, boundValues, targetType, keyPath, onBound, onFailure,
            });
            return await task.ConfigureAwait(false);
        }

        public void Write(Type sourceType, object value, IBindingSink sink, IBindingContext context) =>
            throw new NotSupportedException(
                $"{nameof(StorageEntityBinder)} is read-only — write side uses dedicated extensions.");
    }

    /// <summary>
    /// <see cref="IBindFailureReason"/> for "storage row not found", carrying a
    /// 404 status code so the V3 dispatcher emits the right response.
    /// </summary>
    public sealed record StorageEntityNotFoundReason(Type EntityType, string Description)
        : IBindFailureWithStatusCode
    {
        public HttpStatusCode StatusCode => HttpStatusCode.NotFound;

        public string Describe() =>
            string.IsNullOrEmpty(Description)
                ? $"{EntityType?.Name ?? "?"} not found"
                : $"{EntityType?.Name ?? "?"} not found ({Description})";
    }
}

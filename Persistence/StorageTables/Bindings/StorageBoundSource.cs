using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// <see cref="IBindingSource"/> wrapper that flows the per-parameter
    /// storage driver to a TypeBinder. The V3 storage selection attributes
    /// (<c>[StorableEntityFromResource]</c>, <c>[StorageEntityFromQueryId]</c> /
    /// <c>QueryParam</c> / <c>Route</c>, <c>[StorageEntities]</c>) resolve the
    /// driver eagerly via <see cref="StorageDriverScope.Resolve"/> inside
    /// <c>TrySelectSource</c> (where the <see cref="System.Reflection.ParameterInfo"/>
    /// is in scope), wrap the inner binding source in this type, and close
    /// over the wrapped instance in the <c>BindCall</c>.
    /// <para>
    /// The matching TypeBinder (<c>StorableEntityBinder</c> /
    /// <c>StorageEntityBinder</c> / <c>StorageQueryBinder</c>) downcasts the
    /// source it receives via <see cref="IBindingSource.GetValue{TResult}"/>'s
    /// <c>onObject</c> callback and reads <see cref="Driver"/>. Non-storage
    /// binders ignore this type entirely — every <c>GetValue</c> call is
    /// proxied verbatim to <see cref="Inner"/>, so the wrapper is transparent
    /// to the rest of the binding pipeline.
    /// </para>
    /// </summary>
    public sealed class StorageBoundSource : IBindingSource
    {
        private static readonly IBindingSource emptyInner = new EmptySource();

        public StorageBoundSource(IBindingSource inner, AzureTableDriverDynamic driver,
            object rawBody = null)
        {
            Inner = inner ?? emptyInner;
            Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            RawBody = rawBody;
        }

        /// <summary>
        /// The underlying request-derived source (e.g. body, query composite,
        /// route composite). Never null — defaults to <see cref="EmptySource"/>
        /// when the attribute has no request input (e.g. <c>[StorageEntities]</c>).
        /// </summary>
        public IBindingSource Inner { get; }

        /// <summary>
        /// Per-parameter storage driver resolved at selection time via
        /// <see cref="StorageDriverScope"/>. Never null.
        /// </summary>
        public AzureTableDriverDynamic Driver { get; }

        /// <summary>
        /// Optional raw body payload stashed at selection time for binders that
        /// need format-specific access (e.g. <see cref="StorableEntityBinder"/>
        /// hands the JContainer / IFormCollection / string to
        /// <see cref="StorageBindingHelpers"/> which uses Newtonsoft's
        /// <c>JsonConvert</c> + <c>BindConvert</c> for app-aware deserialization).
        /// Null for attributes that consume no body (loader attributes,
        /// <c>[StorageEntities]</c>).
        /// </summary>
        public object RawBody { get; }

        public ValueTask<TResult> GetValue<TResult>(
            string path = null,
            Func<TResult> onNull = null,
            Func<string, TResult> onString = null,
            Func<Guid, TResult> onGuid = null,
            Func<bool, TResult> onBool = null,
            Func<long, TResult> onInt64 = null,
            Func<double, TResult> onDouble = null,
            Func<DateTime, TResult> onDateTime = null,
            Func<byte[], TResult> onBytes = null,
            Func<IBindingSource, TResult> onObject = null,
            Func<IEnumerableBindingSource, TResult> onArray = null,
            Type elementTypeHint = null,
            Func<BindFailure, TResult> onFailure = null) =>
            Inner.GetValue<TResult>(
                path, onNull, onString, onGuid, onBool, onInt64, onDouble, onDateTime,
                onBytes, onObject, onArray, elementTypeHint, onFailure);

        /// <summary>
        /// Trivial source that reports <see cref="NotPresent"/> for every
        /// access. Used as the inner for attributes that consume no request
        /// input (e.g. <c>[StorageEntities]</c>) but still need to surface the
        /// driver through this wrapper.
        /// </summary>
        private sealed class EmptySource : IBindingSource
        {
            public ValueTask<TResult> GetValue<TResult>(
                string path = null,
                Func<TResult> onNull = null,
                Func<string, TResult> onString = null,
                Func<Guid, TResult> onGuid = null,
                Func<bool, TResult> onBool = null,
                Func<long, TResult> onInt64 = null,
                Func<double, TResult> onDouble = null,
                Func<DateTime, TResult> onDateTime = null,
                Func<byte[], TResult> onBytes = null,
                Func<IBindingSource, TResult> onObject = null,
                Func<IEnumerableBindingSource, TResult> onArray = null,
                Type elementTypeHint = null,
                Func<BindFailure, TResult> onFailure = null) =>
                BindingSourceDispatch.FailTask(
                    new BindFailure(new NotPresent(), typeof(object), path ?? string.Empty),
                    onFailure);
        }
    }
}

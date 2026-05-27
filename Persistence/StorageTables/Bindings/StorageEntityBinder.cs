using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using EastFive.Api;
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
    /// The selection attribute resolves the driver eagerly and stashes each
    /// key wire-name's raw string value on a
    /// <see cref="StorageBoundSource"/> (via
    /// <see cref="StorageBoundSource.RawBody"/> as a
    /// <see cref="IReadOnlyList{KeyValuePair}"/>). This binder
    /// probes the source, parses each key part via the application's binder,
    /// builds a partial <see cref="StorageEntity{T}"/>, runs the async load,
    /// and surfaces:
    /// <list type="bullet">
    ///   <item>200 path → loaded full <see cref="StorageEntity{T}"/> via <c>onBound</c>.</item>
    ///   <item>not-found → <see cref="BindFailure"/> carrying a
    ///   <see cref="StorageEntityNotFoundReason"/> so the dispatcher emits 404.</item>
    ///   <item>driver failure → <see cref="ParseError"/> → 400.</item>
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
            var entityType = StorageBindingHelpers.ExtractEntityType(targetType, typeof(StorageEntity<>));
            if (entityType is null)
                return onFailure(new BindFailure(
                    new UnsupportedTargetType(targetType), targetType, context?.KeyPath ?? string.Empty));

            if (context is not ApiBindingContext apiContext)
                return onFailure(new BindFailure(
                    new ParseError($"{nameof(StorageEntityBinder)} requires {nameof(ApiBindingContext)}."),
                    targetType, context?.KeyPath ?? string.Empty));

            var keyPath = context.KeyPath ?? string.Empty;

            var probed = await source.GetValue<object>(
                path: keyPath,
                onObject: outer => outer is StorageBoundSource s
                    ? (object)s
                    : new BindFailure(
                        new ParseError($"{nameof(StorageEntityBinder)} expects a {nameof(StorageBoundSource)} via onObject."),
                        targetType, keyPath),
                onFailure: f => (object)f);
            if (probed is BindFailure outerFailure)
                return onFailure(outerFailure);

            var storageSrc = (StorageBoundSource)probed;
            if (storageSrc.RawBody is not IReadOnlyList<KeyValuePair<string, string>> rawKeys)
                return onFailure(new BindFailure(
                    new ParseError($"{nameof(StorageEntityBinder)} requires raw key map on {nameof(StorageBoundSource.RawBody)}."),
                    targetType, keyPath));

            var keyMembers = KeyMemberDiscovery.DiscoverKeyMembers(entityType);
            if (keyMembers.Length != rawKeys.Count)
                return onFailure(new BindFailure(
                    new ParseError(
                        $"loader produced {rawKeys.Count} key value(s); {entityType.FullName} declares {keyMembers.Length}."),
                    targetType, keyPath));

            // Parse each raw key value into its declared member type via the
            // application binder.
            var boundValues = new object[keyMembers.Length];
            for (var i = 0; i < keyMembers.Length; i++)
            {
                var memberType = keyMembers[i].Member.GetPropertyOrFieldType();
                var raw = rawKeys[i].Value;
                string parseError = null;
                object parsed = null;
                apiContext.Application.Bind(raw, memberType,
                    value => { parsed = value; return 0; },
                    why => { parseError = why; return 0; });
                if (parseError is not null)
                    return onFailure(new BindFailure(
                        new ParseError($"could not parse '{rawKeys[i].Key}': {parseError}"),
                        targetType, keyPath));
                boundValues[i] = parsed;
            }

            var partial = StorageEntityLoaderHelpers.BuildPartialStorageEntity(entityType, boundValues);

            // Reflectively dispatch a generic helper bound to T = entityType.
            var helper = typeof(StorageEntityBinder)
                .GetMethod(nameof(LoadGenericAsync), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(entityType, typeof(TResult));
            var task = (ValueTask<TResult>)helper.Invoke(null, new object[]
            {
                storageSrc.Driver, partial, rawKeys, targetType, keyPath, onBound, onFailure,
            });
            return await task.ConfigureAwait(false);
        }

        private static async ValueTask<TResult> LoadGenericAsync<T, TResult>(
            AzureTableDriverDynamic driver,
            object partial,
            IReadOnlyList<KeyValuePair<string, string>> rawKeys,
            Type targetType,
            string keyPath,
            Func<object, TResult> onBound,
            Func<BindFailure, TResult> onFailure)
            where T : IReferenceable
        {
            var typed = (StorageEntity<T>)partial;
            var key = (AzureStorageTableStorageKey<T>)typed.Key;

            string Describe()
            {
                if (rawKeys.Count == 0) return string.Empty;
                var parts = new string[rawKeys.Count];
                for (var i = 0; i < rawKeys.Count; i++)
                    parts[i] = $"{rawKeys[i].Key}={rawKeys[i].Value}";
                return string.Join(", ", parts);
            }

            BindFailure? failure = null;
            object loaded = null;
            await driver.LoadStorageEntityAsync<T, int>(
                key,
                onFound: stored => { loaded = stored; return 0; },
                onNotFound: () =>
                {
                    failure = new BindFailure(
                        new StorageEntityNotFoundReason(typeof(T), Describe()), targetType, keyPath);
                    return 0;
                },
                onFailure: (code, msg) =>
                {
                    failure = new BindFailure(
                        new ParseError($"storage failure loading {typeof(T).Name} ({Describe()}): [{code}] {msg}"),
                        targetType, keyPath);
                    return 0;
                });

            if (failure is not null)
                return onFailure(failure.Value);
            return onBound(loaded);
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

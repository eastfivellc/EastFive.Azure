using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Bindings;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Reflection;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Shared two-step pipeline for loader attributes that produce a
    /// <see cref="StorageEntity{T}"/> parameter.
    ///
    /// Step 1 (binding, sync, runs during route selection):
    ///   <see cref="IProvideBindingRequirements.GetParameterBinding"/> returns
    ///   one requirement per key member plus an <see cref="AssembleParameter"/>
    ///   closure. After the envelope binds every requirement, the closure
    ///   produces (a) a partial <see cref="StorageEntity{T}"/> (key only,
    ///   entity = default) for the parameter slot and (b) the wire-level
    ///   (name, value) bindings for the per-parameter binding-context
    ///   dictionary — used solely for diagnostic 404 messages.
    ///
    /// Step 2 (validation, async, runs after method match):
    ///   <see cref="IValidateHttpRequestForBoundParameters.ValidateBoundParametersForRequest"/>
    ///   resolves the driver, loads the record, and returns a
    ///   <see cref="ParameterMutation"/> that hands the chain a parameter list
    ///   with the partial slot replaced by a fully-loaded
    ///   <see cref="StorageEntity{T}"/>. On miss the mutation short-circuits
    ///   with 404 (using the captured bindings to identify the missing row).
    /// </summary>
    internal static class StorageEntityLoaderHelpers
    {
        /// <summary>
        /// Step 1a: parse a single key member's raw string value into its
        /// declared member type via the application's binder.
        /// </summary>
        public static TResult BindKeyMemberFromString<TResult>(
            string raw,
            KeyMemberDiscovery.KeyMember keyMember,
            Type memberType,
            IApplication httpApp,
            IHttpRequest request,
            Func<object, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            return httpApp.Bind(raw, memberType,
                value => onSuccess(value),
                why => onFailure(why));
        }

        /// <summary>
        /// Step 1b: assemble the bound key-member values into a partial
        /// <see cref="StorageEntity{T}"/>. Creates a stub of the entity (via
        /// <see cref="RuntimeHelpers.GetUninitializedObject(Type)"/> so we
        /// don't depend on a parameterless ctor), assigns each key member,
        /// computes the table-shaped key via the driver's static helper, and
        /// wraps it in a key-only <c>StorageEntity&lt;T&gt;</c>.
        /// </summary>
        public static object BuildPartialStorageEntity(
            Type entityType, object[] boundValues)
        {
            var keyMembers = KeyMemberDiscovery.DiscoverKeyMembers(entityType);
            if (boundValues.Length != keyMembers.Length)
                throw new InvalidOperationException(
                    $"binder produced {boundValues.Length} key value(s); " +
                    $"{entityType.FullName} declares {keyMembers.Length}");

            var stub = RuntimeHelpers.GetUninitializedObject(entityType);
            for (var i = 0; i < keyMembers.Length; i++)
                AssignMember(keyMembers[i].Member, stub, boundValues[i]);

            var keyInstance = InvokeComputeStorageKey(entityType, stub);

            // Use the partial constructor (key only) — entity is loaded later.
            var storageEntityType = typeof(StorageEntity<>).MakeGenericType(entityType);
            return Activator.CreateInstance(
                storageEntityType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new[] { keyInstance },
                culture: null);
        }

        /// <summary>
        /// Step 1c: build the wire-level (name, value) pairs that will ride in
        /// the binding-context dictionary so the validator can produce a
        /// client-friendly 404 echoing what was actually sent.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, object>> BuildBindings(
            IReadOnlyList<string> wireNames, object[] boundValues)
        {
            if (wireNames == null || wireNames.Count == 0)
                return Array.Empty<KeyValuePair<string, object>>();
            var pairs = new KeyValuePair<string, object>[wireNames.Count];
            for (var i = 0; i < wireNames.Count; i++)
                pairs[i] = new KeyValuePair<string, object>(wireNames[i], boundValues[i]);
            return pairs;
        }

        private static object InvokeComputeStorageKey(Type entityType, object populated)
        {
            // AzureTableDriverDynamic.ComputeStorageKey<T>(T populated) — static, partial class.
            var method = typeof(AzureTableDriverDynamic)
                .GetMethod(nameof(AzureTableDriverDynamic.ComputeStorageKey),
                    BindingFlags.Public | BindingFlags.Static)
                .MakeGenericMethod(entityType);
            return method.Invoke(null, new[] { populated });
        }

        private static void AssignMember(MemberInfo member, object target, object value)
        {
            switch (member)
            {
                case PropertyInfo p:
                    p.SetValue(target, value);
                    break;
                case FieldInfo f:
                    f.SetValue(target, value);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"unsupported member kind {member.MemberType} on {member.DeclaringType?.FullName}.{member.Name}");
            }
        }

        /// <summary>
        /// Step 2: launch the async load and return a <see cref="ParameterMutation"/>
        /// that hands the chain a parameter list with this owner's slot
        /// replaced by the full <see cref="StorageEntity{T}"/>. On miss /
        /// failure the closure short-circuits with the appropriate response.
        /// </summary>
        public static Task<ParameterMutation> LoadEntityAsync(
            ParameterInfo ownerParameter,
            IReadOnlyList<KeyValuePair<string, object>> bindings,
            IReadOnlyList<KeyValuePair<ParameterInfo, object>> parameterSelection,
            IHttpRequest routeData,
            CancellationToken cancellationToken)
        {
            var entityType = ExtractEntityType(ownerParameter);
            if (entityType == null)
                return Task.FromResult<ParameterMutation>((req, parms, @continue) =>
                    routeData
                        .CreateResponse(HttpStatusCode.InternalServerError)
                        .AddReason($"parameter '{ownerParameter.Name}' is not StorageEntity<T>")
                        .AsTask());

            // Slot must already be a partial StorageEntity<T> from Combine.
            var slotValue = parameterSelection
                .Where(kvp => kvp.Key == ownerParameter)
                .Select(kvp => kvp.Value)
                .FirstOrDefault();
            if (slotValue == null)
                return Task.FromResult<ParameterMutation>((req, parms, @continue) =>
                    routeData
                        .CreateResponse(HttpStatusCode.BadRequest)
                        .AddReason($"loader produced no slot value for '{ownerParameter.Name}'")
                        .AsTask());

            var driverProvider = StorageDriverScope.Resolve(ownerParameter);
            var driver = driverProvider.GetDriver();

            var shim = typeof(LoaderReflectionShim)
                .GetMethod(nameof(LoaderReflectionShim.LoadEntityAsyncCore),
                    BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(entityType);

            return (Task<ParameterMutation>)shim.Invoke(null,
                new object[] { driver, slotValue, ownerParameter, bindings ?? Array.Empty<KeyValuePair<string, object>>(), routeData, cancellationToken });
        }

        public static Type ExtractEntityType(ParameterInfo parameter)
        {
            var t = parameter.ParameterType;
            if (!t.IsGenericType)
                return null;
            if (t.GetGenericTypeDefinition() != typeof(StorageEntity<>))
                return null;
            return t.GenericTypeArguments[0];
        }
    }

    /// <summary>
    /// Reflection shim — must be a separate class so the open generic
    /// <c>LoadEntityAsyncCore&lt;TEntity&gt;</c> can be reified per entity.
    /// </summary>
    internal static class LoaderReflectionShim
    {
        internal static async Task<ParameterMutation> LoadEntityAsyncCore<TEntity>(
            AzureTableDriverDynamic driver,
            object partialSlotValue,
            ParameterInfo ownerParameter,
            IReadOnlyList<KeyValuePair<string, object>> bindings,
            IHttpRequest routeData,
            CancellationToken cancellationToken)
            where TEntity : IReferenceable
        {
            // cancellationToken: no driver overload accepts it today. A
            // sibling short-circuit lets this load complete; the orchestrator
            // observes the orphan in finally.
            _ = cancellationToken;

            var partial = (StorageEntity<TEntity>)partialSlotValue;
            var key = (AzureStorageTableStorageKey<TEntity>)partial.Key;

            // Identifier shown to clients on 404 / 500. Prefer wire-level
            // bindings (e.g. "id=<guid>"); fall back to the storage-shaped
            // key when no bindings were captured.
            string Describe() => bindings.Count > 0
                ? string.Join(", ", bindings.Select(kvp => $"{kvp.Key}={kvp.Value}"))
                : (string.Equals(key.RowKey, key.PartitionKey, StringComparison.Ordinal)
                    ? key.RowKey
                    : $"row='{key.RowKey}', partition='{key.PartitionKey}'");

            // Run the load now (parallel pre-work). The continuation captures
            // the eventual response factory — driver returns Task<TResult>
            // where TResult is itself the response factory, so we await once
            // and the factory yields the per-outcome mutation closure.
            return await driver.LoadStorageEntityAsync<TEntity, ParameterMutation>(
                key,
                onFound: full => (req, parms, @continue) =>
                {
                    // Replace this owner's slot with the loaded full entity
                    // and hand the next link the new list.
                    var updated = parms
                        .Select(kvp => kvp.Key == ownerParameter
                            ? new KeyValuePair<ParameterInfo, object>(ownerParameter, full)
                            : kvp)
                        .ToArray();
                    return @continue(req, updated);
                },
                onNotFound: () => (req, parms, @continue) =>
                    routeData
                        .CreateResponse(HttpStatusCode.NotFound)
                        .AddReason($"{typeof(TEntity).Name} not found ({Describe()})")
                        .AsTask(),
                onFailure: (code, msg) => (req, parms, @continue) =>
                    routeData
                        .CreateResponse(HttpStatusCode.InternalServerError)
                        .AddReason($"storage failure loading {typeof(TEntity).Name} ({Describe()}): [{code}] {msg}")
                        .AsTask());
        }
    }
}

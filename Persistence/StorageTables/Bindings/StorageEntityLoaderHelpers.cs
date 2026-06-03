using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Reflection;
using EastFive.Serialization.Binding;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Shared helpers for V3 storage-loader attributes: build a partial
    /// <see cref="StorageEntity{T}"/> (key-only) from already-bound key-member
    /// values, and run the async storage load. Owns the <see cref="IReferenceable"/>-
    /// constrained load orchestration so <c>StorageEntityBinder</c> stays
    /// generic-agnostic at the call site.
    /// </summary>
    internal static class StorageEntityLoaderHelpers
    {
        /// <summary>
        /// Assemble the bound key-member values into a partial
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
                AssignMember(keyMembers[i], stub, boundValues[i]);

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
        /// Async-load the key-only partial via
        /// <see cref="AzureTableDriverDynamic.LoadStorageEntityAsync"/> and
        /// dispatch to <paramref name="onBound"/> / <paramref name="onFailure"/>.
        /// Surfaces not-found via <see cref="StorageEntityNotFoundReason"/>
        /// (HTTP 404 at the dispatcher) and driver errors via
        /// <see cref="ParseError"/>.
        /// </summary>
        public static async ValueTask<TResult> LoadGenericAsync<T, TResult>(
            AzureTableDriverDynamic driver,
            object partial,
            string[] wireNames,
            object[] boundValues,
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
                if (wireNames is null || wireNames.Length == 0) return string.Empty;
                var parts = new string[wireNames.Length];
                for (var i = 0; i < wireNames.Length; i++)
                    parts[i] = $"{wireNames[i]}={boundValues[i]}";
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
    }
}

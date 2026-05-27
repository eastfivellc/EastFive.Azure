using System;
using System.Reflection;
using System.Runtime.CompilerServices;

using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Reflection;

namespace EastFive.Azure.Persistence.StorageTables.Bindings
{
    /// <summary>
    /// Shared helpers for V3 storage-loader attributes: build a partial
    /// <see cref="StorageEntity{T}"/> (key-only) from already-bound key-member
    /// values. The async load is performed by <c>StorageEntityBinder</c> via
    /// <c>driver.LoadStorageEntityAsync</c> directly — this class no longer
    /// owns any orchestration.
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

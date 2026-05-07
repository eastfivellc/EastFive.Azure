using System;
using System.Linq;
using System.Reflection;

using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Linq;
using EastFive.Reflection;

namespace EastFive.Persistence.Azure.StorageTables.Driver
{
    public partial class AzureTableDriverDynamic
    {
        /// <summary>
        /// Computes the storage-shaped key (<see cref="AzureStorageTableStorageKey{T}"/>)
        /// for a populated entity by walking its
        /// <see cref="IModifyAzureStorageTableRowKey"/> and
        /// <see cref="IModifyAzureStorageTablePartitionKey"/> members and
        /// asking each attribute to generate its part of the key from the
        /// member's current value.
        /// </summary>
        /// <remarks>
        /// Static today; will move behind a driver interface once one exists.
        /// Use this rather than constructing
        /// <see cref="AzureStorageTableStorageKey{T}"/> directly — it
        /// encapsulates the reflection over key-shaping attributes.
        /// </remarks>
        public static AzureStorageTableStorageKey<T> ComputeStorageKey<T>(T populatedEntity)
        {
            var rowKeyMember = typeof(T)
                .GetPropertyAndFieldsWithAttributesInterface<IModifyAzureStorageTableRowKey>()
                .Single(
                    onNone: () => throw new Exception(
                        $"{typeof(T).FullName} has no member with attribute implementing " +
                        $"{typeof(IModifyAzureStorageTableRowKey).FullName}."),
                    onSingle: tpl => tpl,
                    onMultiple: all => throw new Exception(
                        $"{typeof(T).FullName} has multiple members with attribute implementing " +
                        $"{typeof(IModifyAzureStorageTableRowKey).FullName}: " +
                        $"{all.Select(t => t.Item1.Name).Join(',')}."));
            var (rowMember, computeRow) = rowKeyMember;
            var rowKey = computeRow.GenerateRowKey(populatedEntity, rowMember);

            var partitionKeyMember = typeof(T)
                .GetPropertyAndFieldsWithAttributesInterface<IModifyAzureStorageTablePartitionKey>()
                .Single(
                    onNone: () => throw new Exception(
                        $"{typeof(T).FullName} has no member with attribute implementing " +
                        $"{typeof(IModifyAzureStorageTablePartitionKey).FullName}."),
                    onSingle: tpl => tpl,
                    onMultiple: all => throw new Exception(
                        $"{typeof(T).FullName} has multiple members with attribute implementing " +
                        $"{typeof(IModifyAzureStorageTablePartitionKey).FullName}: " +
                        $"{all.Select(t => t.Item1.Name).Join(',')}."));
            var (partMember, computePart) = partitionKeyMember;
            var partitionKey = computePart.GeneratePartitionKey(rowKey, populatedEntity, partMember);

            return new AzureStorageTableStorageKey<T>(rowKey, partitionKey);
        }
    }
}

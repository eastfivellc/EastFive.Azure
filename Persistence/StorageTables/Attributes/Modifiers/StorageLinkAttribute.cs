﻿using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class StorageLinkAttribute : Attribute,
        IModifyAzureStorageTableSave, IProvideFindBy
    {
        public Type ReferenceType { get; set; }

        public string ReferenceProperty { get; set; }

        public string LookupTableName { get; set; }

        public Type PartitionAttribute { get; set; }

        private string GetLookupTableName(MemberInfo memberInfo)
        {
            if (LookupTableName.HasBlackSpace())
                return this.LookupTableName;
            return $"{memberInfo.DeclaringType.Name}{memberInfo.Name}";
        }

        public IEnumerableAsync<KeyValuePair<string, string>> GetKeys<TEntity>(IRef<TEntity> value,
                Driver.AzureTableDriverDynamic repository, MemberInfo memberInfo)
            where TEntity : IReferenceable
        {
            var tableName = GetLookupTableName(memberInfo);
            var rowKey = value.StorageComputeRowKey();
            var partitionKey = value.StorageComputePartitionKey();
            return repository
                .FindByIdAsync<StorageLookupTable, IEnumerableAsync <KeyValuePair<string, string>>>(rowKey, partitionKey,
                    (dictEntity) =>
                    {
                        var rowAndParitionKeys = dictEntity.rowAndPartitionKeys.NullToEmpty()
                            .Select(rowParitionKeyKvp => rowParitionKeyKvp.AsTask())
                            .AsyncEnumerable();
                        return rowAndParitionKeys;
                    },
                    () => EnumerableAsync.Empty<KeyValuePair<string, string>>(),
                    tableName: tableName)
                .FoldTask();
        }

        [StorageTable]
        public struct StorageLookupTable
        {
            [RowKey]
            public string rowKey;

            [ParititionKey]
            public string partitionKey;

            [Storage]
            public KeyValuePair<string, string>[] rowAndPartitionKeys;
        }

        private string GetPartitionKey(IRef<IReferenceable> refKey, string rowKey, MemberInfo memberInfo)
        {
            var partitionKeyAttributeType = this.PartitionAttribute.IsDefaultOrNull() ?
                  typeof(StandardParititionKeyAttribute)
                  :
                  this.PartitionAttribute;
            if (!partitionKeyAttributeType.IsSubClassOfGeneric(typeof(IComputeAzureStorageTablePartitionKey)))
                throw new Exception($"{memberInfo.DeclaringType.FullName}..{memberInfo.Name} defines partition type as {partitionKeyAttributeType.FullName} which does not implement {typeof(IModifyAzureStorageTablePartitionKey).FullName}.");
            var partitionKeyAttribute = Activator.CreateInstance(partitionKeyAttributeType) as IComputeAzureStorageTablePartitionKey;
            var partitionKey = partitionKeyAttribute.ComputePartitionKey(refKey, rowKey, memberInfo);
            return partitionKey;
        }

        public virtual async Task<TResult> ExecuteAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var propertyValueType = memberInfo.GetMemberType();
            var rowKeyValue = memberInfo.GetValue(value);
            var referencedEntityType = ReferenceType.IsDefaultOrNull() ?
                propertyValueType.GetGenericArguments().First()
                :
                ReferenceType;

            if (!propertyValueType.IsSubClassOfGeneric(typeof(IRef<>)))
                throw new Exception($"`{propertyValueType.FullName}` is instance of IRef<>");

            Task<TResult> result = (Task<TResult>)this.GetType()
                .GetMethod("ExecuteTypedAsync", BindingFlags.Public | BindingFlags.Instance)
                .MakeGenericMethod(typeof(TEntity), referencedEntityType, typeof(TResult))
                .Invoke(this, new object[] { rowKeyValue,
                    memberInfo, rowKeyRef, partitionKeyRef, value, dictionary, repository, onSuccessWithRollback, onFailure });

            return await result;
        }

        public virtual Task<TResult> ExecuteTypedAsync<TEntity, TRefEntity, TResult>(IRef<TRefEntity> entityRef,
                MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
            where TRefEntity : IReferenceable
        {
            var rowKey = entityRef.StorageComputeRowKey();
            var partitionKey = entityRef.StorageComputePartitionKey();
            return repository.UpdateAsync<TRefEntity, TResult>(rowKey, partitionKey,
                async (entity, saveAsync) =>
                {
                    var referencedEntityType = typeof(TRefEntity);
                    var fieldToModifyFieldInfo = referencedEntityType
                                .GetFields()
                                .Select(
                                    field =>
                                    {
                                        return field
                                            .GetAttributesInterface<IPersistInAzureStorageTables>()
                                            .Where(attr => attr.Name == this.ReferenceProperty)
                                            .First(
                                                (attr, next) => field,
                                                () => default(FieldInfo));
                                    })
                                .Where(v => !v.IsDefaultOrNull())
                                .First();
                    var valueToMutate = fieldToModifyFieldInfo.GetValue(entity);
                    var valueToMutateType = valueToMutate.GetType();
                    if (valueToMutateType.IsSubClassOfGeneric(typeof(IRefs<>)))
                    {
                        var references = valueToMutate as IReferences;
                        var idsOriginal = references.ids;
                        var rowKeyId = Guid.Parse(rowKeyRef);
                        if (idsOriginal.Contains(rowKeyId))
                            return onSuccessWithRollback(() => 1.AsTask());

                        var ids = idsOriginal
                            .Append(rowKeyId)
                            .Distinct()
                            .ToArray();
                        var refsInstantiatable = typeof(Refs<>)
                            .MakeGenericType(valueToMutateType.GenericTypeArguments.First().AsArray());
                        var valueMutated = Activator.CreateInstance(refsInstantiatable, ids.AsArray());

                        fieldToModifyFieldInfo.SetValue(ref entity, valueMutated);

                        await saveAsync(entity);
                        Func<Task> rollback =
                            async () =>
                            {
                                bool rolled = await repository.UpdateAsync<TRefEntity, bool>(rowKey, partitionKey,
                                    async (entityRollback, saveRollbackAsync) =>
                                    {
                                        fieldToModifyFieldInfo.SetValue(ref entityRollback, valueToMutate);
                                        await saveRollbackAsync(entityRollback);
                                        return true;
                                    },
                                    () => false);
                            };
                        return onSuccessWithRollback(rollback);
                    }

                    return onFailure();
                },
                onFailure);
        }

        private class FaildModificationHandler<TResult> : IHandleFailedModifications<TResult>
        {
            internal MemberInfo member;
            internal Func<TResult> handler;

            public bool DoesMatchMember(MemberInfo[] membersWithFailures)
            {
                var doesMatchMember =  membersWithFailures
                    .Where(memberWithFailure => memberWithFailure.ContainsCustomAttribute<StorageLinkAttribute>(true))
                    .Where(memberWithFailure => memberWithFailure.Name == member.Name)
                    .Any();
                return doesMatchMember;
            }

            public TResult ModificationFailure(MemberInfo[] membersWithFailures)
            {
                var failureMember = membersWithFailures
                    .Where(membersWithFailure => membersWithFailure.ContainsCustomAttribute<StorageLinkAttribute>(true))
                    .Where(memberWithFailure => memberWithFailure.Name == member.Name)
                    .First();
                return handler();
            }
        }

        public static IHandleFailedModifications<TResult> ModificationFailure<T, TResult>(
            Expression<Func<T, object>> property, 
            Func<TResult> handlerOnFailure)
        {
            var member = property.MemberInfo(
                memberInfo => memberInfo,
                () => throw new Exception($"`{property}`: is not a member expression"));

            return new FaildModificationHandler<TResult>()
            {
                member = member,
                handler = handlerOnFailure,
            };
        }

        public virtual Task<TResult> ExecuteCreateAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            return ExecuteAsync(memberInfo,
                rowKeyRef, partitionKeyRef,
                value, dictionary,
                repository,
                onSuccessWithRollback,
                onFailure);
        }

        public Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo, 
                string rowKeyRef, string partitionKeyRef, 
                TEntity valueExisting, IDictionary<string, EntityProperty> dictionaryExisting,
                TEntity valueUpdated, IDictionary<string, EntityProperty> dictionaryUpdated, 
                AzureTableDriverDynamic repository, 
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            // Since only updating the row/partition keys could force a change here, just ignroe
            return onSuccessWithRollback(
                () => true.AsTask()).AsTask();
        }

        public Task<TResult> ExecuteDeleteAsync<TEntity, TResult>(MemberInfo memberInfo, 
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary, 
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            return ExecuteAsync(memberInfo,
                rowKeyRef, partitionKeyRef,
                value, dictionary,
                repository,
                onSuccessWithRollback,
                onFailure);
        }
    }
}

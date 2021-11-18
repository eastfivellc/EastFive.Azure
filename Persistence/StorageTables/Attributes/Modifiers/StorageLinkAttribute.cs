using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Analytics;
using EastFive.Azure.Persistence;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EastFive.Reflection;
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

        public IEnumerableAsync<IRefAst> GetKeys(
            MemberInfo memberInfo, Driver.AzureTableDriverDynamic repository,
            KeyValuePair<MemberInfo, object>[] queries,
            ILogger logger = default)
        {
            if (queries.IsDefaultNullOrEmpty())
                throw new ArgumentException("Exactly one query param is required for StorageLinkAttribute.");
            if (queries.Length != 1)
                throw new ArgumentException("Exactly one query param is valid for StorageLinkAttribute.");

            var tableName = GetLookupTableName(memberInfo);
            var memberValue = queries.First().Value;
            var queryMemberInfo = queries.First().Key;
            var rowKey = queryMemberInfo.StorageComputeRowKey(memberValue,
                onMissing: () => new RowKeyAttribute());
            var partitionKey = queryMemberInfo.StorageComputePartitionKey(memberValue, rowKey,
                onMissing: () => new RowKeyPrefixAttribute());
            return repository
                .FindByIdAsync<StorageLookupTable, IEnumerableAsync<IRefAst>>(rowKey, partitionKey,
                    (dictEntity, tableResult) =>
                    {
                        var rowAndParitionKeys = dictEntity.rowAndPartitionKeys
                            .NullToEmpty()
                            .Select(rowParitionKeyKvp => rowParitionKeyKvp.Key.AsAstRef(rowParitionKeyKvp.Value))
                            .AsAsync();
                        return rowAndParitionKeys;
                    },
                    () => EnumerableAsync.Empty<IRefAst>(),
                    tableName: tableName)
                .FoldTask();
        }

        public Task<TResult> GetLookupInfoAsync<TResult>(
                MemberInfo memberInfo, Driver.AzureTableDriverDynamic repository,
                KeyValuePair<MemberInfo, object>[] queries,
            Func<string, DateTime, int, TResult> onEtagLastModifedFound,
            Func<TResult> onNoLookupInfo)
        {
            if (queries.IsDefaultNullOrEmpty())
                throw new ArgumentException("Exactly one query param is required for StorageLinkAttribute.");
            if (queries.Length != 1)
                throw new ArgumentException("Exactly one query param is valid for StorageLinkAttribute.");

            var tableName = GetLookupTableName(memberInfo);
            var memberValue = queries.First().Value;
            var queryMemberInfo = queries.First().Key;
            var rowKey = queryMemberInfo.StorageComputeRowKey(memberValue,
                onMissing: () => new RowKeyAttribute());
            var partitionKey = queryMemberInfo.StorageComputePartitionKey(memberValue, rowKey,
                onMissing: () => new RowKeyPrefixAttribute());
            return repository.FindByIdAsync<StorageLookupTable, TResult>(
                    rowKey, partitionKey,
                (dictEntity, tableResult) =>
                {
                    return onEtagLastModifedFound(tableResult.Etag,
                        dictEntity.lastModified,
                        dictEntity.rowAndPartitionKeys
                            .NullToEmpty().Count());
                },
                () => onNoLookupInfo(),
                tableName: tableName);
        }

        public Task<PropertyLookupInformation[]> GetInfoAsync(
            MemberInfo memberInfo)
        {
            throw new NotImplementedException();
        }

        [StorageTable]
        public struct StorageLookupTable
        {
            [RowKey]
            public string rowKey;

            [ParititionKey]
            public string partitionKey;

            [LastModified]
            public DateTime lastModified;

            [Storage]
            public KeyValuePair<string, string>[] rowAndPartitionKeys;
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

        // Called via reflection
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
            var partitionKey = entityRef.StorageComputePartitionKey(rowKey);
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

                        var result = await saveAsync(entity);
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

        public IEnumerable<IBatchModify> GetBatchCreateModifier<TEntity>(MemberInfo member,
            string rowKey, string partitionKey, TEntity entity,
            IDictionary<string, EntityProperty> serializedEntity)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> ExecuteInsertOrReplaceAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            return onFailure().AsTask();
        }

        public Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo,
                IAzureStorageTableEntity<TEntity> updatedEntity,
                IAzureStorageTableEntity<TEntity> existingEntity,
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

        public IEnumerable<IBatchModify> GetBatchDeleteModifier<TEntity>(MemberInfo member,
            string rowKey, string partitionKey, TEntity entity,
            IDictionary<string, EntityProperty> serializedEntity)
        {
            throw new NotImplementedException();
        }
    }
}

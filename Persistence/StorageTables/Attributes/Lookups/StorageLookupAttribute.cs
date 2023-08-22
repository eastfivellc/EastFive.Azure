using EastFive.Analytics;
using EastFive.Azure.Persistence;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables.Backups;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EastFive.Reflection;
using System.Reflection;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public abstract class StorageLookupAttribute : Attribute,
        IRepairAzureStorageTableSave, IProvideFindBy, IBackupStorageMember, CascadeDeleteAttribute.IDeleteCascaded,
        IGenerateLookupKeys
    {
        public string LookupTableName { get; set; }

        public string Cascade { get; set; }

        internal static string GetMemberTableName(MemberInfo memberInfo)
        {
            return $"{memberInfo.DeclaringType.Name}{memberInfo.Name}";
        }

        protected virtual string GetLookupTableName(MemberInfo memberInfo)
        {
            if (LookupTableName.HasBlackSpace())
                return this.LookupTableName;
            return GetMemberTableName(memberInfo);
        }

        public TResult GetKeys<TResult>(
                MemberInfo memberInfo, Driver.AzureTableDriverDynamic repository,
                KeyValuePair<MemberInfo, object>[] queries,
            Func<IEnumerableAsync<IRefAst>, TResult> onQueriesMatched,
            Func<TResult> onQueriesDidNotMatch,
                ILogger logger = default)
        {
            var tableName = GetLookupTableName(memberInfo);
            var scopedLogger = logger.CreateScope("GetKeys");
            return this.GetLookupKeys(memberInfo, queries,
                lookupKeys =>
                {
                    var lookupRefs = lookupKeys.ToArray();
                    scopedLogger.Trace($"Found {lookupRefs.Length} lookupRefs [{lookupRefs.Select(lr => $"{lr.PartitionKey}/{lr.RowKey}").Join(",")}]");
                    var items = lookupRefs
                        .Select(
                            lookupRef =>
                            {
                                scopedLogger.Trace($"Fetching... {lookupRef.PartitionKey}/{lookupRef.RowKey}");
                                return repository.FindByIdAsync<StorageLookupTable, IRefAst[]>(
                                        lookupRef.RowKey, lookupRef.PartitionKey,
                                    (dictEntity, tableResult) =>
                                    {
                                        scopedLogger.Trace($"Fetched {lookupRef.PartitionKey}/{lookupRef.RowKey}");
                                        var rowAndPartitionKeys = dictEntity.rowAndPartitionKeys
                                            .NullToEmpty()
                                            .Where(rowPartitionKeyKvp => rowPartitionKeyKvp.Key.HasBlackSpace() && rowPartitionKeyKvp.Value.HasBlackSpace())
                                            .Distinct(rowPartitionKeyKvp => $"{rowPartitionKeyKvp.Key}|{rowPartitionKeyKvp.Value}")
                                            .Select(rowPartitionKeyKvp => rowPartitionKeyKvp.Key.AsAstRef(rowPartitionKeyKvp.Value))
                                            .ToArray();
                                        scopedLogger.Trace($"{lookupRef.PartitionKey}/{lookupRef.RowKey} = {rowAndPartitionKeys.Length} lookups");
                                        return rowAndPartitionKeys;
                                    },
                                    () =>
                                    {
                                        scopedLogger.Trace($"Fetch FAILED for {lookupRef.PartitionKey}/{lookupRef.RowKey}");
                                        return new IRefAst[] { };
                                    },
                                    tableName: tableName);
                            })
                        .AsyncEnumerable(startAllTasks: true)
                        .SelectMany(logger: scopedLogger);
                    return onQueriesMatched(items);
                },
                why => onQueriesDidNotMatch());
        }

        public Task<TResult> GetLookupInfoAsync<TResult>(
                MemberInfo memberInfo, AzureTableDriverDynamic repository,
                KeyValuePair<MemberInfo, object>[] queries,
            Func<string, DateTime, int, TResult> onEtagLastModifedFound,
            Func<TResult> onNoLookupInfo)
        {
            var tableName = GetLookupTableName(memberInfo);
            return this.GetLookupKeys(memberInfo, queries,
                lookupValues =>
                {
                    var lookupRefs = lookupValues.ToArray();
                    return lookupRefs
                        .Select(
                            lookupRef =>
                            {
                                return repository.FindByIdAsync<StorageLookupTable, (bool, string, DateTime, int)>(
                                        lookupRef.RowKey, lookupRef.PartitionKey,
                                    (dictEntity, tableResult) =>
                                    {
                                        var lastModified = dictEntity.lastModified;
                                        var count = dictEntity
                                            .rowAndPartitionKeys
                                            .NullToEmpty()
                                            .Count();
                                        return (true, tableResult.Etag,
                                            lastModified,
                                            count);
                                    },
                                    () =>
                                    {
                                        var etag = default(string);
                                        var count = default(int);
                                        var lastModified = default(DateTime);
                                        return (false, etag, lastModified, count);
                                    },
                                    tableName: tableName);
                            })
                        .AsyncEnumerable()
                        .Where(tpl => tpl.Item1)
                        .FirstAsync(
                            tpl => onEtagLastModifedFound(
                                tpl.Item2, tpl.Item3, tpl.Item4),
                            () => onNoLookupInfo());
                },
                (why) => throw new Exception(why));
        }

        public async Task<PropertyLookupInformation[]> GetInfoAsync(
            MemberInfo memberInfo)
        {
            var tableName = GetLookupTableName(memberInfo);
            var storageTableLookups = await typeof(StorageLookupTable)
                .StorageGetAll(tableName)
                .Select(slt => GetInfo((StorageLookupTable)slt))
                .ToArrayAsync();
            var propertyLookupInformations = storageTableLookups
                .GroupBy(slt => slt.rowKey + slt.partitionKey)
                .Select(
                    propLookInfosGrp =>
                    {
                        var propLookInfos = propLookInfosGrp.ToArray();
                        var total = propLookInfos.Sum(propLookInfo => propLookInfo.count);
                        var value = propLookInfos.First();
                        value.count = total;
                        return value;
                    })
                .ToArray();
            return propertyLookupInformations;
        }

        protected virtual PropertyLookupInformation GetInfo(
            StorageLookupTable slt)
        {
            return new PropertyLookupInformation
            {
                count = slt.rowAndPartitionKeys.Length,
                partitionKey = slt.partitionKey,
                rowKey = slt.rowKey,
                value = slt.rowKey,
                keys = slt.rowAndPartitionKeys
                    .Select(kvp => $"{kvp.Value}/{kvp.Key}")
                    .ToArray(),
            };
        }

        [StorageTable]
        public struct StorageLookupTable
        {
            [RowKey]
            public string rowKey;

            [ParititionKey]
            public string partitionKey;

            [ETag]
            public string eTag;

            [LastModified]
            public DateTime lastModified;

            [StorageOverflow]
            public KeyValuePair<string, string>[] rowAndPartitionKeys;
        }

        protected virtual TResult GetKeys<TEntity, TResult>(MemberInfo decoratedMember, TEntity value,
            Func<IEnumerable<IRefAst>, TResult> onLookupValuesMatch,
            Func<string, TResult> onNoMatch)
        {
            var lookupGeneratorAttr = (IGenerateLookupKeys)this;
            var membersOfInterest = lookupGeneratorAttr.ProvideLookupMembers(decoratedMember);
            var membersAndValuesRequiredForComputingLookup = membersOfInterest
                .Select(member => member.GetValue(value).PairWithKey(member))
                .ToArray();
            return lookupGeneratorAttr.GetLookupKeys(decoratedMember, 
                membersAndValuesRequiredForComputingLookup,
                onLookupValuesMatch,
                onNoMatch:onNoMatch);
        }

        #region Execution Code IMPORTANT: READ NOTE BEFORE MODIFYING!!!!!

        // NOTE: This table will contain duplication indexes. 
        // This is important so rollback can undo exactly what it did.
        // Removal of duplicate entries inside of Execution Chain can
        // result in data loss during concurrent operations.

        public virtual Task<TResult> ExecuteAsync<TEntity, TResult>(MemberInfo memberInfo,
                TEntity value,
                AzureTableDriverDynamic repository, 
                Func<IEnumerable<IRefAst>, IEnumerable<IRefAst>> mutateCollection,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            return GetKeys(memberInfo, value,
                async lookupKeys =>
                {
                    var rollbacks = await lookupKeys
                        .Select(
                            lookupKey =>
                            {
                                return MutateLookupTable(lookupKey.RowKey, lookupKey.PartitionKey,
                                    memberInfo, repository, mutateCollection);
                            })
                        .WhenAllAsync();
                    Func<Task> allRollbacks =
                        () =>
                        {
                            var tasks = rollbacks.Select(rb => rb());
                            return Task.WhenAll(tasks);
                        };
                    return onSuccessWithRollback(allRollbacks);
                },
                why => onFailure().AsTask());
        }

        public async Task<Func<Task>> MutateLookupTable(string rowKey, string partitionKey,
            MemberInfo memberInfo, AzureTableDriverDynamic repository,
            Func<IEnumerable<IRefAst>, IEnumerable<IRefAst>> mutateCollection)
        {
            var tableName = GetLookupTableName(memberInfo);
            return await repository.UpdateOrCreateAsync<StorageLookupTable, Func<Task>>(rowKey, partitionKey,
                async (created, lookup, saveAsync) =>
                {
                    // store for rollback
                    var orignalRowAndPartitionKeys = lookup.rowAndPartitionKeys
                        .NullToEmpty()
                        .Where(kvp => kvp.Key.HasBlackSpace() && kvp.Value.HasBlackSpace()) // omit any bad entries
                        .Select(kvp => kvp.Key.AsAstRef(kvp.Value))
                        .Distinct(rowParitionKeyKvp => $"{rowParitionKeyKvp.RowKey}|{rowParitionKeyKvp.PartitionKey}")
                        .ToArray();
                    var updatedRowAndPartitionKeys = mutateCollection(orignalRowAndPartitionKeys)
                        .Where(kvp => kvp.RowKey.HasBlackSpace() && kvp.PartitionKey.HasBlackSpace())
                        .Distinct(rowParitionKeyKvp => $"{rowParitionKeyKvp.RowKey}|{rowParitionKeyKvp.PartitionKey}")
                        .ToArray();

                    if (Unmodified(orignalRowAndPartitionKeys, updatedRowAndPartitionKeys))
                        return () => true.AsTask();

                    lookup.rowAndPartitionKeys = updatedRowAndPartitionKeys
                        .Select(astRef => astRef.RowKey.PairWithValue(astRef.PartitionKey))
                        .ToArray();
                    await saveAsync(lookup);

                    Func<Task<bool>> rollback =
                        async () =>
                        {
                            var removed = orignalRowAndPartitionKeys
                                .Except(updatedRowAndPartitionKeys,
                                    rk => $"{rk.RowKey}|{rk.PartitionKey}")
                                //.Select(orignalRowAndPartitionKey =>
                                //    orignalRowAndPartitionKey.RowKey.PairWithValue(orignalRowAndPartitionKey.PartitionKey))
                                .ToArray();
                            var added = updatedRowAndPartitionKeys
                                .Except(orignalRowAndPartitionKeys,   rk => $"{rk.RowKey}|{rk.PartitionKey}")
                                //.Select(updatedRowAndPartitionKey =>
                                //    updatedRowAndPartitionKey.RowKey.PairWithValue(updatedRowAndPartitionKey.PartitionKey))
                                .ToArray();
                            var table = repository.TableClient.GetTableReference(tableName);
                            return await repository.UpdateAsync<StorageLookupTable, bool>(rowKey, partitionKey,
                                async (currentDoc, saveRollbackAsync) =>
                                {
                                    var currentLookups = currentDoc.rowAndPartitionKeys
                                        .Select(rpk => rpk.Key.AsAstRef(rpk.Value))
                                        .Distinct(rowParitionKeyKvp => $"{rowParitionKeyKvp.RowKey}|{rowParitionKeyKvp.PartitionKey}")
                                        .NullToEmpty()
                                        .ToArray();
                                    var rolledBackRowAndPartitionKeys = currentLookups
                                        .Concat(removed)
                                        .Except(added, rk => $"{rk.RowKey}|{rk.PartitionKey}")
                                        .Distinct(rowParitionKeyKvp => $"{rowParitionKeyKvp.RowKey}|{rowParitionKeyKvp.PartitionKey}")
                                        .ToArray();
                                    if (Unmodified(rolledBackRowAndPartitionKeys, currentLookups))
                                        return true;
                                    currentDoc.rowAndPartitionKeys = rolledBackRowAndPartitionKeys
                                        .Select(rpk => rpk.RowKey.PairWithValue(rpk.PartitionKey))
                                        .ToArray();
                                    await saveRollbackAsync(currentDoc);
                                    return true;
                                },
                                table: table);
                        };
                    return rollback;
                },
                tableName: tableName);

            bool Unmodified(
                IRefAst[] rollbackRowAndPartitionKeys,
                IRefAst[] modifiedDocRowAndPartitionKeys)
            {
                var modifiedAddedOrUpdated = modifiedDocRowAndPartitionKeys
                    .Except(rollbackRowAndPartitionKeys, rk => $"{rk.RowKey}|{rk.PartitionKey}")
                    .Any();
                if (modifiedAddedOrUpdated)
                    return false;

                var noneDeleted = rollbackRowAndPartitionKeys.Length == 
                    modifiedDocRowAndPartitionKeys.Length;

                return noneDeleted;
            }
        }

        public virtual Task<TResult> ExecuteCreateAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            return ExecuteAsync(memberInfo,
                value,
                repository,
                (rowAndParitionKeys) => rowAndParitionKeys
                    .NullToEmpty()
                    .Append(rowKeyRef.AsAstRef(partitionKeyRef))
                    .ToArray(),
                onSuccessWithRollback,
                onFailure);
        }

        private class BatchModifier : IBatchModify
        {
            public delegate IEnumerable<KeyValuePair<string, string>> ModifierDelegate(
                IEnumerable<KeyValuePair<string, string>> currentLookups);

            public BatchModifier(string tableName,
                IRefAst lookupResourceRef,
                ModifierDelegate modifier)
            {
                this.tableName = tableName;
                this.lookupResourceRef = lookupResourceRef;
                this.modifier = modifier;
            }

            private string tableName;
            private IRefAst lookupResourceRef;

            private ModifierDelegate modifier;

            public string GroupingKey => $"{tableName}|{lookupResourceRef.PartitionKey}|{lookupResourceRef.RowKey}";

            public int? GroupLimit => default(int?);

            public Task<TResult> CreateOrUpdateAsync<TResult>(
                AzureTableDriverDynamic repository,
                Func<object, Func<object, Task>, Task<TResult>> callback)
            {
                return repository.UpdateOrCreateAsync<StorageLookupTable, TResult>(
                    lookupResourceRef.RowKey, lookupResourceRef.PartitionKey,
                    (created, lookupTable, saveAsync) => callback(lookupTable,
                        resource => saveAsync((StorageLookupTable)resource)),
                    tableName: tableName);
            }

            public object Modify(object resource)
            {
                var storageLookup = (StorageLookupTable)resource;
                storageLookup.rowAndPartitionKeys = this.modifier(
                        storageLookup
                            .rowAndPartitionKeys
                            .NullToEmpty())
                    .ToArray();
                return storageLookup;
            }
        }

        public virtual IEnumerable<IBatchModify> GetBatchCreateModifier<TEntity>(MemberInfo memberInfo,
            string rowKey, string partitionKey,
            TEntity entity, IDictionary<string, EntityProperty> serializedEntity)
        {
            var tableName = GetLookupTableName(memberInfo);
            return GetKeys(memberInfo, entity,
                lookupKeys =>
                {
                    return lookupKeys
                        .Select(
                            lookupKey =>
                            {
                                return (IBatchModify)new BatchModifier(tableName, lookupKey,
                                    rowAndPartitionKeys => rowAndPartitionKeys
                                        .Append(rowKey.PairWithValue(partitionKey)));
                            })
                        .ToArray();
                },
                why => throw new Exception(why));
        }

        public async Task<TResult> ExecuteInsertOrReplaceAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var tableName = GetLookupTableName(memberInfo);
            return await GetKeys(memberInfo, value,
                async existingRowKeys =>
                {
                    var missingRows = existingRowKeys
                        .Select(
                            async astKey =>
                            {
                                var isGood = await repository.FindByIdAsync<StorageLookupTable, bool>(astKey.RowKey, astKey.PartitionKey,
                                    (lookup, tableResult) =>
                                    {
                                        var rowAndParitionKeys = lookup.rowAndPartitionKeys;
                                        var rowKeyFound = rowAndParitionKeys
                                            .NullToEmpty()
                                            .Distinct(rowParitionKeyKvp => $"{rowParitionKeyKvp.Key}|{rowParitionKeyKvp.Value}")
                                            .Where(kvp => kvp.Key == rowKeyRef)
                                            .Where(kvp => kvp.Value == partitionKeyRef)
                                            .Any();
                                        if (rowKeyFound)
                                            return true;
                                        return false;
                                    },
                                    onNotFound: () => false,
                                    tableName: tableName);
                                return isGood;
                            })
                        .AsyncEnumerable()
                        .Where(item => !item);
                    if (await missingRows.AnyAsync())
                        return onFailure();
                    return onSuccessWithRollback(() => 1.AsTask());
                },
                why => throw new Exception(why));
        }

        public Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo,
                IAzureStorageTableEntity<TEntity> updatedEntity, 
                IAzureStorageTableEntity<TEntity> existingEntity,
                AzureTableDriverDynamic repository, 
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            return GetKeys(memberInfo, existingEntity.Entity,
                existingRowKeys =>
                {
                    return GetKeys(memberInfo, updatedEntity.Entity,
                        async updatedRowKeys =>
                        {
                            var rowKeysDeleted = existingRowKeys.Except(updatedRowKeys, rk => $"{rk.RowKey}|{rk.PartitionKey}");
                            var rowKeysAdded = updatedRowKeys.Except(existingRowKeys, rk => $"{rk.RowKey}|{rk.PartitionKey}");
                            var deletionRollbacks = rowKeysDeleted
                                .Select(
                                    rowKey =>
                                    {
                                        return MutateLookupTable(rowKey.RowKey, rowKey.PartitionKey, memberInfo,
                                            repository,
                                            (rowAndParitionKeys) =>
                                            {
                                                var existingEntityMarker = $"{existingEntity.RowKey}|{existingEntity.PartitionKey}";
                                                return rowAndParitionKeys
                                                    .NullToEmpty()
                                                    .Where(kvp => $"{kvp.RowKey}|{kvp.PartitionKey}" != existingEntityMarker)
                                                    .ToArray();
                                            });
                                    });
                            var additionRollbacks = rowKeysAdded
                                 .Select(
                                     rowKey =>
                                     {
                                         return MutateLookupTable(rowKey.RowKey, rowKey.PartitionKey, memberInfo,
                                             repository,
                                             (rowAndParitionKeys) => rowAndParitionKeys
                                                .NullToEmpty()
                                                .Append(updatedEntity.RowKey.AsAstRef(updatedEntity.PartitionKey))
                                                .ToArray());
                                     });
                            var allRollbacks = await additionRollbacks.Concat(deletionRollbacks).WhenAllAsync();
                            Func<Task> allRollback =
                                () =>
                                {
                                    var tasks = allRollbacks.Select(rb => rb());
                                    return Task.WhenAll(tasks);
                                };
                            return onSuccessWithRollback(allRollback);
                        },
                        why => throw new Exception(why));
                },
                why => throw new Exception(why));
            
        }

        public Task<TResult> ExecuteDeleteAsync<TEntity, TResult>(MemberInfo memberInfo, 
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary, 
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            return ExecuteAsync(memberInfo,
                value,
                repository,
                (rowAndParitionKeys) => rowAndParitionKeys
                    .NullToEmpty()
                    .Where(
                        kvp =>
                            kvp.RowKey != rowKeyRef ||
                            kvp.PartitionKey != partitionKeyRef),
                onSuccessWithRollback,
                onFailure);
        }

        public virtual IEnumerable<IBatchModify> GetBatchDeleteModifier<TEntity>(MemberInfo memberInfo,
            string rowKey, string partitionKey,
            TEntity entity, IDictionary<string, EntityProperty> serializedEntity)
        {
            var tableName = GetLookupTableName(memberInfo);
            return GetKeys(memberInfo, entity,
                lookupKeys =>
                {
                    return lookupKeys
                        .Select(
                            lookupKey =>
                            {
                                return (IBatchModify)new BatchModifier(tableName, lookupKey,
                                    rowAndPartitionKeys =>
                                    {
                                        return rowAndPartitionKeys
                                            .Where(
                                                kvp =>
                                                    kvp.Key != rowKey ||
                                                    kvp.Value != partitionKey);
                                    });
                            })
                        .ToArray();
                },
                why => throw new Exception(why));
        }

        public async Task<TResult> RepairAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> propertyAndValues,
                AzureTableDriverDynamic repository,
            Func<string, TResult> onRepaired,
            Func<TResult> onNoChangesNecessary)
        {
            var queryableProperties = typeof(TEntity)
                .StorageProperties()
                .Select(
                    (memberAttrKvp) =>
                    {
                        var member = memberAttrKvp.Key;
                        var attr = memberAttrKvp.Value;
                        var memberValue = member.GetValue(value);
                        return member.PairWithValue(memberValue);
                    })
                .ToArray();

            return await this.GetLookupKeys(memberInfo, queryableProperties,
                async lookupKeys =>
                {
                    Func<Task>[] rollbacks = await lookupKeys
                        .Select(
                            lookupKeys => MutateLookupTable(lookupKeys.RowKey, lookupKeys.PartitionKey, memberInfo,
                                repository,
                                (rowAndParitionKeys) => rowAndParitionKeys
                                    .NullToEmpty()
                                    .Append(rowKeyRef.AsAstRef(partitionKeyRef))))
                        .AsyncEnumerable()
                        .ToArrayAsync();

                    return onRepaired($"Processed Lookup:{rowKeyRef + " | " + partitionKeyRef}");

                },
                why => throw new Exception(why));
        }

        #endregion

        #region Modification Failures

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


        private class FaildModificationHandler<TResult> : IHandleFailedModifications<TResult>
        {
            internal MemberInfo member;
            internal Func<TResult> handler;

            public bool DoesMatchMember(MemberInfo[] membersWithFailures)
            {
                var doesMatchMember = membersWithFailures
                    .Where(memberWithFailure => memberWithFailure.ContainsCustomAttribute<StorageLookupAttribute>(true))
                    .Where(memberWithFailure => memberWithFailure.Name == member.Name)
                    .Any();
                return doesMatchMember;
            }

            public TResult ModificationFailure(MemberInfo[] membersWithFailures)
            {
                var failureMember = membersWithFailures
                    .Where(membersWithFailure => membersWithFailure.ContainsCustomAttribute<StorageLookupAttribute>(true))
                    .Where(memberWithFailure => memberWithFailure.Name == member.Name)
                    .First();
                return handler();
            }
        }

        #endregion

        public Task<Func<Task>> CascadeDeleteAsync<TEntity>(MemberInfo memberInfo,
            string rowKeyRef, string partitionKeyRef,
            TEntity memberValue, IDictionary<string, EntityProperty> dictionary,
            AzureTableDriverDynamic repository)
        {
            var tableName = GetLookupTableName(memberInfo);

            return repository
                .DeleteAsync<StorageLookupTable, Func<Task>>(rowKeyRef, partitionKeyRef,
                    async (lookupTable, deleteAsync) =>
                    {
                        var lookupEntity = await deleteAsync();
                        var rollbacks = await lookupTable
                            .rowAndPartitionKeys
                            .NullToEmpty()
                            .Distinct(rowParitionKeyKvp => $"{rowParitionKeyKvp.Key}|{rowParitionKeyKvp.Value}")
                            .Select(rowParitionKeyKvp =>
                                repository.DeleteAsync<Func<Task>>(rowParitionKeyKvp.Key, rowParitionKeyKvp.Value, memberInfo.DeclaringType,
                                    (entity, data) => 
                                        () => (Task)repository.CreateAsync(entity, memberInfo.DeclaringType,
                                            (x) => true, () => false),
                                    () => 
                                        () => 1.AsTask()))
                            .AsyncEnumerable()
                            .Append(
                                () => repository.CreateAsync(lookupEntity, tableName,
                                    x => true,
                                    () => false))
                            .ToArrayAsync();
                        Func<Task> rollbacksAll = () => Task.WhenAll(rollbacks.Select(rollback => rollback()));
                        return rollbacksAll;
                    },
                    () => () => 0.AsTask(),
                    tableName: tableName);
        }

        public IEnumerable<StorageResourceInfo> GetStorageResourceInfos(MemberInfo memberInfo)
        {
            var tableName = GetLookupTableName(memberInfo);
            yield return new StorageResourceInfo
            {
                tableName = tableName,
                message = new [] { new WhereInformation() },
                sortKey = tableName,
            };
        }

        public virtual IEnumerable<MemberInfo> ProvideLookupMembers(MemberInfo decoratedMember)
        {
            return decoratedMember.AsEnumerable();
        }

        public abstract TResult GetLookupKeys<TResult>(MemberInfo decoratedMember, 
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues,
            Func<IEnumerable<IRefAst>, TResult> onLookupValuesMatch,
            Func<string, TResult> onNoMatch);
    }
}

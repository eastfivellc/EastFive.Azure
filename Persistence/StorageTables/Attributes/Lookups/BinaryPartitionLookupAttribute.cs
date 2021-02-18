using BlackBarLabs;
using BlackBarLabs.Extensions;
using EastFive.Analytics;
using EastFive.Azure.Persistence;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables.Backups;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.Azure.Cosmos.Table;
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
    public class BinaryPartitionAttribute : Attribute,
        IModifyAzureStorageTableSave, IProvideFindBy, IBackupStorageMember
    {
        public string LookupTableName { get; set; }

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
            var tableName = GetLookupTableName(memberInfo);
            var lookupGeneratorAttr = (IGenerateLookupKeys)this;
            var lookupRefs = lookupGeneratorAttr
                .GetLookupKeys(memberInfo, queries)
                .ToArray();
            return lookupRefs
                .Select(
                    lookupRef =>
                    {
                        return repository.FindByIdAsync<StorageLookupTable, IEnumerable<IRefAst>>(
                                lookupRef.RowKey, lookupRef.PartitionKey,
                            (dictEntity, etag) =>
                            {
                                var rowAndParitionKeys = dictEntity.rowAndPartitionKeys
                                    .NullToEmpty()
                                    .Select(rowParitionKeyKvp => rowParitionKeyKvp.Key.AsAstRef(rowParitionKeyKvp.Value));
                                return rowAndParitionKeys;
                            },
                            () => Enumerable.Empty<IRefAst>(),
                            tableName: tableName);
                    })
                .AsyncEnumerable(true)
                .SelectMany();
        }

        public Task<TResult> GetLookupInfoAsync<TResult>(
                MemberInfo memberInfo, Driver.AzureTableDriverDynamic repository,
                KeyValuePair<MemberInfo, object>[] queries,
            Func<string, DateTime, int, TResult> onEtagLastModifedFound,
            Func<TResult> onNoLookupInfo)
        {
            var tableName = GetLookupTableName(memberInfo);
            var lookupGeneratorAttr = (IGenerateLookupKeys)this;
            var lookupRef = lookupGeneratorAttr
                .GetLookupKeys(memberInfo, queries)
                .First();
            return repository.FindByIdAsync<StorageLookupTable, TResult>(
                    lookupRef.RowKey, lookupRef.PartitionKey,
                (dictEntity, etag) =>
                {
                    return onEtagLastModifedFound(etag, 
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

            [ETag]
            public string eTag;

            [LastModified]
            public DateTime lastModified;

            [Storage]
            public KeyValuePair<string, string>[] rowAndPartitionKeys;
        }

        private IEnumerable<IRefAst> GetKeys<TEntity>(MemberInfo decoratedMember, TEntity value)
        {
            var lookupGeneratorAttr = (IGenerateLookupKeys)this;
            var membersOfInterest = lookupGeneratorAttr.ProvideLookupMembers(decoratedMember);
            var membersAndValuesRequiredForComputingLookup = membersOfInterest
                .Select(member => member.GetValue(value).PairWithKey(member))
                .ToArray();
            return lookupGeneratorAttr.GetLookupKeys(decoratedMember,
                membersAndValuesRequiredForComputingLookup);
        }

        #region Execution Code IMPORTANT: READ NOTE BEFORE MODIFYING!!!!!

        // NOTE: This table will contain duplication indexes. 
        // This is important so rollback can undo exactly what it did.
        // Removal of duplicate entries inside of Execution Chain can
        // result in data loss during concurrent operations.

        public virtual async Task<TResult> ExecuteAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
                Func<IEnumerable<KeyValuePair<string, string>>, IEnumerable<KeyValuePair<string, string>>> mutateCollection,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var rollbacks = await GetKeys(memberInfo, value)
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
        }

        public async Task<Func<Task>> MutateLookupTable(string rowKey, string partitionKey,
            MemberInfo memberInfo, AzureTableDriverDynamic repository,
            Func<IEnumerable<KeyValuePair<string, string>>, IEnumerable<KeyValuePair<string, string>>> mutateCollection)
        {
            var tableName = GetLookupTableName(memberInfo);
            return await repository.UpdateOrCreateAsync<StorageLookupTable, Func<Task>>(rowKey, partitionKey,
                async (created, lookup, saveAsync) =>
                {
                    // store for rollback
                    var orignalRowAndPartitionKeys = lookup.rowAndPartitionKeys
                        .NullToEmpty()
                        .ToArray();
                    var updatedRowAndPartitionKeys = mutateCollection(orignalRowAndPartitionKeys)
                        .ToArray();

                    if (Unmodified(orignalRowAndPartitionKeys, updatedRowAndPartitionKeys))
                        return () => true.AsTask();

                    lookup.rowAndPartitionKeys = updatedRowAndPartitionKeys;
                    await saveAsync(lookup);

                    Func<Task<bool>> rollback =
                        async () =>
                        {
                            var removed = orignalRowAndPartitionKeys.Except(updatedRowAndPartitionKeys, rk => $"{rk.Key}|{rk.Value}");
                            var added = updatedRowAndPartitionKeys.Except(orignalRowAndPartitionKeys, rk => $"{rk.Key}|{rk.Value}");
                            var table = repository.TableClient.GetTableReference(tableName);
                            return await repository.UpdateAsync<StorageLookupTable, bool>(rowKey, partitionKey,
                                async (currentDoc, saveRollbackAsync) =>
                                {
                                    var currentLookups = currentDoc.rowAndPartitionKeys
                                        .NullToEmpty()
                                        .ToArray();
                                    var rolledBackRowAndPartitionKeys = currentLookups
                                        .Concat(removed)
                                        .Except(added, rk => $"{rk.Key}|{rk.Value}")
                                        .ToArray();
                                    if (Unmodified(rolledBackRowAndPartitionKeys, currentLookups))
                                        return true;
                                    currentDoc.rowAndPartitionKeys = rolledBackRowAndPartitionKeys;
                                    await saveRollbackAsync(currentDoc);
                                    return true;
                                },
                                table: table);
                        };
                    return rollback;
                },
                tableName: tableName);

            bool Unmodified(
                KeyValuePair<string, string>[] rollbackRowAndPartitionKeys,
                KeyValuePair<string, string>[] modifiedDocRowAndPartitionKeys)
            {
                var modified = modifiedDocRowAndPartitionKeys
                    .Except(rollbackRowAndPartitionKeys, rk => $"{rk.Key}|{rk.Value}")
                    .Any();
                var unmodified = !modified;
                return unmodified;
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
                rowKeyRef, partitionKeyRef,
                value, dictionary,
                repository,
                (rowAndParitionKeys) => rowAndParitionKeys.NullToEmpty().Append(rowKeyRef.PairWithValue(partitionKeyRef)),
                onSuccessWithRollback,
                onFailure);
        }

        public async Task<TResult> ExecuteInsertOrReplaceAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var existingRowKeys = GetKeys(memberInfo, value);
            var tableName = GetLookupTableName(memberInfo);
            var missingRows = existingRowKeys
                .Select(
                    async astKey =>
                    {
                        var isGood = await repository.FindByIdAsync<StorageLookupTable, bool>(astKey.RowKey, astKey.PartitionKey,
                            (lookup, etag) =>
                            {
                                var rowAndParitionKeys = lookup.rowAndPartitionKeys;
                                var rowKeyFound = rowAndParitionKeys
                                    .NullToEmpty()
                                    .Where(kvp => kvp.Key == rowKeyRef)
                                    .Any();
                                var partitionKeyFound = rowAndParitionKeys
                                    .NullToEmpty()
                                    .Where(kvp => kvp.Value == partitionKeyRef)
                                    .Any();
                                if (rowKeyFound && partitionKeyFound)
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
        }

        public async Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity valueExisting, IDictionary<string, EntityProperty> dictionaryExisting,
                TEntity valueUpdated, IDictionary<string, EntityProperty> dictionaryUpdated,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var existingRowKeys = GetKeys(memberInfo, valueExisting);
            var updatedRowKeys = GetKeys(memberInfo, valueUpdated);
            var rowKeysDeleted = existingRowKeys.Except(updatedRowKeys, rk => $"{rk.RowKey}|{rk.PartitionKey}");
            var rowKeysAdded = updatedRowKeys.Except(existingRowKeys, rk => $"{rk.RowKey}|{rk.PartitionKey}");
            var deletionRollbacks = rowKeysDeleted
                .Select(
                    rowKey =>
                    {
                        return MutateLookupTable(rowKey.RowKey, rowKey.PartitionKey, memberInfo,
                            repository,
                            (rowAndParitionKeys) => rowAndParitionKeys
                                .NullToEmpty()
                                .Where(kvp => kvp.Key != rowKeyRef));
                    });
            var additionRollbacks = rowKeysAdded
                 .Select(
                     rowKey =>
                     {
                         return MutateLookupTable(rowKey.RowKey, rowKey.PartitionKey, memberInfo,
                             repository,
                             (rowAndParitionKeys) => rowAndParitionKeys
                                .NullToEmpty()
                                .Append(rowKeyRef.PairWithValue(partitionKeyRef)));
                     });
            var allRollbacks = await additionRollbacks.Concat(deletionRollbacks).WhenAllAsync();
            Func<Task> allRollback =
                () =>
                {
                    var tasks = allRollbacks.Select(rb => rb());
                    return Task.WhenAll(tasks);
                };
            return onSuccessWithRollback(allRollback);
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
                (rowAndParitionKeys) => rowAndParitionKeys
                    .NullToEmpty()
                    .Where(kvp => kvp.Key != rowKeyRef && kvp.Value != partitionKeyRef),
                onSuccessWithRollback,
                onFailure);
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
                message = new[] { new WhereInformation() },
                sortKey = tableName,
            };
        }

    }
}

using BlackBarLabs;
using BlackBarLabs.Extensions;
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
    public class DateTimeLookupAttribute : Attribute,
        IModifyAzureStorageTableSave, IProvideFindBy
    {
        public string LookupTableName { get; set; }

        /// <summary>
        /// Total seconds
        /// </summary>
        public double Row { get; set; }

        /// <summary>
        /// Total seconds
        /// </summary>
        public double Partition { get; set; }

        public string ComputeLookupKey(DateTime memberValue, TimeSpan timeSpan)
        {
            var key = $"{memberValue.Year}";
            if (timeSpan.TotalDays >= 28)
                return key;
            key = $"{key}{memberValue.Month.ToString("D2")}";
            if (timeSpan.TotalDays >= 1.0)
                return key;
            key = $"{key}{memberValue.Day.ToString("D2")}";
            if (timeSpan.TotalHours >= 1.0)
                return key;
            key = $"{key}{memberValue.Hour.ToString("D2")}";
            if (timeSpan.TotalMinutes >= 60.0)
                return key;
            key = $"{key}{memberValue.Minute.ToString("D2")}";
            if (timeSpan.Seconds >= 60.0)
                return key;
            return $"{key}{memberValue.Second.ToString("D2")}";
        }

        private string GetLookupTableName(MemberInfo memberInfo)
        {
            if (LookupTableName.HasBlackSpace())
                return this.LookupTableName;
            return $"{memberInfo.DeclaringType.Name}{memberInfo.Name}";
        }

        public IEnumerableAsync<IRefAst> GetKeys(object memberValueObj,
            MemberInfo memberInfo, Driver.AzureTableDriverDynamic repository,
            KeyValuePair<MemberInfo, object>[] queries)
        {
            if (!queries.IsDefaultNullOrEmpty())
                throw new ArgumentException("Exactly one query param is valid for DateTimeLookupAttribute.");

            var memberValue = (DateTime)memberValueObj;
            var tableName = GetLookupTableName(memberInfo);
            var lookupRowKey = ComputeLookupKey(memberValue, TimeSpan.FromSeconds(this.Row));
            var lookupPartitionKey = ComputeLookupKey(memberValue, TimeSpan.FromSeconds(this.Partition));
            return repository
                .FindByIdAsync<DateTimeLookupTable, IEnumerableAsync<IRefAst>>(lookupRowKey, lookupPartitionKey,
                    (dictEntity, etag) =>
                    {
                        var rowAndParitionKeys = dictEntity.rows
                            .NullToEmpty()
                            .Zip(dictEntity.partitions,
                                (row, partition) => row.AsAstRef(partition))
                            .AsAsync();
                        return rowAndParitionKeys;
                    },
                    () => EnumerableAsync.Empty<IRefAst>(),
                    tableName: tableName)
                .FoldTask();
        }

        [StorageTable]
        public struct DateTimeLookupTable
        {

            [RowKey]
            public string rowKey;

            [ParititionKey]
            public string partitionKey;

            [Storage]
            public string[] rows;

            [Storage]
            public string[] partitions;
        }

        private DateTime? GetDtValue<TEntity>(MemberInfo memberInfo, TEntity value)
        {
            var dtValueObj = memberInfo.GetValue(value);
            var dtValueType = dtValueObj.GetType();
            if (typeof(DateTime).IsAssignableFrom(dtValueType))
            {
                var dtValue = (DateTime)dtValueObj;
                return dtValue;
            }
            if (typeof(DateTime?).IsAssignableFrom(dtValueType))
            {
                var dtValueMaybe = (DateTime?)dtValueObj;
                return dtValueMaybe;
            }
            if (typeof(DateTimeOffset).IsAssignableFrom(dtValueType))
            {
                var dtValue = (DateTimeOffset)dtValueObj;
                return dtValue.UtcDateTime;
            }
            if (typeof(DateTimeOffset?).IsAssignableFrom(dtValueType))
            {
                var dtValueMaybe = (DateTimeOffset?)dtValueObj;
                if (!dtValueMaybe.HasValue)
                    return default(DateTime?);
                return dtValueMaybe.Value.UtcDateTime;
            }
            return null;
        }

        protected string GetRowKey<TEntity>(MemberInfo memberInfo, TEntity value)
        {
            var dtMaybe = GetDtValue(memberInfo, value);
            if (!dtMaybe.HasValue)
                return null;
            var dt = dtMaybe.Value;
            var partitionKey = ComputeLookupKey(dt, TimeSpan.FromSeconds(this.Row));
            return partitionKey;
        }

        protected string GetPartitionKey<TEntity>(MemberInfo memberInfo, TEntity value)
        {
            var dtMaybe = GetDtValue(memberInfo, value);
            if (!dtMaybe.HasValue)
                return null;
            var dt = dtMaybe.Value;
            var partitionKey = ComputeLookupKey(dt, TimeSpan.FromSeconds(this.Partition));
            return partitionKey;
        }

        public virtual async Task<TResult> ExecuteAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
                Func<IEnumerable<KeyValuePair<string, string>>, IEnumerable<KeyValuePair<string, string>>> mutateCollection,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var rowKey = GetRowKey(memberInfo, value);
            var partitionKey = GetPartitionKey(memberInfo, value);
            if (rowKey.IsNullOrWhiteSpace() || partitionKey.IsNullOrWhiteSpace())
                return onSuccessWithRollback(() => 1.AsTask());
            var rollback = await MutateLookupTable(rowKey, partitionKey,
                            memberInfo, repository, mutateCollection);
            return onSuccessWithRollback(rollback);
        }

        public async Task<Func<Task>> MutateLookupTable(string rowKey, string partitionKey,
            MemberInfo memberInfo, AzureTableDriverDynamic repository,
            Func<IEnumerable<KeyValuePair<string, string>>, IEnumerable<KeyValuePair<string, string>>> mutateCollection)
        {
            var tableName = GetLookupTableName(memberInfo);
            return await repository.UpdateOrCreateAsync<DateTimeLookupTable, Func<Task>>(rowKey, partitionKey,
                async (created, lookup, saveAsync) =>
                {
                    // store for rollback
                    var rollbackRowAndPartitionKeys = lookup.rows
                        .NullToEmpty()
                        .Zip(lookup.partitions.NullToEmpty(), (k,v) => k.PairWithValue(v))
                        .ToArray();
                    await saveAsync(lookup);
                    Func<Task<bool>> rollback =
                        async () =>
                        {
                            if (created)
                            {
                                return await repository.DeleteAsync<DateTimeLookupTable, bool>(rowKey, partitionKey,
                                    () => true,
                                    () => false,
                                    tableName: tableName);
                            }
                            var table = repository.TableClient.GetTableReference(tableName);
                            return await repository.UpdateAsync<DateTimeLookupTable, bool>(rowKey, partitionKey,
                                async (modifiedDoc, saveRollbackAsync) =>
                                {
                                    bool Modified()
                                    {
                                        if (rollbackRowAndPartitionKeys.Length != modifiedDoc.rows.Length)
                                            return true;

                                        var matchKeys = rollbackRowAndPartitionKeys.SelectKeys().AsHashSet();
                                        var matchValues = rollbackRowAndPartitionKeys.SelectKeys().AsHashSet();
                                        var allRowAccountedFor = modifiedDoc.rows
                                            .All(
                                                row =>
                                                {
                                                    if (!matchKeys.Contains(row))
                                                        return false;
                                                    return true;
                                                });
                                        var allPartitionsAccountedFor = modifiedDoc.partitions
                                            .All(
                                                partition =>
                                                {
                                                    if (!matchValues.Contains(partition))
                                                        return false;
                                                    return true;
                                                });
                                        var allValuesAccountedFor = allRowAccountedFor & allPartitionsAccountedFor;
                                        return !allValuesAccountedFor;
                                    }
                                    if (!Modified())
                                        return true;
                                    modifiedDoc.rows = rollbackRowAndPartitionKeys.SelectKeys().ToArray();
                                    modifiedDoc.partitions = rollbackRowAndPartitionKeys.SelectValues().ToArray();
                                    await saveRollbackAsync(modifiedDoc);
                                    return true;
                                },
                                table: table);
                        };
                    return rollback;
                },
                tableName: tableName);
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

        public Task<TResult> ExecuteInsertOrReplaceAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var existingRowKey = GetRowKey(memberInfo, value);
            var existingPartitionKey = GetPartitionKey(memberInfo, value);
            var tableName = GetLookupTableName(memberInfo);

            return repository.FindByIdAsync<DateTimeLookupTable, TResult>(existingRowKey, existingPartitionKey,
                (lookup, etag) =>
                {
                    var rowAndParitionKeys = lookup.rows
                        .NullToEmpty()
                        .Zip(lookup.partitions.NullToEmpty(), (k, v) => k.PairWithValue(v))
                        .ToArray();
                    var rowKeyFound = rowAndParitionKeys
                            .NullToEmpty()
                            .Where(kvp => kvp.Key == rowKeyRef)
                            .Any();
                    var partitionKeyFound = rowAndParitionKeys
                            .NullToEmpty()
                            .Where(kvp => kvp.Value == partitionKeyRef)
                            .Any();
                    if (rowKeyFound && partitionKeyFound)
                        return onSuccessWithRollback(() => true.AsTask());
                    return onFailure();
                },
                onNotFound:() => onFailure(),
                tableName: tableName);
        }

        public async Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo, 
                string rowKeyRef, string partitionKeyRef, 
                TEntity valueExisting, IDictionary<string, EntityProperty> dictionaryExisting,
                TEntity valueUpdated, IDictionary<string, EntityProperty> dictionaryUpdated, 
                AzureTableDriverDynamic repository, 
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            var existingRowKey = GetRowKey(memberInfo, valueExisting);
            var updatedRowKey = GetRowKey(memberInfo, valueUpdated);
            var existingPartitionKey = GetPartitionKey(memberInfo, valueExisting);
            var updatedPartitionKey = GetPartitionKey(memberInfo, valueUpdated);

            bool Changed()
            {
                if (existingPartitionKey != updatedPartitionKey)
                    return true;
                if (existingRowKey != updatedRowKey)
                    return true;
                return false;
            }

            Func<Task> noFactor = () => 1.AsTask();
            if (!Changed())
                return onSuccessWithRollback(noFactor);

            IEnumerable<Task<Func<Task>>> GetDeleteRollbacks()
            {
                if (existingPartitionKey.IsNullOrWhiteSpace() || existingPartitionKey.IsNullOrWhiteSpace())
                    yield break;
                yield return MutateLookupTable(existingRowKey, existingPartitionKey, memberInfo,
                        repository,
                        (rowAndParitionKeys) => rowAndParitionKeys
                            .NullToEmpty()
                            .Where(kvp => kvp.Key != rowKeyRef));
            }
            var deleteRollback = GetDeleteRollbacks();

            IEnumerable<Task<Func<Task>>> GetAdditionRollbacks()
            {
                if (updatedRowKey.IsNullOrWhiteSpace() || updatedPartitionKey.IsNullOrWhiteSpace())
                    yield break;
                yield return MutateLookupTable(updatedRowKey, updatedPartitionKey, memberInfo,
                    repository,
                    (rowAndParitionKeys) => rowAndParitionKeys
                        .NullToEmpty()
                        .Append(rowKeyRef.PairWithValue(partitionKeyRef)));
            }
            var additionRollback = GetAdditionRollbacks();

            var allRollbacks = await deleteRollback.Concat(additionRollback).WhenAllAsync();
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
                    .Where(kvp => kvp.Key != rowKeyRef),
                onSuccessWithRollback,
                onFailure);
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
    }
}

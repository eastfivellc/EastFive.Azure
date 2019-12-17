using BlackBarLabs;
using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence.Azure.Attributes;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables.Backups;
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
    public class StorageLookupAttribute : Attribute,
        IModifyAzureStorageTableSave, IProvideFindBy, IBackupStorageMember, CascadeDeleteAttribute.IDeleteCascaded
    {
        public string LookupTableName { get; set; }

        public string Scope { get; set; }

        public string Cascade { get; set; }

        public Type RowKeyAttribute { get; set; }

        public Type PartitionAttribute { get; set; }

        public string GetTableName(object declaringInfo)
        {
            if (!typeof(MemberInfo).IsAssignableFrom(declaringInfo.GetType()))
                throw new ArgumentException("delcaringInfo", $"{typeof(StorageLookupAttribute).FullName} cannot decorate {declaringInfo.GetType().FullName}");
            return GetLookupTableName(declaringInfo as MemberInfo);
        }

        public string SortKey => default;

        public interface IScope
        {
            string Scope { get; }

            IRefAst MutateReference(IRefAst lookupKeyCurrent, MemberInfo key, object value);
        }

        public class ScopingAttribute : Attribute, IScope
        {
            public string Scope { get; set; }

            public bool Facet { get; set; } = false;

            public ScopingAttribute(string scope)
            {
                this.Scope = scope;
            }

            public IRefAst MutateReference(IRefAst lookupKeyCurrent, MemberInfo key, object value)
            {
                var valueStr = GetStringValue(key, value);
                if (this.Facet)
                    return lookupKeyCurrent.RowKey.AsAstRef(valueStr);
                return $"{lookupKeyCurrent.RowKey}{valueStr}".AsAstRef(lookupKeyCurrent.PartitionKey);
            }

            protected virtual string GetStringValue(MemberInfo key, object value)
            {
                if(typeof(Guid).IsAssignableFrom(key.GetMemberType()))
                {
                    var guidValue = (Guid)value;
                    return guidValue.ToString("N");
                }
                return (string)value;
            }
        }

        public class Scoping2Attribute : ScopingAttribute
        {
            public Scoping2Attribute(string scope) : base(scope)
            {
            }
        }

        public virtual string ComputeLookupRowKey(object memberValue, MemberInfo memberInfo)
        {
            if (!this.RowKeyAttribute.IsDefaultOrNull())
            {
                var rowKeyComputer = (IComputeAzureStorageTableRowKey)Activator.CreateInstance(RowKeyAttribute);
                return rowKeyComputer.ComputeRowKey(memberValue, memberInfo);
            }
            return memberInfo.StorageComputeRowKey(memberValue,
                () => new RowKeyAttribute());
        }

        public virtual string ComputeLookupPartitionKey(object memberValue, MemberInfo memberInfo, string rowKey)
        {
            if (!this.PartitionAttribute.IsDefaultOrNull())
            {
                var partitionKeyComputer = (IComputeAzureStorageTablePartitionKey)Activator.CreateInstance(PartitionAttribute);
                return partitionKeyComputer.ComputePartitionKey(memberValue, memberInfo, rowKey);
            }

            return memberInfo.StorageComputePartitionKey(memberValue, rowKey,
                () => new StandardParititionKeyAttribute());
        }

        private string GetLookupTableName(MemberInfo memberInfo)
        {
            if (LookupTableName.HasBlackSpace())
                return this.LookupTableName;
            return $"{memberInfo.DeclaringType.Name}{memberInfo.Name}";
        }

        public IEnumerableAsync<IRefAst> GetKeys(object memberValue,
            MemberInfo memberInfo, Driver.AzureTableDriverDynamic repository,
            KeyValuePair<MemberInfo, object>[] queries)
        {
            var tableName = GetLookupTableName(memberInfo);
            var lookupRowKey = ComputeLookupRowKey(memberValue, memberInfo);
            var lookupPartitionKey = ComputeLookupPartitionKey(memberValue, memberInfo, lookupRowKey);
            var lookupKey = queries
                .NullToEmpty()
                .OrderBy(kvp => kvp.Key.Name)
                .Aggregate(
                    lookupRowKey.AsAstRef(lookupPartitionKey),
                    (lookupKeyCurrent, kvp) =>
                    {
                        var scopings = kvp.Key
                            .GetAttributesInterface<IScope>()
                            .Where(attr => attr.Scope == this.Scope);
                        if (!scopings.Any())
                        {
                            // TODO: Error here if scope is set?
                            return lookupKeyCurrent;
                        }
                        var scoping = scopings.First();
                        return scoping.MutateReference(lookupKeyCurrent, kvp.Key, kvp.Value);
                    });

            var enumerableTask = repository
                .FindByIdAsync<StorageLookupTable, IEnumerable<IRefAst>>(lookupKey.RowKey, lookupKey.PartitionKey,
                    (dictEntity, etag) =>
                    {
                        var rowAndParitionKeys = dictEntity.rowAndPartitionKeys
                            .NullToEmpty()
                            .Select(rowParitionKeyKvp => rowParitionKeyKvp.Key.AsAstRef(rowParitionKeyKvp.Value));
                        return rowAndParitionKeys;
                    },
                    () => Enumerable.Empty<IRefAst>(),
                    tableName: tableName);
            return enumerableTask.FoldTask();
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

            [Storage]
            public KeyValuePair<string, string>[] rowAndPartitionKeys;
        }

        protected virtual IEnumerable<string> GetRowKeys<TEntity>(MemberInfo memberInfo, TEntity value)
        {
            var rowKeyValue = memberInfo.GetValue(value);
            var propertyValueType = memberInfo.GetMemberType();
            if (typeof(Guid).IsAssignableFrom(propertyValueType))
            {
                var guidValue = (Guid)rowKeyValue;
                return guidValue.AsRowKey().AsEnumerable();
            }
            if (typeof(IReferenceable).IsAssignableFrom(propertyValueType))
            {
                var refValue = (IReferenceable)rowKeyValue;
                if(refValue.IsDefaultOrNull())
                    return new string[] { };
                return refValue.id.AsRowKey().AsEnumerable();
            }
            if (typeof(string).IsAssignableFrom(propertyValueType))
            {
                var stringValue = (string)rowKeyValue;
                return stringValue.AsEnumerable();
            }
            if (typeof(IReferenceableOptional).IsAssignableFrom(propertyValueType))
            {
                var referenceableOptional = (IReferenceableOptional)rowKeyValue;
                if (referenceableOptional.IsDefaultOrNull())
                    return new string[] { };
                if (!referenceableOptional.HasValue)
                    return new string[] { };
                return referenceableOptional.id.Value.AsRowKey().AsEnumerable();
            }
            if (typeof(IReferences).IsAssignableFrom(propertyValueType))
            {
                var references = (IReferences)rowKeyValue;
                if (references.IsDefaultOrNull())
                    return new string[] { };
                return references.ids.Select(id => id.AsRowKey());
            }
            var exMsg = $"{this.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                $"Please override GetRowKeys on `{this.GetType().FullName}`.";
            throw new NotImplementedException(exMsg);
        }

        protected virtual string GetPartitionKey<TEntity>(string rowKey, TEntity value, MemberInfo memberInfo)
        {
            var partitionKeyAttributeType = this.PartitionAttribute.IsDefaultOrNull() ?
                  typeof(StandardParititionKeyAttribute)
                  :
                  this.PartitionAttribute;
            if (!partitionKeyAttributeType.IsSubClassOfGeneric(typeof(IModifyAzureStorageTablePartitionKey)))
                throw new Exception($"{memberInfo.DeclaringType.FullName}..{memberInfo.Name} defines partition type as {partitionKeyAttributeType.FullName} which does not implement {typeof(IModifyAzureStorageTablePartitionKey).FullName}.");
            var partitionKeyAttribute = Activator.CreateInstance(partitionKeyAttributeType) as IModifyAzureStorageTablePartitionKey;
            var partitionKey = partitionKeyAttribute.GeneratePartitionKey(rowKey, value, memberInfo);
            return partitionKey;
        }

        private IEnumerable<IRefAst> GetKeys<TEntity>(MemberInfo memberInfo, TEntity value)
        {
            var rowKeys = GetRowKeys(memberInfo, value);
            return rowKeys
                .Where(rowKey => !rowKey.IsDefaultNullOrEmpty())
                .Select(
                    rowKey =>
                    {
                        var partitionKey = GetPartitionKey(rowKey, value, memberInfo);
                        return typeof(TEntity)
                            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                            .Where(mi => mi.GetAttributesInterface<IScope>().Any())
                            .OrderBy(kvp => kvp.Name)
                            .Aggregate(
                                rowKey.AsAstRef(partitionKey),
                                (lookupKeyCurrent, mi) =>
                                {
                                    var scopings = mi
                                        .GetAttributesInterface<IScope>()
                                        .Where(attr => attr.Scope == this.Scope);
                                    if (!scopings.Any())
                                    {
                                        // TODO: Error here?
                                        return lookupKeyCurrent;
                                    }
                                    var scoping = scopings.First();
                                    var memberValue = mi.GetValue(value);
                                    return scoping.MutateReference(lookupKeyCurrent, mi, memberValue);
                                });
                    });
        }

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
                    var orignalRowAndPartitionKeys = lookup.rowAndPartitionKeys.NullToEmpty().ToArray();
                    var updatedRowAndPartitionKeys = mutateCollection(orignalRowAndPartitionKeys).ToArray();

                    if (Unmodified(orignalRowAndPartitionKeys, updatedRowAndPartitionKeys))
                        return () => true.AsTask();

                    lookup.rowAndPartitionKeys = updatedRowAndPartitionKeys;
                    await saveAsync(lookup);

                    Func<Task<bool>> rollback =
                        async () =>
                        {
                            var keysRemoved = orignalRowAndPartitionKeys
                                .Where(kvp => !updatedRowAndPartitionKeys
                                    .Any(
                                        kvp2 =>
                                            kvp2.Key == kvp.Key &&
                                            kvp2.Value == kvp.Value))
                                .ToArray();
                            var table = repository.TableClient.GetTableReference(tableName);
                            return await repository.UpdateAsync<StorageLookupTable, bool>(rowKey, partitionKey,
                                async (modifiedDoc, saveRollbackAsync) =>
                                {
                                    var currentRowAndPartitionKeys = modifiedDoc.rowAndPartitionKeys.ToDictionary();
                                    var valuesToAddBack = keysRemoved
                                        // If the item this update removed was added back, ignore it
                                        .Where(kvp => !currentRowAndPartitionKeys.ContainsKey(kvp.Key))
                                        .ToArray();
                                    var rolledBackRowAndPartitionKeys = modifiedDoc.rowAndPartitionKeys
                                        .Select(
                                            kvp =>
                                            {
                                                // If this updated didn't create/update this value, or
                                                // if the current value is not the value saved by this update:
                                                // don't modify it
                                                if (!updatedRowAndPartitionKeys.Contains(kvp))
                                                    return kvp;

                                                if (!orignalRowAndPartitionKeys.Contains(kvp))
                                                    // Item as added by this update, delete it
                                                    return default(KeyValuePair<string, string>?);

                                                return kvp;
                                            })
                                        .SelectWhereHasValue()
                                        .Concat(valuesToAddBack)
                                        .Distinct(rpKey => rpKey.Key)
                                        .ToArray();
                                    if (Unmodified(rolledBackRowAndPartitionKeys, modifiedDoc.rowAndPartitionKeys.NullToEmpty().ToArray()))
                                        return true;
                                    modifiedDoc.rowAndPartitionKeys = rolledBackRowAndPartitionKeys;
                                    await saveRollbackAsync(modifiedDoc);
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
                bool LengthModified()
                {
                    if (rollbackRowAndPartitionKeys.Length != modifiedDocRowAndPartitionKeys.Length)
                        return true;

                    return false;
                }

                var modifiedByLength = LengthModified();
                if (LengthModified())
                    return false;

                var matchKeys = rollbackRowAndPartitionKeys.ToDictionary();
                var unmodified = modifiedDocRowAndPartitionKeys
                    .All(
                        rowAndPartitionKey =>
                        {
                            if (!matchKeys.ContainsKey(rowAndPartitionKey.Key))
                                return false;
                            
                            var value = matchKeys[rowAndPartitionKey.Key];
                            if (value != rowAndPartitionKey.Value)
                                return false;

                            return true;
                        });
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
                message = new [] { new WhereInformation() },
                sortKey = tableName,
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

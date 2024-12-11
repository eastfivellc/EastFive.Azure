using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Reflection;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class StorageTableAttribute : Attribute, IProvideTable, IProvideEntity
    {
        public string TableName { get; set; }

        public IAzureStorageTableEntity<TEntity> GetEntity<TEntity>(TEntity entity)
        {
            var creatableEntity = new TableEntity<TEntity>();
            creatableEntity.Entity = entity;
            return creatableEntity;
        }

        public TEntity CreateEntityInstance<TEntity>(string rowKey, string partitionKey,
            IDictionary<string, EntityProperty> properties,
            string etag, DateTimeOffset lastUpdated)
        {
            return TableEntity<TEntity>.CreateEntityInstance(rowKey, partitionKey,
                lastUpdated, etag,
                properties);
        }

        public string GetTableName(Type tableType)
        {
            var tableName = this.TableName.HasBlackSpace() ?
                this.TableName
                :
                tableType.Name.ToLower();
            return tableName;
        }

        public CloudTable GetTable(Type tableType, CloudTableClient client)
        {
            if (tableType.IsSubClassOfGeneric(typeof(TableEntity<>)))
            {
                var genericTableType = tableType.GenericTypeArguments.First();
                return this.GetTable(genericTableType, client);
            }
            var tableName = this.GetTableName(tableType);
            var table = client.GetTableReference(tableName);
            return table;
        }

        public object GetTableQuery<TEntity>(string whereExpression = null,
            IList<string> selectColumns = default)
        {
            var query = new TableQuery<TableEntity<TEntity>>();
            if (!selectColumns.IsDefaultNullOrEmpty())
                query.SelectColumns = selectColumns;
            if (!whereExpression.HasBlackSpace())
                return query;
            return query.Where(whereExpression);
        }

        private class TableEntity<EntityType> : 
            IWrapTableEntity<EntityType>,
            IAzureStorageTableEntity<EntityType>,
            IAzureStorageTableEntityBatchable
        {
            public EntityType Entity { get; set; }

            protected string rawRowKey;
            public string RawRowKey => rawRowKey;

            protected string rawPartitionKey;
            public string RawPartitionKey => rawPartitionKey;

            protected IDictionary<string, EntityProperty> rawProperties;
            public IDictionary<string, EntityProperty> RawProperties => rawProperties;

            private static TResult GetMemberSupportingInterface<TInterface, TResult>(
                Func<MemberInfo, TInterface, TResult> onFound,
                Func<TResult> onNotFound)
            {
                return typeof(EntityType)
                    .GetMembers()
                    .SelectMany(
                        memberInfo =>
                        {
                            return memberInfo.GetAttributesInterface<TInterface>()
                                .Select(rowKeyModifier => rowKeyModifier.PairWithKey(memberInfo));
                        })
                    .First(
                        (propertyInterfaceKvp, next) =>
                        {
                            var property = propertyInterfaceKvp.Key;
                            var interfaceInstance = propertyInterfaceKvp.Value;
                            return onFound(property, interfaceInstance);
                        },
                        onNotFound);
            }

            public virtual string RowKey
            {
                get
                {
                    return GetMemberSupportingInterface<IModifyAzureStorageTableRowKey, string>(
                        (rowKeyProperty,  rowKeyGenerator) =>
                        {
                            var rowKeyValue = rowKeyGenerator.GenerateRowKey(this.Entity, rowKeyProperty);
                            return rowKeyValue;
                        },
                        () => throw new Exception("Entity does not contain row key attribute"));
                }
                set
                {
                    rawRowKey = value;
                    if(this.Entity == null)
                        this.Entity = Activator.CreateInstance<EntityType>();
                    this.Entity = SetRowKey(this.Entity, value);
                }
            }

            private static EntityType SetRowKey(EntityType entity, string value)
            {
                return GetMemberSupportingInterface<IModifyAzureStorageTableRowKey, EntityType>(
                    (rowKeyProperty, rowKeyGenerator) =>
                    {
                        return rowKeyGenerator.ParseRowKey(entity, value, rowKeyProperty);
                    },
                    () => throw new Exception("Entity does not contain row key attribute"));
            }

            public string PartitionKey
            {
                get
                {
                    return GetMemberSupportingInterface<IModifyAzureStorageTablePartitionKey, string>(
                        (partitionKeyProperty, partitionKeyGenerator) =>
                        {
                            var partitionKey = partitionKeyGenerator.GeneratePartitionKey(this.RowKey, this.Entity, partitionKeyProperty);
                            return partitionKey;
                        },
                        () => throw new Exception("Entity does not contain partition key attribute"));
                }
                set
                {
                    rawPartitionKey = value;
                    if (this.Entity == null)
                        this.Entity = Activator.CreateInstance<EntityType>();
                    this.Entity = SetPartitionKey(this.Entity, value);
                }
            }
            private static EntityType SetPartitionKey(EntityType entity, string value)
            {
                return GetMemberSupportingInterface<IModifyAzureStorageTablePartitionKey, EntityType>(
                    (partitionKeyProperty, partitionKeyGenerator) =>
                    {
                        return partitionKeyGenerator.ParsePartitionKey(entity, value, partitionKeyProperty);
                    },
                    () => throw new Exception("Entity does not contain partition key attribute"));
            }

            private DateTimeOffset timestamp;
            public DateTimeOffset Timestamp
            {
                get
                {
                    return GetMemberSupportingInterface<IModifyAzureStorageTableLastModified, DateTimeOffset>(
                        (lastModifiedProperty, lastModifiedGenerator) =>
                        {
                            var rowKeyValue = lastModifiedGenerator.GenerateLastModified(this.Entity, lastModifiedProperty);
                            return rowKeyValue;
                        },
                        () => timestamp);
                }
                set
                {
                    this.timestamp = value;
                    this.Entity = SetTimestamp(this.Entity, value);
                }
            }

            private static EntityType SetTimestamp(EntityType entity, DateTimeOffset value)
            {
                return GetMemberSupportingInterface<IModifyAzureStorageTableLastModified, EntityType>(
                    (rowKeyProperty, rowKeyGenerator) =>
                    {
                        return rowKeyGenerator.ParseLastModfied(entity, value, rowKeyProperty);
                    },
                    () =>
                    {
                        return entity;
                    });
            }

            public string ETag
            {
                get
                {
                    return GetMemberSupportingInterface<IModifyAzureStorageTableETag, string>(
                        (eTagProperty, eTagGenerator) =>
                        {
                            var rowKeyValue = eTagGenerator.GenerateETag(this.Entity, eTagProperty);
                            return rowKeyValue;
                        },
                        () => "*");
                }
                set
                {
                    this.Entity = SetETag(this.Entity, value);
                }
            }

            private static EntityType SetETag(EntityType entity, string value)
            {
                return GetMemberSupportingInterface<IModifyAzureStorageTableETag, EntityType>(
                    (eTagProperty, eTagGenerator) =>
                    {
                        return eTagGenerator.ParseETag(entity, value, eTagProperty);
                    },
                    () =>
                    {
                        return entity;
                    });
            }

            public void ReadEntity(
                IDictionary<string, EntityProperty> properties, OperationContext operationContext)
            {
                this.rawProperties = properties;
                this.Entity = CreateEntityInstance(this.Entity, properties);
            }

            public static EntityType CreateEntityInstance(string rowKey, string partitionKey,
                    DateTimeOffset timestamp, string eTag,
                IDictionary<string, EntityProperty> properties)
            {
                var entity = Activator.CreateInstance<EntityType>();
                entity = SetRowKey(entity, rowKey);
                entity = SetPartitionKey(entity, partitionKey);
                entity = SetTimestamp(entity, timestamp);
                entity = SetETag(entity, eTag);
                entity = CreateEntityInstance(entity, properties);
                return entity;
            }

            public static EntityType CreateEntityInstance(EntityType entity, IDictionary<string, EntityProperty> properties)
            {
                var storageProperties = typeof(EntityType).StorageProperties();
                foreach (var propInfoAttribute in storageProperties)
                {
                    var propInfo = propInfoAttribute.Key;
                    var attr = propInfoAttribute.Value;
                    var value = attr.GetMemberValue(propInfo, properties, out var shouldSkip);
                    if(!shouldSkip)
                        propInfo.SetValue(ref entity, value);
                }
                return entity;
            }

            public IDictionary<string, EntityProperty> WriteEntity(OperationContext operationContext)
            {
                var type = typeof(EntityType);
                var valuesToStore = type
                    .StorageProperties()
                    .SelectMany(
                        (propInfoAttribute) =>
                        {
                            var propInfo = propInfoAttribute.Key;
                            var attr = propInfoAttribute.Value;
                            var value = propInfo.GetValue(this.Entity);
                            return attr.ConvertValue(propInfo, value, this);
                        })
                    .ToDictionary();
                return valuesToStore;
            }

            #region IAzureStorageTableEntity

            private struct ExecResult
            {
                public bool success;
                public MemberInfo member;
                public Func<Task> rollback;
            }

            private async Task<TResult> GetExecutionResults<TResult>(AzureTableDriverDynamic repository,
                    Func<MemberInfo, IModifyAzureStorageTableSave, Task<ExecResult>> runModifier,
                Func<Func<Task>, TResult> onSuccessWithRollback,
                Func<MemberInfo[], TResult> onFailure)
            {
                var modifierResults = await typeof(EntityType)
                    .GetPropertyOrFieldMembers()
                    .SelectMany(memberInfo =>
                        memberInfo
                            .GetAttributesAndPropertyAttributesInterface<IModifyAzureStorageTableSave>()
                            .Select(modifier => (memberInfo, modifier)))
                    .Aggregate(
                        new ExecResult[] { }.AsTask(),
                        async (executionResultsTask, memberInfoModifer) =>
                        {
                            var executionResults = await executionResultsTask;
                            if (executionResults.Any(er => !er.success))
                                return executionResults;
                            var (memberInfo, modifier) = memberInfoModifer;
                            var executionResult = await runModifier(memberInfo, modifier);
                            return executionResults.Append(executionResult).ToArray();
                        });
                var rollbacks = modifierResults
                    .Where(result => result.success)
                    .Select(result => result.rollback());
                var failures = modifierResults
                    .Where(result => !result.success)
                    .Select(result => result.member);
                var didFail = failures.Any();
                if (didFail)
                {
                    await Task.WhenAll(rollbacks);
                    return onFailure(failures.ToArray());
                }

                return onSuccessWithRollback(
                    () => Task.WhenAll(rollbacks));
            }

            public Task<TResult> ExecuteCreateModifiersAsync<TResult>(AzureTableDriverDynamic repository,
                Func<Func<Task>, TResult> onSuccessWithRollback,
                Func<MemberInfo[], TResult> onFailure)
            {
                return GetExecutionResults(repository,
                        (memberInfo, storageModifier) =>
                        {
                            return storageModifier.ExecuteCreateAsync(memberInfo,
                                     this.RowKey, this.PartitionKey,
                                     this.Entity, this.WriteEntity(null),
                                     repository,
                                 rollback =>
                                     new ExecResult
                                     {
                                         success = true,
                                         rollback = rollback,
                                         member = memberInfo,
                                     },
                                 () =>
                                     new ExecResult
                                     {
                                         success = false,
                                         member = memberInfo,
                                     });
                        },
                    onSuccessWithRollback: onSuccessWithRollback,
                    onFailure: onFailure);
            }

            public Task<TResult> ExecuteInsertOrReplaceModifiersAsync<TResult>(AzureTableDriverDynamic repository, 
                Func<Func<Task>, TResult> onSuccessWithRollback, 
                Func<MemberInfo[], TResult> onFailure)
            {
                return GetExecutionResults(repository,
                        (memberInfo, storageModifier) =>
                        {
                            return storageModifier.ExecuteInsertOrReplaceAsync(memberInfo,
                                    this.RowKey, this.PartitionKey,
                                    this.Entity, this.WriteEntity(null),
                                    repository,
                                rollback =>
                                    new ExecResult
                                    {
                                        success = true,
                                        rollback = rollback,
                                        member = memberInfo,
                                    },
                                () =>
                                    new ExecResult
                                    {
                                        success = false,
                                        member = memberInfo,
                                    });
                        },
                    onSuccessWithRollback: onSuccessWithRollback,
                    onFailure: onFailure);
            }

            public Task<TResult> ExecuteUpdateModifiersAsync<TResult>(IAzureStorageTableEntity<EntityType> current,
                    AzureTableDriverDynamic repository,
                Func<Func<Task>, TResult> onSuccessWithRollback, 
                Func<MemberInfo[], TResult> onFailure)
            {
                return GetExecutionResults(repository,
                        (memberInfo, storageModifier) =>
                        {
                            return storageModifier.ExecuteUpdateAsync(memberInfo,
                                this, current,
                                repository,
                                rollback =>
                                {
                                    return new ExecResult
                                    {
                                        success = true,
                                        rollback = rollback,
                                        member = memberInfo,
                                    };
                                },
                                () =>
                                {
                                    return new ExecResult
                                    {
                                        success = false,
                                        rollback = default(Func<Task>),
                                        member = memberInfo,
                                    };
                                });
                        },
                    onSuccessWithRollback: onSuccessWithRollback,
                    onFailure: onFailure);
            }

            public Task<TResult> ExecuteDeleteModifiersAsync<TResult>(AzureTableDriverDynamic repository,
                Func<Func<Task>, TResult> onSuccessWithRollback, 
                Func<MemberInfo[], TResult> onFailure)
            {
                return GetExecutionResults(repository,
                        (memberInfo, storageModifier) =>
                        {
                            return storageModifier.ExecuteDeleteAsync(memberInfo,
                                    this.RowKey, this.PartitionKey,
                                    this.Entity, this.WriteEntity(null),
                                    repository,
                                rollback =>
                                {
                                    return new ExecResult
                                    {
                                        success = true,
                                        rollback = rollback,
                                        member = memberInfo,
                                    };
                                },
                                () =>
                                {
                                    return new ExecResult
                                    {
                                        success = false,
                                        rollback = default(Func<Task>),
                                        member = memberInfo,
                                    };
                                });
                        },
                    onSuccessWithRollback: onSuccessWithRollback,
                    onFailure: onFailure);
            }

            #endregion

            #region IAzureStorageTableEntityBatchable

            public IBatchModify[] BatchCreateModifiers()
            {
                return typeof(EntityType)
                    .GetPropertyAndFieldsWithAttributesInterface<IModifyAzureStorageTableSave>()
                    .SelectMany(memberInfoAndModifier =>
                        memberInfoAndModifier.Item2.GetBatchCreateModifier(memberInfoAndModifier.Item1,
                            this.RowKey, this.PartitionKey,
                            this.Entity, this.WriteEntity(null)))
                    .ToArray();
            }

            public IBatchModify[] BatchInsertOrReplaceModifiers()
            {
                throw new NotImplementedException();
            }

            public IBatchModify[] BatchDeleteModifiers()
            {
                return typeof(EntityType)
                    .GetPropertyAndFieldsWithAttributesInterface<IModifyAzureStorageTableSave>()
                    .SelectMany(memberInfoAndModifier =>
                        memberInfoAndModifier.Item2.GetBatchDeleteModifier(memberInfoAndModifier.Item1,
                            this.RowKey, this.PartitionKey,
                            this.Entity, this.WriteEntity(null)))
                    .ToArray();
            }

            #endregion
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Microsoft.WindowsAzure.Storage.Table;

using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Analytics;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Serialization;

namespace EastFive.Persistence.Azure.StorageTables.Driver
{
    public class AzureTableDriverDynamic
    {
        public const int DefaultNumberOfTimesToRetry = 10;
        protected static readonly TimeSpan DefaultBackoffForRetry = TimeSpan.FromSeconds(4);

        public readonly CloudTableClient TableClient;
        public readonly CloudBlobClient BlobClient;

        #region Init / Setup / Utility

        public AzureTableDriverDynamic(CloudStorageAccount storageAccount)
        {
            TableClient = storageAccount.CreateCloudTableClient();
            TableClient.DefaultRequestOptions.RetryPolicy =
                new ExponentialRetry(DefaultBackoffForRetry, DefaultNumberOfTimesToRetry);

            BlobClient = storageAccount.CreateCloudBlobClient();
            BlobClient.DefaultRequestOptions.RetryPolicy =
                new ExponentialRetry(DefaultBackoffForRetry, DefaultNumberOfTimesToRetry);
        }

        public static AzureTableDriverDynamic FromSettings(string settingKey = EastFive.Azure.Persistence.AppSettings.Storage)
        {
            return EastFive.Web.Configuration.Settings.GetString(settingKey,
                (storageString) => FromStorageString(storageString),
                (why) => throw new Exception(why));
        }

        public static AzureTableDriverDynamic FromStorageString(string storageSetting)
        {
            var cloudStorageAccount = CloudStorageAccount.Parse(storageSetting);
            var azureStorageRepository = new AzureTableDriverDynamic(cloudStorageAccount);
            return azureStorageRepository;
        }

        private CloudTable GetTable<TEntity>()
        {
            var tableType = typeof(TEntity);
            return TableFromEntity(tableType, this.TableClient);
        }

        private static CloudTable TableFromEntity(Type tableType, CloudTableClient tableClient)
        {
            return tableType.GetAttributesInterface<IProvideTable>()
                .First(
                    (attr, next) => attr.GetTable(tableType, tableClient),
                    () =>
                    {
                        if (tableType.IsSubClassOfGeneric(typeof(TableEntity<>)))
                        {
                            var genericTableType = tableType.GenericTypeArguments.First();
                            return TableFromEntity(genericTableType, tableClient);
                        }
                        var tableName = tableType.Name.ToLower();
                        var table = tableClient.GetTableReference(tableName);
                        return table;
                    });
        }

        #endregion

        #region ITableEntity Management

        private static IAzureStorageTableEntity<TEntity> GetEntity<TEntity>(TEntity entity)
        {
            return typeof(TEntity)
                .GetAttributesInterface<IProvideEntity>()
                .First(
                    (entityProvider, next) =>
                    {
                        return entityProvider.GetEntity(entity);
                    },
                    () =>
                    {
                        return TableEntity<TEntity>.Create(entity);
                    });
        }

        private class DeletableEntity<EntityType> : TableEntity<EntityType>
            where EntityType : IReferenceable
        {
            private string rowKeyValue;

            public override string RowKey
            {
                get => this.rowKeyValue;
                set => base.RowKey = value;
            }

            private string partitionKeyValue;

            public override string PartitionKey
            {
                get => this.partitionKeyValue;
                set => base.PartitionKey = value;
            }

            public override string ETag
            {
                get
                {
                    return "*";
                }
                set
                {
                }
            }

            internal static ITableEntity Delete(Guid rowKey)
            {
                var entityRef = rowKey.AsRef<EntityType>();
                var deletableEntity = new DeletableEntity<EntityType>();
                deletableEntity.rowKeyValue = entityRef.StorageComputeRowKey();
                deletableEntity.partitionKeyValue = entityRef.StorageComputePartitionKey();
                return deletableEntity;
            }
        }

        private class DeletableRPEntity<EntityType> : TableEntity<EntityType>
        {
            private string rowKey;
            private string partitionKey;

            public override string RowKey
            {
                get => rowKey;
                set { }
            }

            public override string PartitionKey
            {
                get => partitionKey;
                set { }
            }

            public override string ETag
            {
                get
                {
                    return "*";
                }
                set
                {
                }
            }

            internal static ITableEntity Delete(string rowKey, string partitionKey)
            {
                var deletableEntity = new DeletableRPEntity<EntityType>();
                deletableEntity.rowKey = rowKey;
                deletableEntity.partitionKey = partitionKey;
                return deletableEntity;
            }
        }

        #endregion

        #region Metadata

        public async Task<TableInformation> TableInformationAsync<TEntity>(
            CloudTable table = default(CloudTable),
            string tableName = default,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry)
            where TEntity : IReferenceable
        {
            if(table.IsDefaultOrNull())
                table = tableName.HasBlackSpace() ?
                    this.TableClient.GetTableReference(tableName)
                    :
                    GetTable<TEntity>();

            var tableQuery = TableQueryExtensions.GetTableQuery<TEntity>(
                selectColumns:new List<string> { "PartitionKey" });
            var tableEntityTypes = tableQuery.GetType().GetGenericArguments();
            var findAllIntermediate = typeof(AzureTableDriverDynamic)
                .GetMethod("FindAllInternal", BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(tableEntityTypes)
                .Invoke(null, new object[] { tableQuery, table, numberOfTimesToRetry });

            var findAllCasted = findAllIntermediate as IEnumerableAsync<IWrapTableEntity<TEntity>>;

            var tableInformationPartitions = await findAllCasted
                .AggregateAsync(
                    new TableInformation()
                    {
                        partitions = new Dictionary<string, long>(),
                    },
                    (tableInformation, resource) =>
                    {
                        if (resource.RawRowKey != resource.RowKey)
                            tableInformation.mismatchedRowKeys++;
                        var partitionKey = resource.RawPartitionKey;
                        if(partitionKey != resource.PartitionKey)
                            tableInformation.mismatchedPartitionKeys++;
                        tableInformation.partitions = tableInformation.partitions.AddIfMissing(partitionKey,
                            (addValue) => addValue(1),
                            (currrent, dict, wasAdded) =>
                            {
                                if (!wasAdded)
                                    dict[partitionKey] = currrent + 1;
                                return dict;
                            });
                        return tableInformation;
                    });
            tableInformationPartitions.total = tableInformationPartitions.partitions.SelectValues().Sum();
            return tableInformationPartitions;
        }

        #endregion

        #region Core

        #region With modifiers

        public async Task<TResult> CreateAsync<TEntity, TResult>(TEntity entity,
            Func<IAzureStorageTableEntity<TEntity>, TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
           AzureStorageDriver.RetryDelegate onTimeout = default,
           CloudTable table = default)
        {
            var tableEntity = GetEntity(entity);
            if(tableEntity.RowKey.IsNullOrWhiteSpace())
                throw new ArgumentException("RowKey must have value.");

            if(table.IsDefaultOrNull())
                table = GetTable<TEntity>();
            return await await tableEntity.ExecuteCreateModifiersAsync<Task<TResult>>(this,
                async rollback =>
                {
                    while (true)
                    {
                        try
                        {
                            var insert = TableOperation.Insert(tableEntity);
                            TableResult tableResult = await table.ExecuteAsync(insert);
                            return onSuccess(tableEntity);
                        }
                        catch (StorageException ex)
                        {
                            return await await ex.ResolveCreate(table,
                                async () => await await CreateAsync<Task<TResult>>(tableEntity, table,
                                    (ite) => onSuccess(tableEntity).AsTask(),
                                    onAlreadyExists:
                                        async () =>
                                        {
                                            await rollback();
                                            if (onAlreadyExists.IsDefaultOrNull())
                                                throw new Api.ResourceAlreadyExistsException();
                                            return onAlreadyExists();
                                        },
                                    onFailure:
                                        async (code, msg) =>
                                        {
                                            await rollback();
                                            return onFailure(code, msg);
                                        },
                                    onTimeout: onTimeout), // TODO: Handle rollback with timeout
                                onFailure:
                                    async (code, msg) =>
                                    {
                                        await rollback();
                                        return onFailure(code, msg);
                                    },
                                onAlreadyExists:
                                    async () =>
                                    {
                                        await rollback();
                                        if (onAlreadyExists.IsDefaultOrNull())
                                            throw new Api.ResourceAlreadyExistsException();
                                        return onAlreadyExists();
                                    },
                                onTimeout:onTimeout);
                        }
                        catch (Exception generalEx)
                        {
                            await rollback();
                            var message = generalEx;
                            throw;
                        }
                    }
                },
                (membersWithFailures) =>
                {
                    return onModificationFailures
                        .NullToEmpty()
                        .Where(
                            onModificationFailure =>
                            {
                                return onModificationFailure.DoesMatchMember(membersWithFailures);
                            })
                        .First<IHandleFailedModifications<TResult>, TResult>(
                            (onModificationFailure, next) => onModificationFailure.ModificationFailure(membersWithFailures),
                            () => throw new Exception("Modifiers failed to execute."))
                        .AsTask();
                });
            
        }

        private async Task<TResult> UpdateIfNotModifiedAsync<TData, TResult>(TData data,
                IAzureStorageTableEntity<TData> currentDocument,
            Func<TResult> success,
            Func<TResult> documentModified,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            AzureStorageDriver.RetryDelegate onTimeout = null,
            CloudTable table = default(CloudTable))
        {
            if (table.IsDefaultOrNull())
                table = GetTable<TData>();
            var tableData = GetEntity(data);
            var update = TableOperation.Replace(tableData);
            return await await tableData.ExecuteUpdateModifiersAsync(currentDocument, this,
                async rollback =>
                {
                    try
                    {
                        await table.ExecuteAsync(update);
                        return success();
                    }
                    catch (StorageException ex)
                    {
                        await rollback();
                        return await ex.ParseStorageException(
                            async (errorCode, errorMessage) =>
                            {
                                switch (errorCode)
                                {
                                    case ExtendedErrorInformationCodes.Timeout:
                                        {
                                            var timeoutResult = default(TResult);
                                            if (default(AzureStorageDriver.RetryDelegate) == onTimeout)
                                                onTimeout = AzureStorageDriver.GetRetryDelegate();
                                            await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                                                async () =>
                                                {
                                                    timeoutResult = await UpdateIfNotModifiedAsync(data, currentDocument,
                                                        success,
                                                        documentModified,
                                                        onFailure:onFailure,
                                                        onModificationFailures: onModificationFailures, 
                                                        onTimeout:onTimeout,
                                                            table:table);
                                                });
                                            return timeoutResult;
                                        }
                                    case ExtendedErrorInformationCodes.UpdateConditionNotSatisfied:
                                        {
                                            return documentModified();
                                        }
                                    default:
                                        {
                                            if (onFailure.IsDefaultOrNull())
                                                throw ex;
                                            return onFailure(errorCode, errorMessage);
                                        }
                                }
                            },
                            () =>
                            {
                                throw ex;
                            });
                    }
                },
                (membersWithFailures) =>
                {
                    if (onModificationFailures.IsDefaultNullOrEmpty())
                        throw new Exception("Modifiers failed to execute.");
                    return onModificationFailures
                        .NullToEmpty()
                        .Where(
                            onModificationFailure =>
                            {
                                return onModificationFailure.DoesMatchMember(membersWithFailures);
                            })
                        .First<IHandleFailedModifications<TResult>, TResult>(
                            (onModificationFailure, next) => onModificationFailure.ModificationFailure(membersWithFailures),
                            () => throw new Exception("Modifiers failed to execute."))
                        .AsTask();
                });
        }

        public Task<TResult> ReplaceAsync<TData, TResult>(TData data,
            Func<TResult> success,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            AzureStorageDriver.RetryDelegate onTimeout = null) where TData : IReferenceable
        {
            var tableData = GetEntity(data);
            return ReplaceAsync(tableData,
                success,
                onFailure,
                onTimeout);
        }

        public async Task<TResult> ReplaceAsync<TData, TResult>(IAzureStorageTableEntity<TData> tableData,
            Func<TResult> success,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            var table = GetTable<TData>();
            var update = TableOperation.Replace(tableData);
            var rollback = await tableData.ExecuteUpdateModifiersAsync(tableData, this,
                rollbacks => rollbacks,
                (members) => throw new Exception("Modifiers failed to execute."));
            try
            {
                await table.ExecuteAsync(update);
                return success();
            }
            catch (StorageException ex)
            {
                await rollback();
                return await ex.ParseStorageException(
                    async (errorCode, errorMessage) =>
                    {
                        switch (errorCode)
                        {
                            case ExtendedErrorInformationCodes.Timeout:
                                {
                                    var timeoutResult = default(TResult);
                                    if (default(AzureStorageDriver.RetryDelegate) == onTimeout)
                                        onTimeout = AzureStorageDriver.GetRetryDelegate();
                                    await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                                        async () =>
                                        {
                                            timeoutResult = await ReplaceAsync(tableData,
                                                success, onFailure, onTimeout);
                                        });
                                    return timeoutResult;
                                }
                            default:
                                {
                                    if (onFailure.IsDefaultOrNull())
                                        throw ex;
                                    return onFailure(errorCode, errorMessage);
                                }
                        }
                    },
                    () =>
                    {
                        throw ex;
                    });
            }
        }


        #endregion

        #region Without Modifiers

        #region Mutation

        public async Task<TResult> CreateAsync<TResult>(ITableEntity tableEntity,
                CloudTable table,
            Func<ITableEntity, TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
           AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            while (true)
            {
                try
                {
                    var insert = TableOperation.Insert(tableEntity);
                    TableResult tableResult = await table.ExecuteAsync(insert);
                    return onSuccess(tableResult.Result as ITableEntity);
                }
                catch (StorageException ex)
                {
                    bool shouldRetry = false; // TODO: This is funky
                    var r = await ex.ResolveCreate(table,
                        () =>
                        {
                            shouldRetry = true;
                            return default;
                        },
                        onFailure: onFailure,
                        onAlreadyExists: onAlreadyExists,
                        onTimeout: onTimeout);

                    if (shouldRetry)
                        continue;
                    return r;
                }
                catch (Exception generalEx)
                {
                    var message = generalEx;
                    throw;
                }
            };
        }

        public async Task<TResult> ReplaceAsync<TData, TResult>(ITableEntity tableEntity,
            Func<TResult> success,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            var table = GetTable<TData>();
            var update = TableOperation.Replace(tableEntity);
            try
            {
                await table.ExecuteAsync(update);
                return success();
            }
            catch (StorageException ex)
            {
                return await ex.ParseStorageException(
                    async (errorCode, errorMessage) =>
                    {
                        switch (errorCode)
                        {
                            case ExtendedErrorInformationCodes.Timeout:
                                {
                                    var timeoutResult = default(TResult);
                                    if (default(AzureStorageDriver.RetryDelegate) == onTimeout)
                                        onTimeout = AzureStorageDriver.GetRetryDelegate();
                                    await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                                        async () =>
                                        {
                                            timeoutResult = await ReplaceAsync<TData, TResult>(tableEntity,
                                                success, onFailure, onTimeout);
                                        });
                                    return timeoutResult;
                                }
                            default:
                                {
                                    if (onFailure.IsDefaultOrNull())
                                        throw ex;
                                    return onFailure(errorCode, errorMessage);
                                }
                        }
                    },
                    () =>
                    {
                        throw ex;
                    });
            }
        }

        public async Task<TResult> InsertOrReplaceAsync<TData, TResult>(ITableEntity tableEntity,
            Func<bool, ITableEntity, TResult> success,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            var table = GetTable<TData>();
            var update = TableOperation.InsertOrReplace(tableEntity);
            try
            {
                TableResult result = await table.ExecuteAsync(update);
                var created = result.HttpStatusCode == ((int)HttpStatusCode.Created);
                var entity = result.Result as ITableEntity;
                return success(created, entity);
            }
            catch (StorageException ex)
            {
                return await ex.ParseStorageException(
                    async (errorCode, errorMessage) =>
                    {
                        switch (errorCode)
                        {
                            case ExtendedErrorInformationCodes.Timeout:
                                {
                                    var timeoutResult = default(TResult);
                                    if (default(AzureStorageDriver.RetryDelegate) == onTimeout)
                                        onTimeout = AzureStorageDriver.GetRetryDelegate();
                                    await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                                        async () =>
                                        {
                                            timeoutResult = await InsertOrReplaceAsync<TData, TResult>(tableEntity,
                                                success, onFailure, onTimeout);
                                        });
                                    return timeoutResult;
                                }
                            default:
                                {
                                    if (onFailure.IsDefaultOrNull())
                                        throw ex;
                                    return onFailure(errorCode, errorMessage);
                                }
                        }
                    },
                    () =>
                    {
                        throw ex;
                    });
            }
        }

        private async Task<TResult> DeleteAsync<TResult>(ITableEntity entity, CloudTable table,
            Func<TResult> success,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var delete = TableOperation.Delete(entity);
            try
            {
                var response = await table.ExecuteAsync(delete);
                if (response.HttpStatusCode == (int)HttpStatusCode.NotFound)
                    return onNotFound();
                return success();
            }
            catch (StorageException se)
            {
                return await se.ParseStorageException(
                    async (errorCode, errorMessage) =>
                    {
                        switch (errorCode)
                        {
                            case ExtendedErrorInformationCodes.Timeout:
                                {
                                    var timeoutResult = default(TResult);
                                    if (default(AzureStorageDriver.RetryDelegate) == onTimeout)
                                        onTimeout = AzureStorageDriver.GetRetryDelegate();
                                    await onTimeout(se.RequestInformation.HttpStatusCode, se,
                                        async () =>
                                        {
                                            timeoutResult = await DeleteAsync<TResult>(
                                                entity, table, success, onNotFound, onFailure, onTimeout);
                                        });
                                    return timeoutResult;
                                }
                            case ExtendedErrorInformationCodes.TableNotFound:
                            case ExtendedErrorInformationCodes.TableBeingDeleted:
                                {
                                    return onNotFound();
                                }
                            default:
                                {
                                    if (se.IsProblemDoesNotExist())
                                        return onNotFound();
                                    if (onFailure.IsDefaultOrNull())
                                        throw se;
                                    return onFailure(errorCode, errorMessage);
                                }
                        }
                    },
                    () =>
                    {
                        throw se;
                    });
            }
        }

        #endregion

        #region Find

        public async Task<TResult> FindByIdAsync<TEntity, TResult>(
                string rowKey, string partitionKey,
            Func<TEntity, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure =
                default(Func<ExtendedErrorInformationCodes, string, TResult>),
            CloudTable table = default(CloudTable),
            string tableName = default(string),
            AzureStorageDriver.RetryDelegate onTimeout =
                default(AzureStorageDriver.RetryDelegate))
        {
            var operation = TableOperation.Retrieve(partitionKey, rowKey,
                (string partitionKeyEntity, string rowKeyEntity, DateTimeOffset timestamp, IDictionary<string, EntityProperty> properties, string etag) =>
                {
                    return typeof(TEntity)
                        .GetAttributesInterface<IProvideEntity>()
                        .First(
                            (entityProvider, next) =>
                            {
                                var entityPopulated = entityProvider.CreateEntityInstance<TEntity>(
                                    rowKeyEntity, partitionKeyEntity, properties, etag, timestamp);
                                return entityPopulated;
                            },
                            () =>
                            {
                                var entityPopulated = TableEntity<TEntity>.CreateEntityInstance(properties);
                                return entityPopulated;
                            });
                });
            if (table.IsDefaultOrNull())
                table = tableName.HasBlackSpace()?
                    this.TableClient.GetTableReference(tableName)
                    :
                    table = GetTable<TEntity>();
            try
            {
                var result = await table.ExecuteAsync(operation);
                if (404 == result.HttpStatusCode)
                    return onNotFound();
                return onSuccess((TEntity)result.Result);
            }
            catch (StorageException se)
            {
                if (se.IsProblemTableDoesNotExist())
                    return onNotFound();
                if (se.IsProblemTimeout())
                {
                    TResult result = default(TResult);
                    if (default(AzureStorageDriver.RetryDelegate) == onTimeout)
                        onTimeout = AzureStorageDriver.GetRetryDelegate();
                    await onTimeout(se.RequestInformation.HttpStatusCode, se,
                        async () =>
                        {
                            result = await FindByIdAsync(rowKey, partitionKey,
                                onSuccess, onNotFound, onFailure,
                                    table:table, onTimeout:onTimeout);
                        });
                    return result;
                }
                throw se;
            }
            catch(Exception ex)
            {
                ex.GetType();
                throw ex;
            }

        }

        public async Task<TResult> FindByIdAsync<TResult>(
                string rowKey, string partitionKey,
                Type typeData,
            Func<object, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            CloudTable table = default,
            string tableName = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var operation = TableOperation.Retrieve(partitionKey, rowKey,
                (string partitionKeyEntity, string rowKeyEntity,
                 DateTimeOffset timestamp, IDictionary<string, EntityProperty> properties, string etag) =>
                {
                    return typeData
                        .GetAttributesInterface<IProvideEntity>()
                        .First<IProvideEntity, object>(
                            (entityProvider, next) =>
                            {
                                // READ AS:
                                //var entityPopulated = entityProvider.CreateEntityInstance<TEntity>(
                                //    rowKeyEntity, partitionKeyEntity, properties, etag, timestamp);

                                var entityPopulated = entityProvider.GetType()
                                    .GetMethod("CreateEntityInstance", BindingFlags.Instance | BindingFlags.Public)
                                    .MakeGenericMethod(typeData.AsArray())
                                    .Invoke(entityProvider,
                                        new object[] { rowKeyEntity, partitionKeyEntity, properties, etag, timestamp });
                                    
                                return entityPopulated;
                            },
                            () =>
                            {
                                throw new Exception($"No attributes of type IProvideEntity on {typeData.FullName}.");
                            });
                });
            table = GetTable();
            CloudTable GetTable()
            {
                if (!table.IsDefaultOrNull())
                    return table;

                if (tableName.HasBlackSpace())
                    return this.TableClient.GetTableReference(tableName);

                //READ AS: return GetTable<TEntity>();
                return (CloudTable) typeof(AzureTableDriverDynamic)
                    .GetMethod("GetTable", BindingFlags.NonPublic | BindingFlags.Instance)
                    .MakeGenericMethod(typeData.AsArray())
                    .Invoke(this, new object[] { });
            }
            try
            {
                var result = await table.ExecuteAsync(operation);
                if (404 == result.HttpStatusCode)
                    return onNotFound();
                return onSuccess(result.Result);
            }
            catch (StorageException se)
            {
                if (se.IsProblemTableDoesNotExist())
                    return onNotFound();
                if (se.IsProblemTimeout())
                {
                    TResult result = default(TResult);
                    if (default(AzureStorageDriver.RetryDelegate) == onTimeout)
                        onTimeout = AzureStorageDriver.GetRetryDelegate();
                    await onTimeout(se.RequestInformation.HttpStatusCode, se,
                        async () =>
                        {
                            result = await FindByIdAsync(rowKey, partitionKey, typeData,
                                onSuccess, onNotFound, onFailure,
                                    table: table, onTimeout: onTimeout);
                        });
                    return result;
                }
                throw se;
            }
            catch (Exception ex)
            {
                ex.GetType();
                throw ex;
            }

        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRef<TRefEntity>>> by)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef, by);
        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRef<TRefEntity>>> by,
                Expression<Func<TEntity, bool>> query1 = default,
                Expression<Func<TEntity, bool>> query2 = default)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef, by, query1, query2);
        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRef<IReferenceable>>> by)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef, by);
        }

        public IEnumerableAsync<TEntity> FindBy<TEntity>(Guid entityId,
                Expression<Func<TEntity, Guid>> by)
            where TEntity : IReferenceable
        {
            return FindByInternal(entityId.AsRef<IReferenceable>(), by);
        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRefOptional<TRefEntity>>> by)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef.Optional(), by);
        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRefs<TRefEntity>>> by)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef, by);
        }

        private IEnumerableAsync<TEntity> FindByInternal<TMatch, TEntity>(object findByValue,
                Expression<Func<TEntity, TMatch>> by,
                params Expression<Func<TEntity, bool>> [] queries)
            where TEntity : IReferenceable
        {
            return by.MemberInfo(
                (memberCandidate, expr) =>
                {
                    return memberCandidate
                        .GetAttributesInterface<IProvideFindBy>()
                        .First<IProvideFindBy, IEnumerableAsync<TEntity>>(
                            (attr, next) =>
                            {
                                var memberAssignments = queries
                                    .Select(
                                        query =>
                                        {
                                            var memberInfo = (query).MemberComparison(out ExpressionType operand, out object value);
                                            return memberInfo.PairWithValue(value);
                                        })
                                    .ToArray();

                                return attr.GetKeys(findByValue, memberCandidate, this, memberAssignments)
                                    .Select(
                                        rowParitionKeyKvp =>
                                        {
                                            var rowKey = rowParitionKeyKvp.RowKey;
                                            var partitionKey = rowParitionKeyKvp.ParitionKey;
                                            return this.FindByIdAsync(rowKey, partitionKey,
                                                    (TEntity entity) => entity.PairWithKey(true),
                                                    () => default(TEntity).PairWithKey(false),
                                                    onFailure: (code, msg) => default(TEntity).PairWithKey(false));
                                        })
                                    .Await()
                                    .Where(kvp => kvp.Key)
                                    .SelectValues();
                            },
                            () =>
                            {
                                throw new ArgumentException("TEntity does not contain an attribute of type IProvideFindBy.");
                            });
                },
                () => throw new Exception());
        }

        private Task<TResult> FindByInternalAsync<TRefEntity, TMatch, TEntity, TResult>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, TMatch>> by,
            Func<IEnumerableAsync<TEntity>, Func<KeyValuePair<ExtendedErrorInformationCodes, string>[]>, TResult> onFound,
            Func<TResult> onRefNotFound = default)
            where TEntity : struct, IReferenceable
            where TRefEntity : IReferenceable
        {
            return by.MemberInfo(
                (memberInfo, expr) =>
                {
                    return MemberExpr(memberInfo);
                    Task<TResult> MemberExpr(MemberInfo memberCandidate)
                    {
                        return memberCandidate
                            .GetAttributesInterface<IProvideFindByAsync>()
                            .First<IProvideFindByAsync, Task<TResult>>(
                                (attr, next) =>
                                {
                                    return attr.GetKeysAsync(entityRef, this, memberCandidate,
                                        (keys) =>
                                        {
                                            var failures = new KeyValuePair<ExtendedErrorInformationCodes, string>[] { };
                                            var results = keys
                                                .Select(
                                                    rowParitionKeyKvp =>
                                                    {
                                                        var rowKey = rowParitionKeyKvp.Key;
                                                        var partitionKey = rowParitionKeyKvp.Value;
                                                        return this.FindByIdAsync(rowKey, partitionKey,
                                                            (TEntity entity) => entity,
                                                            () => default(TEntity?),
                                                            onFailure:
                                                                (code, msg) =>
                                                                {
                                                                    failures = failures
                                                                        .Append(code.PairWithValue(msg))
                                                                        .ToArray();
                                                                    return default(TEntity?);
                                                                });
                                                    })
                                                .Await()
                                                .SelectWhereHasValue();
                                            return onFound(results,
                                                () => failures);
                                        },
                                        () =>
                                        {
                                            if (!onRefNotFound.IsDefaultOrNull())
                                                return onRefNotFound();
                                            var emptyResults = EnumerableAsync.Empty<TEntity>();
                                            return onFound(emptyResults,
                                                () => new KeyValuePair<ExtendedErrorInformationCodes, string>[] { });
                                        });
                                },
                                () =>
                                {
                                    if (expr is MemberExpression)
                                    {
                                        var exprFunc = expr as MemberExpression;
                                        return MemberExpr(exprFunc.Member);
                                    }
                                    throw new Exception();
                                });
                    }
                },
                () => throw new Exception());
        }

        public static IEnumerableAsync<TEntity> FindAllInternal<TEntity>(
            TableQuery<TEntity> query,
            CloudTable table,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry)
            where TEntity : ITableEntity, new()
        {
            var token = default(TableContinuationToken);
            var segmentFecthing = table.ExecuteQuerySegmentedAsync(query, token);
            return EnumerableAsync.YieldBatch<TEntity>(
                async (yieldReturn, yieldBreak) =>
                {
                    if (segmentFecthing.IsDefaultOrNull())
                        return yieldBreak;
                    try
                    {
                        var segment = await segmentFecthing;
                        if (segment.IsDefaultOrNull())
                            return yieldBreak;

                        token = segment.ContinuationToken;
                        segmentFecthing = token.IsDefaultOrNull()?
                            default(Task<TableQuerySegment<TEntity>>)
                            :
                            table.ExecuteQuerySegmentedAsync(query, token);
                        var results = segment.Results.ToArray();
                        return yieldReturn(results);
                    }
                    catch (AggregateException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!table.Exists())
                            return yieldBreak;
                        if (ex is StorageException except && except.IsProblemTimeout())
                        {
                            if (--numberOfTimesToRetry > 0)
                            {
                                await Task.Delay(DefaultBackoffForRetry);
                                segmentFecthing = token.IsDefaultOrNull() ?
                                    default(Task<TableQuerySegment<TEntity>>)
                                    :
                                    table.ExecuteQuerySegmentedAsync(query, token);
                                return yieldReturn(new TEntity[] { });
                            }
                        }
                        throw;
                    }
                });
        }

        #endregion

        #region Batch

        public async Task<TableResult[]> CreateOrReplaceBatchAsync<TDocument>(string partitionKey, TDocument[] entities,
            CloudTable table = default(CloudTable),
                AzureStorageDriver.RetryDelegate onTimeout = default(AzureStorageDriver.RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
            where TDocument : class, ITableEntity
        {
            if (!entities.Any())
                return new TableResult[] { };

            if(table.IsDefaultOrNull())
                table = GetTable<TDocument>();
            var bucketCount = (entities.Length / 100) + 1;
            diagnostics.Trace($"{entities.Length} rows for partition `{partitionKey}`.");

            var batch = new TableBatchOperation();
            var rowKeyHash = new HashSet<string>();
            foreach (var row in entities)
            {
                if (rowKeyHash.Contains(row.RowKey))
                {
                    diagnostics.Warning($"Duplicate rowkey `{row.RowKey}`.");
                    continue;
                }
                batch.InsertOrReplace(row);
            }

            // submit
            while (true)
            {
                try
                {
                    var resultList = await table.ExecuteBatchAsync(batch);
                    return resultList.ToArray();
                }
                catch (StorageException storageException)
                {
                    var shouldRetry = await storageException.ResolveCreate(table,
                        () => true,
                        onTimeout: onTimeout);
                    if (shouldRetry)
                        continue;

                }
            }
        }

        public IEnumerableAsync<TableResult> DeleteAll<TEntity>(
            Expression<Func<TEntity, bool>> filter,
            CloudTable table = default(CloudTable),
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry) 
            where TEntity : IReferenceable
        {
            var finds = FindAll(filter, table, numberOfTimesToRetry);
            var deleted = finds
                .Select(entity => GetEntity(entity))
                .GroupBy(doc => doc.PartitionKey)
                .Select(
                    rowsToDeleteGrp =>
                    {
                        var partitionKey = rowsToDeleteGrp.Key;
                        var deletions = rowsToDeleteGrp
                            .Batch()
                            .Select(items => DeleteBatchAsync<TEntity>(partitionKey, items))
                            .Await()
                            .SelectMany();
                        return deletions;
                    })
               .SelectAsyncMany();
            return deleted;
        }

        private async Task<TableResult[]> DeleteBatchAsync<TEntity>(string partitionKey, ITableEntity[] entities,
            CloudTable table = default(CloudTable),
            EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
        {
            if (!entities.Any())
                return new TableResult[] { };

            if(table.IsDefaultOrNull())
                table = GetTable<TEntity>();

            var batch = new TableBatchOperation();
            var rowKeyHash = new HashSet<string>();
            foreach (var row in entities)
            {
                if (rowKeyHash.Contains(row.RowKey))
                {
                    continue;
                }
                batch.Delete(row);
            }

            // submit
            while (true)
            {
                try
                {
                    var resultList = await table.ExecuteBatchAsync(batch);
                    return resultList.ToArray();
                }
                catch (StorageException storageException)
                {
                    if (storageException.IsProblemTableDoesNotExist())
                        return new TableResult[] { };
                    throw storageException;
                }
            }
        }

        #endregion

        #endregion

        #endregion

        #region CREATE

        /// <summary>
        /// Table is created using <paramref name="tableName"/> and no modifiers are executed.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="tableEntity"></param>
        /// <param name="tableName"></param>
        /// <param name="onSuccess"></param>
        /// <param name="onAlreadyExists"></param>
        /// <param name="onFailure"></param>
        /// <param name="onTimeout"></param>
        /// <remarks>Does not execute modifiers</remarks>
        /// <returns></returns>
        public Task<TResult> CreateAsync<TResult>(ITableEntity tableEntity,
                string tableName,
            Func<ITableEntity, TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var table = this.TableClient.GetTableReference(tableName);
            return this.CreateAsync(tableEntity, table,
                onSuccess,
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                onTimeout: onTimeout);
        }

        /// <summary>
        /// Table is created using TEntity as the type and no modifiers are executed.
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="tableEntity"></param>
        /// <param name="onSuccess"></param>
        /// <param name="onAlreadyExists"></param>
        /// <param name="onFailure"></param>
        /// <param name="onTimeout"></param>
        /// <remarks>Does not execute modifiers</remarks>
        /// <returns></returns>
        public Task<TResult> CreateAsync<TEntity, TResult>(ITableEntity tableEntity,
            Func<ITableEntity, TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var table = GetTable<TEntity>();
            return this.CreateAsync(tableEntity, table,
                onSuccess,
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                onTimeout: onTimeout);
        }

        /// <summary>
        /// Table is created using <paramref name="entityType"/> and no modifiers are executed.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="tableEntity"></param>
        /// <param name="entityType"></param>
        /// <param name="onSuccess"></param>
        /// <param name="onAlreadyExists"></param>
        /// <param name="onFailure"></param>
        /// <param name="onTimeout"></param>
        /// <remarks>Does not execute modifiers</remarks>
        /// <returns></returns>
        public Task<TResult> CreateAsync<TResult>(ITableEntity tableEntity, Type entityType,
            Func<ITableEntity, TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var table = TableFromEntity(entityType, this.TableClient);
            return this.CreateAsync(tableEntity, table,
                onSuccess,
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                onTimeout: onTimeout);
        }

        public Task<TResult> UpdateOrCreateAsync<TData, TResult>(string rowKey, string partitionKey,
                Func<TData, TData> setId,
            Func<bool, TData, Func<TData, Task>, Task<TResult>> onUpdate,
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default,
            string tableName = default(string))
        {
            var table = default(CloudTable);
            if (tableName.HasBlackSpace())
                table = this.TableClient.GetTableReference(tableName);
            return this.UpdateAsyncAsync<TData, TResult>(rowKey, partitionKey,
                (doc, saveAsync) => onUpdate(false, doc, saveAsync),
                async () =>
                {
                    var doc = Activator.CreateInstance<TData>();
                    doc = setId(doc);
                    var global = default(TResult);
                    bool useGlobal = false;
                    var result = await onUpdate(true, doc,
                        async (docUpdated) =>
                        {
                            if (await this.CreateAsync(docUpdated,
                                discard => true,
                                () => false,
                                table:table))
                                return;
                            global = await this.UpdateOrCreateAsync<TData, TResult>(rowKey, partitionKey,
                                    setId,
                                onUpdate,
                                onTimeoutAsync:onTimeoutAsync, 
                                tableName:tableName);
                            useGlobal = true;
                        });
                    if (useGlobal)
                        return global;
                    return result;
                },
                table:table);
        }

        public Task<TResult> UpdateOrCreateAsync<TData, TResult>(string rowKey, string partitionKey,
            Func<bool, TData, Func<TData, Task>, Task<TResult>> onUpdate,
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default,
            string tableName = default(string)) 
        {
            var table = default(CloudTable);
            if (tableName.HasBlackSpace())
                table = this.TableClient.GetTableReference(tableName);
            return this.UpdateAsyncAsync<TData, TResult>(rowKey, partitionKey,
                (doc, saveAsync) => onUpdate(false, doc, saveAsync),
                async () =>
                {
                    var doc = Activator.CreateInstance<TData>();
                    doc = doc
                        .StorageParseRowKey(rowKey)
                        .StorageParsePartitionKey(partitionKey);
                    var global = default(TResult);
                    bool useGlobal = false;
                    var result = await onUpdate(true, doc,
                        async (docUpdated) =>
                        {
                            if (await this.CreateAsync(docUpdated,
                                discard => true,
                                () => false,
                                table: table))
                                return;
                            global = await this.UpdateOrCreateAsync<TData, TResult>(
                                    rowKey, partitionKey,
                                onUpdate,
                                onTimeoutAsync: onTimeoutAsync,
                                tableName: tableName);
                            useGlobal = true;
                        });
                    if (useGlobal)
                        return global;
                    return result;
                },
                table: table);
        }

        #endregion

        #region Find

        public Task<TResult> FindByIdAsync<TEntity, TResult>(
                Guid rowId,
            Func<TEntity, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure =
                default(Func<ExtendedErrorInformationCodes, string, TResult>),
            CloudTable table = default(CloudTable),
            AzureStorageDriver.RetryDelegate onTimeout =
                default(AzureStorageDriver.RetryDelegate))
            where TEntity : IReferenceable
        {
            var entityRef = rowId.AsRef<TEntity>();
            var rowKey = entityRef.StorageComputeRowKey();
            var partitionKey = entityRef.StorageComputePartitionKey();
            return FindByIdAsync(rowKey, partitionKey,
                onSuccess: onSuccess, onNotFound: onNotFound, onFailure:onFailure,
                table:table,
                onTimeout:onTimeout);
        }

        //public Task<TResult> FindByIdAsync<TEntity, TResult>(
        //        string rowKey,
        //    Func<TEntity, TResult> onSuccess,
        //    Func<TResult> onNotFound,
        //    Func<ExtendedErrorInformationCodes, string, TResult> onFailure =
        //        default(Func<ExtendedErrorInformationCodes, string, TResult>),
        //    CloudTable table = default(CloudTable),
        //    AzureStorageDriver.RetryDelegate onTimeout =
        //        default(AzureStorageDriver.RetryDelegate))
        //{
        //    return FindByIdAsync(rowKey, rowKey.GeneratePartitionKey(),
        //        onSuccess, onNotFound, onFailure, table:table, onTimeout:onTimeout);
        //}

        public IEnumerableAsync<TEntity> FindByIdsAsync<TEntity>(
                IRefAst[] rowKeys,
            CloudTable table = default(CloudTable),
            AzureStorageDriver.RetryDelegate onTimeout =
                default(AzureStorageDriver.RetryDelegate))
            where TEntity : IReferenceable
        {
            if (table.IsDefaultOrNull())
                table = GetTable<TEntity>();
            return rowKeys
                .Select(
                    rowKey =>
                    {
                        return FindByIdAsync<TEntity, KeyValuePair<bool, TEntity>?>(rowKey.Key, rowKey.Value,
                            (entity) => entity.PairWithKey(true),
                            () => default(KeyValuePair<bool, TEntity>?),
                            table: table,
                            onTimeout: onTimeout);
                    })
                .AsyncEnumerable()
                .SelectWhereHasValue()
                .SelectValues();
        }

        public IEnumerableAsync<TableResult> Copy(
            string filter,
            string tableName,
            AzureTableDriverDynamic copyTo,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry)
        {
            var table = this.TableClient.GetTableReference(tableName);
            var query = new TableQuery<GenericTableEntity>();
            var filteredQuery = query.Where(filter);
            var allRows = FindAllInternal(filteredQuery, table, numberOfTimesToRetry);
            return copyTo.CreateOrReplaceBatch(allRows,
                row => row.RowKey,
                row => row.PartitionKey,
                (row, action) => action,
                tableName);
        }

        public IEnumerableAsync<TEntity> FindAll<TEntity>(
            Expression<Func<TEntity, bool>> filter,
            CloudTable table = default(CloudTable),
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry)
        {
            var query = filter.ResolveExpression(out Func<TEntity, bool> postFilter);
            var tableEntityTypes = query.GetType().GetGenericArguments();
            if (table.IsDefaultOrNull())
            {
                var tableEntityType = tableEntityTypes.First();
                if(tableEntityType.IsSubClassOfGeneric(typeof(IWrapTableEntity<>)))
                {
                    tableEntityType = tableEntityType.GetGenericArguments().First();
                }
                table = AzureTableDriverDynamic.TableFromEntity(tableEntityType, this.TableClient);
            }

            var findAllIntermediate = typeof(AzureTableDriverDynamic)
                .GetMethod("FindAllInternal", BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(tableEntityTypes)
                .Invoke(null, new object[] { query, table, numberOfTimesToRetry });
            var findAllCasted = findAllIntermediate as IEnumerableAsync<IWrapTableEntity<TEntity>>;
            return findAllCasted
                .Select(segResult => segResult.Entity)
                .Where(f => postFilter(f));
        }

        public IEnumerableAsync<TData> FindByPartition<TData>(string partitionKeyValue,
            string tableName = default(string))
            where TData  : ITableEntity, new()
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TData>();
            string filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKeyValue);
            var tableQuery = new TableQuery<TData>().Where(filter);
            return FindAllInternal(tableQuery, table);
        }

        public IEnumerableAsync<TEntity> FindEntityBypartition<TEntity>(string partitionKeyValue,
            string tableName = default(string),
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry)
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TEntity>();
            string filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKeyValue);
            var tableQuery = TableQueryExtensions.GetTableQuery<TEntity>(filter);
            var tableEntityTypes = tableQuery.GetType().GetGenericArguments();
            var findAllIntermediate = typeof(AzureTableDriverDynamic)
                .GetMethod("FindAllInternal", BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(tableEntityTypes)
                .Invoke(null, new object[] { tableQuery, table, numberOfTimesToRetry });
            var findAllCasted = findAllIntermediate as IEnumerableAsync<IWrapTableEntity<TEntity>>;
            return findAllCasted
                .Select(segResult => segResult.Entity);
        }

        #endregion

        #region Update

        [Obsolete("Use string based row/partition access")]
        public async Task<TResult> UpdateAsync<TData, TResult>(Guid documentId,
            Func<TData, Func<TData, Task>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>),
                Func<string> getPartitionKey = default(Func<string>))
        {
            if (default(Func<string>) == getPartitionKey)
                getPartitionKey = () => documentId.AsRowKey().GeneratePartitionKey();

            var rowKey = documentId.AsRowKey();
            var partitionKey = getPartitionKey();
            return await UpdateAsync(rowKey, partitionKey, onUpdate, onNotFound);
        }

        [Obsolete("Use string based row/partition access")]
        public async Task<TResult> UpdateAsync<TData, TResult>(Guid documentId, string partitionKey,
            Func<TData, Func<TData, Task>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>))
        {
            var rowKey = documentId.AsRowKey();
            return await UpdateAsync(rowKey, partitionKey, onUpdate, onNotFound);
        }

        public Task<TResult> UpdateAsync<TData, TResult>(string rowKey, string partitionKey,
            Func<TData, Func<TData, Task>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            CloudTable table = default(CloudTable),
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync = 
                default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>))
        {
            return UpdateAsyncAsync(rowKey, partitionKey,
                onUpdate, 
                onNotFound.AsAsyncFunc(),
                onModificationFailures: onModificationFailures,
                    table:table, onTimeoutAsync:onTimeoutAsync);
        }

        public Task<TResult> UpdateAsync<TResult>(string rowKey, string partitionKey,
                Type typeData,
            Func<object, Func<object, Task>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            string tableName = default,
            CloudTable table = default,
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>))
        {
            return UpdateAsyncAsync(rowKey, partitionKey,
                    typeData,
                onUpdate,
                onNotFound.AsAsyncFunc(),
                    tableName:tableName,
                    table:table,
                    onTimeoutAsync:onTimeoutAsync);
        }

        public async Task<TResult> UpdateAsyncAsync<TData, TResult>(Guid documentId,
            Func<TData, Func<TData, Task>, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default(Func<Task<TResult>>),
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>))
            where TData : IReferenceable
        {
            var entityRef = documentId.AsRef<TData>();
            var rowKey = entityRef.StorageComputeRowKey();
            var partitionKey = entityRef.StorageComputePartitionKey();
            return await UpdateAsyncAsync(rowKey, partitionKey, onUpdate, onNotFound);
        }

        private class UpdateModificationFailure<TResult> : IHandleFailedModifications<Task<bool>>
        {
            public IHandleFailedModifications<TResult>[] onModificationFailures;

            public Action<TResult> setGlobalCallback;

            public bool DoesMatchMember(MemberInfo[] membersWithFailures)
            {
                return true;
            }

            Task<bool> IHandleFailedModifications<Task<bool>>.ModificationFailure(MemberInfo[] membersWithFailures)
            {
                var result = onModificationFailures
                    .NullToEmpty()
                    .Where(
                        onModificationFailure =>
                        {
                            return onModificationFailure.DoesMatchMember(membersWithFailures);
                        })
                    .First<IHandleFailedModifications<TResult>, TResult>(
                        (onModificationFailure, next) => onModificationFailure.ModificationFailure(membersWithFailures),
                            () => throw new Exception("Modifiers failed to execute."));
                setGlobalCallback(result);
                return true.AsTask();
            }
        }

        public async Task<TResult> UpdateAsyncAsync<TData, TResult>(string rowKey, string partitionKey,
            Func<TData, Func<TData, Task>, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default(Func<Task<TResult>>),
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            CloudTable table = default(CloudTable),
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>)) 
        {
            return await await FindByIdAsync(rowKey, partitionKey,
                async (TData currentStorage) =>
                {
                    var resultGlobal = default(TResult);
                    var useResultGlobal = false;
                    var modificationFailure = new UpdateModificationFailure<TResult>()
                    {
                        setGlobalCallback =
                            (v) =>
                            {
                            },
                        onModificationFailures = onModificationFailures,
                    };
                    var resultLocal = await onUpdate.Invoke(currentStorage,
                        async (documentToSave) =>
                        {
                            useResultGlobal = await await UpdateIfNotModifiedAsync(documentToSave,
                                    GetEntity(currentStorage),
                                () =>
                                {
                                    return false.AsTask();
                                },
                                async () =>
                                {
                                    if (onTimeoutAsync.IsDefaultOrNull())
                                        onTimeoutAsync = AzureStorageDriver.GetRetryDelegateContentionAsync<Task<TResult>>();

                                    resultGlobal = await await onTimeoutAsync(
                                        async () => await UpdateAsyncAsync(rowKey, partitionKey, 
                                            onUpdate, 
                                            onNotFound,
                                            onModificationFailures: onModificationFailures,
                                                table:table, onTimeoutAsync:onTimeoutAsync),
                                        (numberOfRetries) => { throw new Exception("Failed to gain atomic access to document after " + numberOfRetries + " attempts"); });
                                    return true;
                                },
                                onModificationFailures: modificationFailure.AsArray(),
                                onTimeout: AzureStorageDriver.GetRetryDelegate(),
                                table:table);
                        });
                    return useResultGlobal ? resultGlobal : resultLocal;
                },
                onNotFound,
                onFailure: default(Func<ExtendedErrorInformationCodes, string, Task<TResult>>),
                table:table,
                onTimeout:AzureStorageDriver.GetRetryDelegate());
        }

        public async Task<TResult> UpdateAsyncAsync<TResult>(string rowKey, string partitionKey,
                Type typeData,
            Func<object, Func<object, Task>, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default(Func<Task<TResult>>),
            string tableName = default,
            CloudTable table = default,
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>))
        {
            table = GetTable();
            CloudTable GetTable()
            {
                if (!table.IsDefaultOrNull())
                    return table;

                if (tableName.HasBlackSpace())
                    return this.TableClient.GetTableReference(tableName);

                //READ AS: return GetTable<TEntity>();
                return (CloudTable)typeof(AzureTableDriverDynamic)
                    .GetMethod("GetTable", BindingFlags.NonPublic | BindingFlags.Instance)
                    .MakeGenericMethod(typeData.AsArray())
                    .Invoke(this, new object[] { });
            }
            return await await FindByIdAsync(rowKey, partitionKey,
                    typeData,
                async (object currentStorage) =>
                {
                    var resultGlobal = default(TResult);
                    var useResultGlobal = false;
                    var resultLocal = await onUpdate.Invoke(currentStorage,
                        async (documentToSave) =>
                        {
                            // READ AS: GetEntity(currentStorage)
                            var entity = typeof(AzureTableDriverDynamic)
                                .GetMethod("GetEntity", BindingFlags.NonPublic | BindingFlags.Static)
                                .MakeGenericMethod(typeData.AsArray())
                                .Invoke(this, currentStorage.AsArray());

                            Func<Task<bool>> success = () => false.AsTask();
                            Func<Task<bool>> documentModified = async () =>
                            {
                                if (onTimeoutAsync.IsDefaultOrNull())
                                    onTimeoutAsync = AzureStorageDriver.GetRetryDelegateContentionAsync<Task<TResult>>();

                                resultGlobal = await await onTimeoutAsync(
                                    async () => await UpdateAsyncAsync(rowKey, partitionKey,
                                            typeData,
                                        onUpdate, onNotFound,
                                            tableName, table, onTimeoutAsync),
                                    (numberOfRetries) => { throw new Exception("Failed to gain atomic access to document after " + numberOfRetries + " attempts"); });
                                return true;
                            };

                            // READ AS:
                            //useResultGlobal = await await UpdateIfNotModifiedAsync(
                            //        documentToSave, entity,
                            //    success,
                            //    documentModified,
                            //    onFailure: null,
                            //    onTimeout: AzureStorageDriver.GetRetryDelegate(),
                            //    table: table);
                            useResultGlobal = await await (Task<Task<bool>>)typeof(AzureTableDriverDynamic)
                                .GetMethod("UpdateIfNotModifiedAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                                .MakeGenericMethod(new Type[] { typeData, typeof(Task<bool>) })
                                .Invoke(this, new object[] { documentToSave, entity, success, documentModified, null,
                                    AzureStorageDriver.GetRetryDelegate(), table });


                        });
                    return useResultGlobal ? resultGlobal : resultLocal;
                },
                onNotFound,
                default,
                table: table,
                onTimeout: AzureStorageDriver.GetRetryDelegate());
        }

        #endregion

        #region Batch

        public IEnumerableAsync<TResult> CreateOrUpdateBatch<TResult>(IEnumerableAsync<ITableEntity> entities,
            Func<ITableEntity, TableResult, TResult> perItemCallback,
            string tableName = default(string),
            AzureStorageDriver.RetryDelegate onTimeout = default(AzureStorageDriver.RetryDelegate),
            EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
        {
            return CreateOrReplaceBatch<ITableEntity, TResult>(entities,
                entity => entity.RowKey,
                entity => entity.PartitionKey,
                perItemCallback,
                tableName: tableName,
                onTimeout: onTimeout,
                diagnostics: diagnostics);
        }

        public IEnumerableAsync<TResult> CreateOrReplaceBatch<TDocument, TResult>(IEnumerableAsync<TDocument> entities,
                Func<TDocument, string> getRowKey,
                Func<TDocument, string> getPartitionKey,
                Func<ITableEntity, TableResult, TResult> perItemCallback,
                string tableName = default(string),
                AzureStorageDriver.RetryDelegate onTimeout = default(AzureStorageDriver.RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
            where TDocument : class, ITableEntity
        {
            return entities
                .Batch()
                .Select(
                    rows =>
                    {
                        return CreateOrReplaceBatch(rows, getRowKey, getPartitionKey, perItemCallback, tableName, onTimeout);
                    })
                .SelectAsyncMany();
        }

        public IEnumerableAsync<TResult> CreateOrUpdateBatch<TResult>(IEnumerable<ITableEntity> entities,
            Func<ITableEntity, TableResult, TResult> perItemCallback,
            string tableName = default(string),
            AzureStorageDriver.RetryDelegate onTimeout = default(AzureStorageDriver.RetryDelegate),
            EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
        {
            return CreateOrReplaceBatch<ITableEntity, TResult>(entities,
                entity => entity.RowKey,
                entity => entity.PartitionKey,
                perItemCallback,
                tableName:tableName,
                onTimeout: onTimeout,
                diagnostics: diagnostics);
        }

        public IEnumerableAsync<TResult> CreateOrReplaceBatch<TDocument, TResult>(IEnumerable<TDocument> entities,
                Func<TDocument, string> getRowKey,
                Func<TDocument, string> getPartitionKey,
                Func<TDocument, TableResult, TResult> perItemCallback,
                string tableName = default(string),
                AzureStorageDriver.RetryDelegate onTimeout = default(AzureStorageDriver.RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
            where TDocument : class, ITableEntity
        {
            var table = tableName.HasBlackSpace() ?
                TableClient.GetTableReference(tableName)
                :
                default(CloudTable);
            return entities
                .Select(
                    row =>
                    {
                        row.RowKey = getRowKey(row);
                        row.PartitionKey = getPartitionKey(row);
                        return row;
                    })
                .GroupBy(row => row.PartitionKey)
                .SelectMany(
                    grp =>
                    {
                        return grp
                            .Split(index => 100)
                            .Select(set => set.ToArray().PairWithKey(grp.Key));
                    })
                .Select(grp => CreateOrReplaceBatchAsync(grp.Key, grp.Value, table:table))
                .AsyncEnumerable()
                .OnComplete(
                    (resultss) =>
                    {
                        if (!resultss.Any())
                            diagnostics.Trace($"saved 0 {typeof(TDocument).Name} documents across 0 partitions.");

                        diagnostics.Trace($"saved {resultss.Sum(results => results.Length)} {typeof(TDocument).Name} documents across {resultss.Length} partitions.");
                    })
                .SelectMany(
                    trs =>
                    {
                        return trs
                            .Select(
                                tableResult =>
                                {
                                    var resultDocument = (tableResult.Result as TDocument);
                                    return perItemCallback(resultDocument, tableResult);
                                });
                    });
        }

        #endregion

        #region DELETE

        [Obsolete("Use string row/parition keys")]
        public Task<TResult> DeleteByIdAsync<TData, TResult>(Guid documentId,
            Func<TResult> success,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure =
                default(Func<ExtendedErrorInformationCodes, string, TResult>),
            AzureStorageDriver.RetryDelegate onTimeout = default(AzureStorageDriver.RetryDelegate))
            where TData : IReferenceable
        {
            var entityRef = documentId.AsRef<TData>();
            var rowKey = entityRef.StorageComputeRowKey();
            var partitionKey = entityRef.StorageComputePartitionKey();
            return DeleteAsync<TData, TResult>(rowKey, partitionKey,
                success,
                onNotFound,
                onFailure,
                onTimeout);
        }

        public async Task<TResult> DeleteAsync<TData, TResult>(string rowKey, string partitionKey,
            Func<TResult> success,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure =
                default(Func<ExtendedErrorInformationCodes, string, TResult>),
            AzureStorageDriver.RetryDelegate onTimeout = default(AzureStorageDriver.RetryDelegate),
            string tableName = default(string))
        {
            return await await FindByIdAsync(rowKey, partitionKey,
                (TData data) =>
                {
                    var entity = GetEntity(data);
                    return DeleteAsync<TData, TResult>(entity,
                        success,
                        onNotFound,
                        onFailure,
                        onTimeout,
                        tableName: tableName);
                },
                onNotFound.AsAsyncFunc(),
                onTimeout: onTimeout,
                tableName: tableName);
        }

        public async Task<TResult> DeleteAsync<TEntity, TResult>(string rowKey, string partitionKey,
            Func<TEntity, Func<Task<IAzureStorageTableEntity<TEntity>>>, Task<TResult>> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default,
            string tableName = default)
        {
            return await await FindByIdAsync(rowKey, partitionKey,
                (TEntity entity) =>
                {
                    return onFound(entity,
                        () =>
                        {
                            var data = GetEntity(entity);
                            return DeleteAsync(data,
                                () => data,
                                () => data,
                                (a, b) => data,
                                onTimeout,
                                tableName: tableName);
                        });
                },
                onNotFound.AsAsyncFunc(),
                onFailure: onFailure.AsAsyncFunc(),
                onTimeout: onTimeout,
                tableName: tableName);
        }

        public async Task<TResult> DeleteAsync<TData, TResult>(ITableEntity entity,
            Func<TResult> success,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default,
            string tableName = default)
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TData>();

            if (default(CloudTable) == table)
                return onNotFound();

            return await DeleteAsync(entity, table,
                success,
                onNotFound,
                onFailure,
                onTimeout);
        }

        public async Task<TResult> DeleteAsync<TResult>(string rowKey, string partitionKey, Type typeData,
            Func<ITableEntity, object, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default,
            string tableName = default)
        {
            return await await FindByIdAsync(rowKey, partitionKey, typeData,
                (data) =>
                {
                    // ITableEntity entity = GetEntity(data);
                    var getEntityMethod = typeof(AzureTableDriverDynamic)
                        .GetMethod("GetEntity", BindingFlags.Static | BindingFlags.NonPublic);
                    var getEntityTyped = getEntityMethod.MakeGenericMethod(typeData);
                    var entity = (ITableEntity)getEntityTyped.Invoke(null, data.AsArray());
                    var table = tableName.HasBlackSpace() ?
                        this.TableClient.GetTableReference(tableName)
                        :
                        TableFromEntity(typeData, this.TableClient);
                    return DeleteAsync(entity, table,
                        () => onFound(entity, data),
                        () => onNotFound(),
                        onFailure:onFailure,
                        onTimeout:onTimeout);
                },
                onNotFound.AsAsyncFunc(),
                onFailure: onFailure.AsAsyncFunc(),
                onTimeout: onTimeout,
                tableName: tableName);
        }

        public async Task<TResult> DeleteAsync<TData, TResult>(IAzureStorageTableEntity<TData> entity,
            Func<TResult> success,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default,
            string tableName = default)
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TData>();

            if (default(CloudTable) == table)
                return onNotFound();

            var rollback = await entity.ExecuteDeleteModifiersAsync(this,
                rb => rb,
                (modifiers) => throw new Exception("Modifiers failed to execute on delete."));
            var delete = TableOperation.Delete(entity);
            try
            {
               var response = await table.ExecuteAsync(delete);
                if (response.HttpStatusCode == (int)HttpStatusCode.NotFound)
                    return onNotFound();
                return success();
            }
            catch (StorageException se)
            {
                await rollback();
                return await se.ParseStorageException(
                    async (errorCode, errorMessage) =>
                    {
                        switch (errorCode)
                        {
                            case ExtendedErrorInformationCodes.Timeout:
                                {
                                    var timeoutResult = default(TResult);
                                    if (default(AzureStorageDriver.RetryDelegate) == onTimeout)
                                        onTimeout = AzureStorageDriver.GetRetryDelegate();
                                    await onTimeout(se.RequestInformation.HttpStatusCode, se,
                                        async () =>
                                        {
                                            timeoutResult = await DeleteAsync<TData, TResult>(entity, success, onNotFound, onFailure, onTimeout);
                                        });
                                    return timeoutResult;
                                }
                            case ExtendedErrorInformationCodes.TableNotFound:
                            case ExtendedErrorInformationCodes.TableBeingDeleted:
                                {
                                    return onNotFound();
                                }
                            default:
                                {
                                    if (se.IsProblemDoesNotExist())
                                        return onNotFound();
                                    if (onFailure.IsDefaultOrNull())
                                        throw se;
                                    return onFailure(errorCode, errorMessage);
                                }
                        }
                    },
                    () =>
                    {
                        throw se;
                    });
            }
        }


        public IEnumerableAsync<TResult> DeleteBatch<TData, TResult>(IEnumerableAsync<Guid> documentIds,
            Func<TableResult, TResult> result,
            AzureStorageDriver.RetryDelegate onTimeout = default)
            where TData : IReferenceable
        {
            return documentIds
                .Select(subsetId => DeletableEntity<TData>.Delete(subsetId))
                .Batch()
                .Select(
                    docs =>
                    {
                        return docs
                            .GroupBy(doc => doc.PartitionKey)
                            .Select(
                                async partitionDocsGrp =>
                                {
                                    var results = await this.DeleteBatchAsync<TData>(partitionDocsGrp.Key, partitionDocsGrp.ToArray());
                                    return results.Select(tr => result(tr));
                                })
                            .AsyncEnumerable()
                            .SelectMany();
                    })
                .SelectAsyncMany();
        }

        #endregion

        #region Locking

        public delegate Task<TResult> WhileLockedDelegateAsync<TDocument, TResult>(TDocument document,
            Func<Func<TDocument, Func<TDocument, Task>, Task>, Task> unlockAndSave,
            Func<Task> unlock);

        public delegate Task<TResult> ConditionForLockingDelegateAsync<TDocument, TResult>(TDocument document,
            Func<Task<TResult>> continueLocking);
        public delegate Task<TResult> ContinueAquiringLockDelegateAsync<TDocument, TResult>(int retryAttempts, TimeSpan elapsedTime,
                TDocument document,
            Func<Task<TResult>> continueAquiring,
            Func<Task<TResult>> force = default(Func<Task<TResult>>));

        public Task<TResult> LockedUpdateAsync<TDocument, TResult>(string rowKey, string partitionKey,
                Expression<Func<TDocument, DateTime?>> lockedPropertyExpression,
            WhileLockedDelegateAsync<TDocument, TResult> onLockAquired,
            Func<TResult> onNotFound,
            Func<TResult> onLockRejected = default(Func<TResult>),
                ContinueAquiringLockDelegateAsync<TDocument, TResult> onAlreadyLocked =
                        default(ContinueAquiringLockDelegateAsync<TDocument, TResult>),
                    ConditionForLockingDelegateAsync<TDocument, TResult> shouldLock =
                        default(ConditionForLockingDelegateAsync<TDocument, TResult>),
                AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeout = default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>),
                Func<TDocument, TDocument> mutateUponLock = default(Func<TDocument, TDocument>))
            where TDocument : IReferenceable => LockedUpdateAsync(rowKey, partitionKey, 
                    lockedPropertyExpression, 0, DateTime.UtcNow,
                onLockAquired,
                onNotFound.AsAsyncFunc(),
                onLockRejected,
                onAlreadyLocked,
                shouldLock,
                onTimeout,
                mutateUponLock);

        public Task<TResult> LockedUpdateAsync<TDocument, TResult>(string rowKey, string partitionKey,
                Expression<Func<TDocument, DateTime?>> lockedPropertyExpression,
            WhileLockedDelegateAsync<TDocument, TResult> onLockAquired,
            Func<Task<TResult>> onNotFound,
            Func<TResult> onLockRejected = default(Func<TResult>),
                ContinueAquiringLockDelegateAsync<TDocument, TResult> onAlreadyLocked =
                        default(ContinueAquiringLockDelegateAsync<TDocument, TResult>),
                    ConditionForLockingDelegateAsync<TDocument, TResult> shouldLock =
                        default(ConditionForLockingDelegateAsync<TDocument, TResult>),
                AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeout = default(AzureStorageDriver.RetryDelegateAsync<Task<TResult>>),
                Func<TDocument, TDocument> mutateUponLock = default(Func<TDocument, TDocument>))
            where TDocument : IReferenceable => LockedUpdateAsync(
                    rowKey, partitionKey,
                    lockedPropertyExpression, 0, DateTime.UtcNow,
                onLockAquired,
                onNotFound,
                onLockRejected,
                onAlreadyLocked,
                shouldLock,
                onTimeout,
                mutateUponLock);

        private async Task<TResult> LockedUpdateAsync<TDocument, TResult>(string rowKey, string partitionKey,
                Expression<Func<TDocument, DateTime?>> lockedPropertyExpression,
                int retryCount,
                DateTime initialPass,
            WhileLockedDelegateAsync<TDocument, TResult> onLockAquired,
            Func<Task<TResult>> onNotFoundAsync,
            Func<TResult> onLockRejected = default,
            ContinueAquiringLockDelegateAsync<TDocument, TResult> onAlreadyLocked = default,
            ConditionForLockingDelegateAsync<TDocument, TResult> shouldLock = default,
            AzureStorageDriver.RetryDelegateAsync<Task<TResult>> onTimeout = default,
            Func<TDocument, TDocument> mutateUponLock = default)
            where TDocument : IReferenceable
        {
            if (onTimeout.IsDefaultOrNull())
                onTimeout = AzureStorageDriver.GetRetryDelegateContentionAsync<Task<TResult>>();

            if (onAlreadyLocked.IsDefaultOrNull())
                onAlreadyLocked = (retryCountDiscard, initialPassDiscard, doc, continueAquiring, force) => continueAquiring();

            if (onLockRejected.IsDefaultOrNull())
                if (!shouldLock.IsDefaultOrNull())
                    throw new ArgumentNullException("onLockRejected", "onLockRejected must be specified if shouldLock is specified");

            if (shouldLock.IsDefaultOrNull())
            {
                // both values 
                shouldLock = (doc, continueLocking) => continueLocking();
                onLockRejected = () => throw new Exception("shouldLock failed to continueLocking");
            }

            #region lock property expressions for easy use later

            var lockedPropertyMember = ((MemberExpression)lockedPropertyExpression.Body).Member;
            var fieldInfo = lockedPropertyMember as FieldInfo;
            var propertyInfo = lockedPropertyMember as PropertyInfo;

            bool isDocumentLocked(TDocument document)
            {
                var lockValueObj = fieldInfo != null ?
                    fieldInfo.GetValue(document)
                    :
                    propertyInfo.GetValue(document);
                var lockValue = (DateTime?)lockValueObj;
                var documentLocked = lockValue.HasValue;
                return documentLocked;
            }
            void lockDocument(TDocument document)
            {
                if (fieldInfo != null)
                    fieldInfo.SetValue(document, DateTime.UtcNow);
                else
                    propertyInfo.SetValue(document, DateTime.UtcNow);
            }
            void unlockDocument(TDocument documentLocked)
            {
                if (fieldInfo != null)
                    fieldInfo.SetValue(documentLocked, default(DateTime?));
                else
                    propertyInfo.SetValue(documentLocked, default(DateTime?));
            }

            // retryIncrease because some retries don't count
            Task<TResult> retry(int retryIncrease) => LockedUpdateAsync(rowKey, partitionKey,
                    lockedPropertyExpression, retryCount + retryIncrease, initialPass,
                onLockAquired,
                onNotFoundAsync,
                onLockRejected,
                onAlreadyLocked,
                    shouldLock,
                    onTimeout);

            #endregion

            return await await this.FindByIdAsync(rowKey, partitionKey,
                async (TDocument document) =>
                {
                    var originalDoc = GetEntity(document); // Not a deep, or even shallow, copy in most cases
                    async Task<TResult> execute()
                    {
                        if (!mutateUponLock.IsDefaultOrNull())
                            document = mutateUponLock(document);
                        // Save document in locked state
                        return await await this.UpdateIfNotModifiedAsync(document,
                                originalDoc, // should not be triggering modifers here anyway
                            () => PerformLockedCallback(rowKey, partitionKey, document, unlockDocument, onLockAquired),
                            () => retry(0));
                    }

                    return await shouldLock(document,
                        () =>
                        {
                            #region Set document to locked state if not already locked

                            var documentLocked = isDocumentLocked(document);
                            if (documentLocked)
                            {
                                return onAlreadyLocked(retryCount,
                                        DateTime.UtcNow - initialPass, document,
                                    () => retry(1),
                                    () => execute());
                            }
                            lockDocument(document);

                            #endregion

                            return execute();
                        });
                },
                onNotFoundAsync);
            // TODO: onTimeout:onTimeout);
        }

        private async Task<TResult> PerformLockedCallback<TDocument, TResult>(
            string rowKey, string partitionKey,
            TDocument documentLocked,
            Action<TDocument> unlockDocument,
            WhileLockedDelegateAsync<TDocument, TResult> success)
            where TDocument : IReferenceable
        {
            try
            {
                var result = await success(documentLocked,
                    async (update) =>
                    {
                        var exists = await UpdateAsync<TDocument, bool>(rowKey, partitionKey,
                            async (entityLocked, save) =>
                            {
                                await update(entityLocked,
                                    async (entityMutated) =>
                                    {
                                        unlockDocument(entityMutated);
                                        await save(entityMutated);
                                    });
                                return true;
                            },
                            () => false);
                    },
                    async () =>
                    {
                        var exists = await UpdateAsync<TDocument, bool>(rowKey, partitionKey,
                            async (entityLocked, save) =>
                            {
                                unlockDocument(entityLocked);
                                await save(entityLocked);
                                return true;
                            },
                            () => false);
                    });
                return result;
            }
            catch (Exception)
            {
                var exists = await UpdateAsync<TDocument, bool>(rowKey, partitionKey,
                    async (entityLocked, save) =>
                    {
                        unlockDocument(entityLocked);
                        await save(entityLocked);
                        return true;
                    },
                    () => false);
                throw;
            }
        }

        #endregion

        #region BLOB

        public async Task<TResult> BlobCreateAsync<TResult>(byte[] content, Guid blobId, string containerName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var container = this.BlobClient.GetContainerReference(containerName);
            container.CreateIfNotExists();
            var blockBlob = container.GetBlockBlobReference(blobId.ToString("N"));
            try
            {
                if (await blockBlob.ExistsAsync())
                {
                    if (onAlreadyExists.IsDefault())
                        throw new RecordAlreadyExistsException();
                    return onAlreadyExists();
                }
                if (contentType.HasBlackSpace())
                    blockBlob.Properties.ContentType = contentType;
                using (var stream = await blockBlob.OpenWriteAsync())
                {
                    await stream.WriteAsync(content, 0, content.Length);
                }
                return onSuccess();
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException ex)
            {
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseStorageException(
                    (errorCode, errorMessage) =>
                        onFailure(errorCode, errorMessage),
                    () => throw ex);
            }
        }

        public async Task<TResult> BlobCreateAsync<TResult>(Stream content, Guid blobId, string containerName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var container = this.BlobClient.GetContainerReference(containerName);
            container.CreateIfNotExists();
            var blockBlob = container.GetBlockBlobReference(blobId.ToString("N"));
            try
            {
                if (await blockBlob.ExistsAsync())
                {
                    if (onAlreadyExists.IsDefault())
                        throw new RecordAlreadyExistsException();
                    return onAlreadyExists();
                }
                if (contentType.HasBlackSpace())
                    blockBlob.Properties.ContentType = contentType;
                using (var stream = await blockBlob.OpenWriteAsync())
                {
                    await content.CopyToAsync(stream);
                }
                return onSuccess();
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException ex)
            {
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseStorageException(
                    (errorCode, errorMessage) =>
                        onFailure(errorCode, errorMessage),
                    () => throw ex);
            }
        }

        public async Task<TResult> BlobLoadBytesAsync<TResult>(Guid blobId, string containerName,
            Func<byte[], string, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var container = this.BlobClient.GetContainerReference(containerName);
            try
            {
                var blockBlob = container.GetBlockBlobReference(blobId.ToString("N"));
                using (var stream = await blockBlob.OpenReadAsync())
                {
                    var content = stream.ToBytes();
                    var contentType = blockBlob.Properties.ContentType;
                    return onFound(content, contentType);
                }
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException ex)
            {
                if (ex.IsProblemDoesNotExist())
                    if(!onNotFound.IsDefaultOrNull())
                        return onNotFound();
                if(onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseExtendedErrorInformation(
                    (code, msg) => onFailure(code, msg),
                    () => throw ex);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="blobId"></param>
        /// <param name="containerName"></param>
        /// <param name="onFound">Stream is NOT disposed.</param>
        /// <param name="onNotFound"></param>
        /// <param name="onFailure"></param>
        /// <param name="onTimeout"></param>
        /// <returns></returns>
        public async Task<TResult> BlobLoadStreamAsync<TResult>(Guid blobId, string containerName,
            Func<System.IO.Stream, string, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = default)
        {
            var container = this.BlobClient.GetContainerReference(containerName);
            try
            {
                var blockBlob = container.GetBlockBlobReference(blobId.ToString("N"));
                var contentType = blockBlob.Properties.ContentType;
                var stream = await blockBlob.OpenReadAsync();
                return onFound(stream, contentType);
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException ex)
            {
                if (ex.IsProblemDoesNotExist())
                    if (!onNotFound.IsDefaultOrNull())
                        return onNotFound();
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseExtendedErrorInformation(
                    (code, msg) => onFailure(code, msg),
                    () => throw ex);
            }
        }

        #endregion

    }
}


using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Newtonsoft.Json;

using EastFive;
using EastFive.Api;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using EastFive.Analytics;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Serialization;
using Azure.Storage.Blobs.Specialized;
using EastFive.Configuration;
using System.Net.Mime;

namespace EastFive.Persistence.Azure.StorageTables.Driver
{
    public class AzureTableDriverDynamic
    {
        public const int DefaultNumberOfTimesToRetry = 10;
        protected static readonly TimeSpan DefaultBackoffForRetry = TimeSpan.FromSeconds(4);

        private static object tableClientsLock = new object();
        private static IDictionary<string, CloudTableClient> tableClients = new Dictionary<string, CloudTableClient>();

        public readonly CloudTableClient TableClient;
        public readonly BlobServiceClient BlobClient;
        public readonly global::Azure.Storage.StorageSharedKeyCredential StorageSharedKeyCredential;

        public delegate Task RetryDelegate(int statusCode, Exception ex, Func<Task> retry);
        public delegate Task<TResult> RetryDelegateAsync<TResult>(
            Func<TResult> retry,
            Func<int, TResult> timeout);

        #region Utility methods

        internal static RetryDelegate GetRetryDelegate()
        {
            var retriesAttempted = 0;
            var retryDelay = TimeSpan.FromSeconds(1.0);
            return async (statusCode, ex, retry) =>
            {
                TimeSpan retryDelayInner = retryDelay;
                retryDelay = TimeSpan.FromSeconds(retryDelay.TotalSeconds * retryDelay.TotalSeconds);
                bool shouldRetry = retriesAttempted < 4;
                retriesAttempted++;
                if (!shouldRetry)
                    throw new Exception("After " + retriesAttempted + "attempts finding the resource timed out");
                await Task.Delay(retryDelay);
                await retry();
            };
        }

        public static RetryDelegateAsync<TResult> GetRetryDelegateContentionAsync<TResult>(
            int maxRetries = 100)
        {
#pragma warning disable SCS0005 // Weak random number generator
            var retriesAttempted = 0;
            var lastFail = default(long);
            var rand = new Random();
            return
                async (retry, timeout) =>
                {
                    bool shouldRetry = retriesAttempted <= maxRetries;
                    if (!shouldRetry)
                        return timeout(retriesAttempted);
                    var failspan = (retriesAttempted > 0) ?
                        DateTime.UtcNow.Ticks - lastFail :
                        0;
                    lastFail = DateTime.UtcNow.Ticks;

                    retriesAttempted++;
                    var bobble = rand.NextDouble() * 2.0;
                    var retryDelay = TimeSpan.FromTicks((long)(failspan * bobble));
                    await Task.Delay(retryDelay);
                    return retry();
                };
#pragma warning restore SCS0005 // Weak random number generator
        }

        protected static RetryDelegateAsync<TResult> GetRetryDelegateCollisionAsync<TResult>(
            TimeSpan delay = default(TimeSpan),
            TimeSpan limit = default(TimeSpan),
            int maxRetries = 10)
        {
#pragma warning disable SCS0005 // Weak random number generator
            if (default(TimeSpan) == delay)
                delay = TimeSpan.FromSeconds(0.5);

            if (default(TimeSpan) == delay)
                limit = TimeSpan.FromSeconds(60.0);

            var retriesAttempted = 0;
            var rand = new Random();
            long delayFactor = 1;
            return
                async (retry, timeout) =>
                {
                    bool shouldRetry = retriesAttempted <= maxRetries;
                    if (!shouldRetry)
                        return timeout(retriesAttempted);
                    retriesAttempted++;
                    var bobble = rand.NextDouble() * 2.0;
                    var delayMultiplier = ((double)(delayFactor >> 1)) + ((double)delayFactor * bobble);
                    var retryDelay = TimeSpan.FromTicks((long)(delay.Ticks * delayMultiplier));
                    delayFactor = delayFactor << 1;
                    delay = TimeSpan.FromSeconds(delay.TotalSeconds + (retriesAttempted * delay.TotalSeconds));
                    await Task.Delay(retryDelay);
                    return retry();
                };
#pragma warning restore SCS0005 // Weak random number generator
        }

        #endregion

        #region Init / Setup / Utility

        public AzureTableDriverDynamic(CloudStorageAccount storageAccount, string connectionString)
        {
            TableClient = storageAccount.CreateCloudTableClient();
            TableClient.DefaultRequestOptions.RetryPolicy =
                new ExponentialRetry(DefaultBackoffForRetry, DefaultNumberOfTimesToRetry);

            var options = new BlobClientOptions();
            options.Retry.NetworkTimeout = TimeSpan.FromMinutes(10);
            BlobClient = new BlobServiceClient(connectionString, options);
            
            StorageSharedKeyCredential = GetCredentialFromConnectionString(connectionString);
        }

        static global::Azure.Storage.StorageSharedKeyCredential GetCredentialFromConnectionString(string connectionString)
        {
            // This code was adapted from a Microsoft Tutorial so some of the syle does not match ours

            const string accountNameLabel = "AccountName";
            const string accountKeyLabel = "AccountKey";
            const string errorMessage = "The connection string must have an AccountName and AccountKey or UseDevelopmentStorage=true";

            try
            {
                var connectionStringValues = connectionString
                    .Split(';')
                    .Select(s => s.Split('='.AsArray(), 2))
                    .ToDictionary(s => s[0], s => s[1]);

                string accountName;
                string accountKey;
                if (connectionStringValues.TryGetValue(accountNameLabel, out var accountNameValue)
                        && accountNameValue.HasBlackSpace()
                        && connectionStringValues.TryGetValue(accountKeyLabel, out var accountKeyValue)
                        && accountKeyValue.HasBlackSpace())
                {
                    accountName = accountNameValue;
                    accountKey = accountKeyValue;
                }
                else
                {
                    throw new ArgumentException(errorMessage);
                }

                return new global::Azure.Storage.StorageSharedKeyCredential(accountName, accountKey);
            }
            catch (IndexOutOfRangeException)
            {
                throw new ArgumentException(errorMessage);
            }
        }

        public static AzureTableDriverDynamic FromSettings(string settingKey = default)
        {
            if (settingKey.IsDefaultOrNull())
                settingKey = EastFive.Azure.AppSettings.Persistence.StorageTables.ConnectionString.Key;
            return EastFive.Web.Configuration.Settings.GetString(settingKey,
                (connectionString) => FromStorageString(connectionString),
                (why) => throw new Exception(why));
        }

        public static AzureTableDriverDynamic FromSettings(ConnectionString settingKey)
        {
            return settingKey.ConfigurationString(
                (connectionString) => FromStorageString(connectionString),
                (why) => throw new Exception(why));
        }

        public static AzureTableDriverDynamic FromStorageString(string connectionString)
        {
            var cloudStorageAccount = CloudStorageAccount.Parse(connectionString);
            var azureStorageRepository = new AzureTableDriverDynamic(cloudStorageAccount, connectionString);
            return azureStorageRepository;
        }

        private CloudTable GetTable<TEntity>()
        {
            var tableType = typeof(TEntity);
            return TableFromEntity(tableType, this.TableClient);
        }

        private E5CloudTable GetE5Table<TEntity>()
        {
            var cloudTable = GetTable<TEntity>();
            return new E5CloudTable(cloudTable);
        }

        private static CloudTable TableFromEntity(Type tableType, CloudTableClient tableClient)
        {
            return tableType.GetAttributesInterface<IProvideTable>(inherit:true)
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

        public static IAzureStorageTableEntity<TEntity> GetEntity<TEntity>(TEntity entity)
        {
            if(!typeof(TEntity).TryGetAttributeInterface<IProvideEntity>(out var attributeInterface, inherit:true))
                throw new Exception($"No attribute of type {nameof(IProvideEntity)} on {typeof(TEntity).FullName}.");
            return attributeInterface.GetEntity(entity);
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
                (deletableEntity.rowKeyValue, deletableEntity.partitionKeyValue) = entityRef.StorageComputeRowAndPartitionKey();
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
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (table.IsDefaultOrNull())
                table = tableName.HasBlackSpace() ?
                    this.TableClient.GetTableReference(tableName)
                    :
                    GetTable<TEntity>();

            var tableQuery = TableQueryExtensions.GetTableQuery<TEntity>();
            var tableEntityTypes = tableQuery.GetType().GetGenericArguments();
            var findAllIntermediate = typeof(AzureTableDriverDynamic)
                .GetMethod(nameof(AzureTableDriverDynamic.FindAllInternal), BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(tableEntityTypes)
                .Invoke(null, new object[] { tableQuery, table, numberOfTimesToRetry, cancellationToken });

            var findAllCasted = findAllIntermediate as IEnumerableAsync<IWrapTableEntity<TEntity>>;

            var propertiesLookup = typeof(TEntity)
                .StorageProperties()
                .Select(
                    propInfoAttribute =>
                    {
                        var propInfo = propInfoAttribute.Key;
                        var name = propInfo.GetTablePropertyName();
                        var memberType = propInfo.GetMemberType();
                        return name.PairWithValue(memberType);
                    })
                .ToDictionary();

            var tableInformationPartitions = await findAllCasted
                .AggregateAsync(
                    new TableInformation()
                    {
                        partitions = new Dictionary<string, PartitionSummary>(),
                        properties = new Dictionary<string, IDictionary<object, long>>(),
                    },
                    (tableInformation, resource) =>
                    {
                        try
                        {
                            if (resource.RawRowKey != resource.RowKey)
                                tableInformation.mismatchedRowKeys++;
                        } catch (Exception)
                        {
                            tableInformation.mismatchedRowKeys++;
                            return tableInformation;
                        }
                        var partitionKey = default(string);
                        try
                        {
                            partitionKey = resource.RawPartitionKey;
                            if (partitionKey != resource.PartitionKey)
                                tableInformation.mismatchedPartitionKeys++;
                        }
                        catch (Exception)
                        {
                            tableInformation.mismatchedPartitionKeys++;
                            return tableInformation;
                        }
                        tableInformation.partitions = tableInformation.partitions.AddIfMissing(partitionKey,
                            (addValue) =>
                            {
                                var partitionSummary = new PartitionSummary
                                {
                                    total = 0,
                                    properties = new Dictionary<string, IDictionary<object, long>>(),
                                };
                                return addValue(partitionSummary);
                            },
                            (summary, dict, wasAdded) =>
                            {
                                summary.total = summary.total + 1;
                                summary.properties = resource.RawProperties.Aggregate(
                                    summary.properties,
                                    (summaryPropertiesCurrent, rawProperty) =>
                                    {
                                        return summaryPropertiesCurrent.AddIfMissing(rawProperty.Key,
                                            (addSummaryProp) =>
                                            {
                                                return addSummaryProp(new Dictionary<object, long>());
                                            },
                                            (summaryProp, summaryPropertiesCurrentNext, didAddSummaryProp) =>
                                            {
                                                if (!propertiesLookup.ContainsKey(rawProperty.Key))
                                                    return summaryPropertiesCurrentNext;
                                                var propType = propertiesLookup[rawProperty.Key];
                                                return rawProperty.Value.Bind(propType,
                                                    propValue =>
                                                    {
                                                        return summaryProp.AddIfMissing(propValue,
                                                            (addSummaryPropValue) => addSummaryPropValue(0),
                                                            (summaryPropValue, summaryPropNext, didAddSummaryPropValue) =>
                                                            {
                                                                summaryPropNext[propValue] = summaryPropValue + 1;
                                                                return summaryPropertiesCurrentNext;
                                                            });
                                                    },
                                                    () => summaryPropertiesCurrentNext);
                                            });
                                    });
                                return dict;
                            });
                        tableInformation.properties = resource.RawProperties.Aggregate(
                            tableInformation.properties,
                            (summaryPropertiesCurrent, rawProperty) =>
                            {
                                return summaryPropertiesCurrent.AddIfMissing(rawProperty.Key,
                                    (addSummaryProp) =>
                                    {
                                        return addSummaryProp(new Dictionary<object, long>());
                                    },
                                    (summaryProp, summaryPropertiesCurrentNext, didAddSummaryProp) =>
                                    {
                                        if (!propertiesLookup.ContainsKey(rawProperty.Key))
                                            return summaryPropertiesCurrentNext;
                                        var propType = propertiesLookup[rawProperty.Key];
                                        return rawProperty.Value.Bind(propType,
                                            propValue =>
                                            {
                                                return summaryProp.AddIfMissing(propValue,
                                                    (addSummaryPropValue) => addSummaryPropValue(0),
                                                    (summaryPropValue, summaryPropNext, didAddSummaryPropValue) =>
                                                    {
                                                        summaryPropNext[propValue] = summaryPropValue + 1;
                                                        return summaryPropertiesCurrentNext;
                                                    });
                                            },
                                            () => summaryPropertiesCurrentNext);
                                    });
                            });
                        return tableInformation;
                    });
            tableInformationPartitions.total = tableInformationPartitions.partitions
                .Select(partition => partition.Value.total)
                .Sum();
            return tableInformationPartitions;
        }

        #endregion

        #region Core

        #region Find

        public Task<TResult> FindByIdAsync<TEntity, TResult>(
                string rowKey, string partitionKey,
            Func<TEntity, TableResult, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure =
                default(Func<ExtendedErrorInformationCodes, string, TResult>),
            CloudTable table = default(CloudTable),
            string tableName = default(string),
            RetryDelegate onTimeout =
                default(RetryDelegate),
            ICacheEntites cache = default,
            IProvideEntity entityProvider = default)
        {
            return cache.ByRowPartitionKeyEx(rowKey, partitionKey,
                (TEntity entity) => onFound(entity, default(TableResult)).AsTask(),
                async (updateCache) =>
                {
                    var operation = TableOperation.Retrieve(partitionKey, rowKey,
                        (string partitionKeyEntity, string rowKeyEntity, DateTimeOffset timestamp, IDictionary<string, EntityProperty> properties, string etag) =>
                        {
                            return GetEntityProvider(
                                entityProvider =>
                                {
                                    return entityProvider.CreateEntityInstance<TEntity>(rowKeyEntity, partitionKeyEntity,
                                        properties, etag, timestamp);
                                },
                                () =>
                                {
                                    var entityPopulated = TableEntity<TEntity>.CreateEntityInstance(properties);
                                    return entityPopulated;
                                });

                            TEntity GetEntityProvider(
                                Func<IProvideEntity, TEntity> onFound,
                                Func<TEntity> onNone)
                            {
                                if (entityProvider.IsNotDefaultOrNull())
                                    return onFound(entityProvider);

                                if(typeof(TEntity).TryGetAttributeInterface<IProvideEntity>(out var newEntityProvider, inherit: true))
                                    return onFound(newEntityProvider);
                                return onNone();
                            }
                        });
                    if (table.IsDefaultOrNull())
                        table = tableName.HasBlackSpace() ?
                            this.TableClient.GetTableReference(tableName)
                            :
                            table = GetTable<TEntity>();
                    try
                    {
                        var result = await new E5CloudTable(table).ExecuteAsync(operation);
                        if (404 == result.HttpStatusCode)
                            return onNotFound();
                        var entity = (TEntity)result.Result;
                        updateCache(entity);
                        return onFound(entity, result);
                    }
                    catch (StorageException se)
                    {
                        if (se.IsProblemTableDoesNotExist())
                            return onNotFound();
                        if (se.IsProblemTimeout())
                        {
                            TResult result = default(TResult);
                            if (default(RetryDelegate) == onTimeout)
                                onTimeout = GetRetryDelegate();
                            await onTimeout(se.RequestInformation.HttpStatusCode, se,
                                async () =>
                                {
                                    result = await FindByIdAsync(rowKey, partitionKey,
                                        onFound, onNotFound, onFailure,
                                            table: table, onTimeout: onTimeout);
                                });
                            return result;
                        }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ex.GetType();
                        throw;
                    }
                });
        }

        public async Task<TResult> FindByIdAsync<TResult>(
                string rowKey, string partitionKey,
                Type typeData,
            Func<object, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            CloudTable table = default,
            string tableName = default,
            RetryDelegate onTimeout = default)
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
                                    .GetMethod(nameof(IProvideEntity.CreateEntityInstance), BindingFlags.Instance | BindingFlags.Public)
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

                return TableFromEntity(typeData, this.TableClient);
            }
            try
            {
                var result = await new E5CloudTable(table).ExecuteAsync(operation);
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
                    if (default(RetryDelegate) == onTimeout)
                        onTimeout = GetRetryDelegate();
                    await onTimeout(se.RequestInformation.HttpStatusCode, se,
                        async () =>
                        {
                            result = await FindByIdAsync(rowKey, partitionKey, typeData,
                                onSuccess, onNotFound, onFailure,
                                    table: table, onTimeout: onTimeout);
                        });
                    return result;
                }
                throw;
            }
            catch (Exception ex)
            {
                ex.GetType();
                throw;
            }
        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRef<TRefEntity>>> by,
                int readAhead = -1)
            // where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef, by, readAhead:readAhead);
        }

        public IEnumerableAsync<TEntity> FindBy<TProperty, TEntity>(TProperty propertyValue,
                Expression<Func<TEntity, TProperty>> propertyExpr,
                Expression<Func<TEntity, bool>> query1 = default,
                Expression<Func<TEntity, bool>> query2 = default,
                int readAhead = -1,
                ILogger logger = default)
            //where TEntity : IReferenceable
        {
            return FindByInternal(propertyValue, propertyExpr,
                logger: logger, readAhead: readAhead,
                query1, query2);
        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRef<IReferenceable>>> by,
                int readAhead = -1)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef, by, readAhead:readAhead);
        }

        public IEnumerableAsync<TEntity> FindBy<TEntity>(Guid entityId,
                Expression<Func<TEntity, Guid>> by,
                Expression<Func<TEntity, bool>> query1 = default,
                Expression<Func<TEntity, bool>> query2 = default,
                int readAhead = -1,
                ILogger logger = default)
            where TEntity : IReferenceable
        {
            return FindByInternal(entityId, by,
                logger: logger, readAhead: readAhead,
                query1, query2);
        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRefOptional<TRefEntity>>> by,
                int readAhead = -1)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef.Optional(), by, readAhead: readAhead);
        }

        public IEnumerableAsync<TEntity> FindBy<TRefEntity, TEntity>(IRef<TRefEntity> entityRef,
                Expression<Func<TEntity, IRefs<TRefEntity>>> by,
                int readAhead = -1)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return FindByInternal(entityRef, by, readAhead: readAhead);
        }

        private IEnumerableAsync<TEntity> FindByInternal<TMatch, TEntity>(object findByValue,
                Expression<Func<TEntity, TMatch>> by,
                ILogger logger = default,
                int readAhead = -1,
                params Expression<Func<TEntity, bool>>[] queries)
            // where TEntity : IReferenceable
        {
            return by.MemberInfo(
                (memberCandidate, expr) =>
                {
                    var memberAssignments = queries
                        .Where(query => !query.IsDefaultOrNull())
                        .Select(
                            query =>
                            {
                                var memberInfo = (query).MemberComparison(out ExpressionType operand, out object value);
                                return memberInfo.PairWithValue(value);
                            })
                        .Append(findByValue.PairWithKey(memberCandidate))
                        .ToArray();

                    return memberCandidate
                        .GetAttributesInterface<IProvideFindBy>()
                        .First<IProvideFindBy, IEnumerableAsync<TEntity>>(
                            (attr, next) =>
                            {
                                return attr.GetKeys(memberCandidate, this, memberAssignments,
                                    (keys) =>
                                    {
                                        return keys.Select(
                                            async rowParitionKeyKvp =>
                                            {
                                                var rowKey = rowParitionKeyKvp.RowKey;
                                                var partitionKey = rowParitionKeyKvp.PartitionKey;
                                                var kvp = await this.FindByIdAsync(rowKey, partitionKey,
                                                        (TEntity entity, TableResult eTag) => entity.PairWithKey(true),
                                                        () => default(TEntity).PairWithKey(false),
                                                        onFailure: (code, msg) => default(TEntity).PairWithKey(false));
                                                if (kvp.Key)
                                                    logger.Trace($"Lookup {partitionKey}/{rowKey} was found.");
                                                else
                                                    logger.Trace($"Lookup {partitionKey}/{rowKey} failed.");

                                                return kvp;
                                            })
                                        .Await(readAhead: readAhead)
                                        .Where(kvp => kvp.Key)
                                        .SelectValues();
                                    },
                                    () =>
                                    {
                                        return next();
                                    },
                                    logger: logger);
                            },
                            () =>
                            {
                                var table = GetTable<TEntity>();

                                if (memberCandidate.TryGetAttributeInterface(out IProvideTableQuery provideTableQuery))
                                {
                                    var propExpr = Expression.Equal(by.Body, Expression.Constant(findByValue));

                                    var lambdaExpr = Expression<Func<TEntity, bool>>.Lambda(propExpr, parameters:by.Parameters);
                                    var initialAssignment = (Expression<Func<TEntity, bool>>)lambdaExpr;

                                    var filter = queries
                                        .Where(query => !query.IsDefaultOrNull())
                                        .Aggregate(
                                            initialAssignment,
                                            (current, next) =>
                                            {
                                                var combined = Expression.AndAlso(current.Body, next.Body);

                                                var combinedlambdaExpr = Expression<Func<TEntity, bool>>.Lambda(combined, parameters: by.Parameters);
                                                var combinedCast = (Expression<Func<TEntity, bool>>)combinedlambdaExpr;

                                                return combinedCast;
                                            });

                                    var whereFilter = provideTableQuery.ProvideTableQuery(memberCandidate,
                                        filter: filter, out Func<TEntity, bool> postFilter);

                                    return RunQuery<TEntity>(whereFilter, table)
                                        .Where(item => postFilter(item));
                                }

                                if (memberCandidate.TryGetAttributeInterface(
                                    out IComputeAzureStorageTablePartitionKey computeAzureStorageTablePartitionKey))
                                {
                                    var partitionValue = computeAzureStorageTablePartitionKey.ComputePartitionKey(findByValue, memberCandidate,
                                        String.Empty, memberAssignments);

                                    var whereFilter = $"PartitionKey=`{partitionValue}`";
                                    return RunQuery<TEntity>(whereFilter, table);
                                }

                                var assignmentsNames = memberAssignments.Select(kvp => kvp.Key.Identification()).Join(",");
                                var line2 = $" or {assignmentsNames} did not match any {nameof(IProvideFindBy)} queries.";
                                throw new ArgumentException($"{memberCandidate.Identification()} does not contain an attribute that implements {nameof(IProvideFindBy)}, {nameof(IProvideTableQuery)}, or {nameof(IComputeAzureStorageTablePartitionKey)}{line2}");
                            });
                },
                () => throw new ArgumentException($"Expression {by} is not a parsable member expression"));
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
                                                        var rowKey = rowParitionKeyKvp.RowKey;
                                                        var partitionKey = rowParitionKeyKvp.PartitionKey;
                                                        return this.FindByIdAsync(rowKey, partitionKey,
                                                            (TEntity entity, TableResult eTag) => entity,
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
                () => throw new ArgumentException($"Expression {by} is not a parsable member expression"));
        }

        public IEnumerableAsync<IRefAst> FindIdsBy<TMatch, TEntity>(object findByValue,
                Expression<Func<TEntity, TMatch>> by,
                ILogger logger = default,
                params Expression<Func<TEntity, bool>>[] queries)
            // where TEntity : IReferenceable
        {
            return by.MemberInfo(
                (memberCandidate, expr) =>
                {
                    return memberCandidate
                        .GetAttributesInterface<IProvideFindBy>()
                        .First<IProvideFindBy, IEnumerableAsync<IRefAst>>(
                            (attr, next) =>
                            {
                                var memberAssignments = queries
                                    .Where(query => !query.IsDefaultOrNull())
                                    .Select(
                                        query =>
                                        {
                                            var memberInfo = (query).MemberComparison(out ExpressionType operand, out object value);
                                            return memberInfo.PairWithValue(value);
                                        })
                                    .Append(findByValue.PairWithKey(memberCandidate))
                                    .ToArray();

                                return attr.GetKeys(memberCandidate, this, memberAssignments,
                                    (value) => value,
                                    () => next(),
                                        logger: logger);
                            },
                            () =>
                            {
                                throw new ArgumentException($"{memberCandidate.Identification()} does not contain an attribute that implements {nameof(IProvideFindBy)}.");
                            });
                },
                () => throw new ArgumentException($"Expression {by} is not a parsable member expression"));
        }

        public Task<TResult> FindModifiedByAsync<TMatch, TEntity, TResult>(object findByValue,
                Expression<Func<TEntity, TMatch>> by,
                Expression<Func<TEntity, bool>>[] queries,
            Func<string, DateTime, int, TResult> onEtagLastModifedFound,
            Func<TResult> onNoLookupInfo)
            where TEntity : IReferenceable
        {
            return by.MemberInfo(
                (memberCandidate, expr) =>
                {
                    return memberCandidate
                        .GetAttributesInterface<IProvideFindBy>()
                        .First<IProvideFindBy, Task<TResult>>(
                            (attr, next) =>
                            {
                                var memberAssignments = queries
                                    .Where(query => !query.IsDefaultOrNull())
                                    .Select(
                                        query =>
                                        {
                                            var memberInfo = (query).MemberComparison(out ExpressionType operand, out object value);
                                            return memberInfo.PairWithValue(value);
                                        })
                                    .Append(findByValue.PairWithKey(memberCandidate))
                                    .ToArray();

                                return attr.GetLookupInfoAsync(memberCandidate, this,
                                    memberAssignments,
                                    onEtagLastModifedFound,
                                    onNoLookupInfo);
                            },
                            () =>
                            {
                                throw new ArgumentException($"{memberCandidate.Identification()} does not contain an attribute that implements {nameof(IProvideFindBy)}.");
                            });
                },
                () => throw new ArgumentException($"Expression {by} is not a parsable member expression"));
        }

        public static IEnumerableAsync<TEntity> FindAllInternal<TEntity>(
            TableQuery<TEntity> query,
            CloudTable table,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
            where TEntity : ITableEntity, new()
        {
            var token = default(TableContinuationToken);
            var segmentFecthing = table.ExecuteQuerySegmentedAsync(query, token);
            return EnumerableAsync.YieldBatch<TEntity>(
                async (yieldReturn, yieldBreak) =>
                {
                    if (!cancellationToken.IsDefault())
                        if (cancellationToken.IsCancellationRequested)
                            return yieldBreak;
                    if (segmentFecthing.IsDefaultOrNull())
                        return yieldBreak;
                    try
                    {
                        var segment = await segmentFecthing;
                        if (segment.IsDefaultOrNull())
                            return yieldBreak;

                        token = segment.ContinuationToken;
                        segmentFecthing = token.IsDefaultOrNull() ?
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
                    catch (StorageException ex)
                    {
                        if (!await table.ExistsAsync())
                            return yieldBreak;
                        if (ex.IsProblemTimeout())
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
                },
                cancellationToken: cancellationToken);
        }

        public static IEnumerableAsync<(ITableEntity, string)> FindAllSegmented<TEntity>(
            TableQuery<TEntity> query,
            TableContinuationToken token,
            CloudTable table,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
            where TEntity : ITableEntity, new()
        {
            var segmentFecthing = table.ExecuteQuerySegmentedAsync(query, token);
            return EnumerableAsync.YieldBatch<(ITableEntity, string)>(
                async (yieldReturn, yieldBreak) =>
                {
                    if (!cancellationToken.IsDefault())
                        if (cancellationToken.IsCancellationRequested)
                            return yieldBreak;
                    if (segmentFecthing.IsDefaultOrNull())
                        return yieldBreak;
                    try
                    {
                        var segment = await segmentFecthing;
                        if (segment.IsDefaultOrNull())
                            return yieldBreak;

                        token = segment.ContinuationToken;
                        segmentFecthing = token.IsDefaultOrNull() ?
                            default(Task<TableQuerySegment<TEntity>>)
                            :
                            table.ExecuteQuerySegmentedAsync(query, token);

                        var tokenString = GetToken();
                        var results = segment.Results
                            .Select(result => (result as ITableEntity, tokenString))
                            .ToArray();
                        return yieldReturn(results);

                        string GetToken()
                        {
                            if (token.IsDefaultOrNull())
                                return default;
                            return JsonConvert.SerializeObject(token);
                        }
                    }
                    catch (AggregateException)
                    {
                        throw;
                    }
                    catch (StorageException ex)
                    {
                        if (!await table.ExistsAsync())
                            return yieldBreak;
                        if (ex.IsProblemTimeout())
                        {
                            if (--numberOfTimesToRetry > 0)
                            {
                                await Task.Delay(DefaultBackoffForRetry);
                                segmentFecthing = token.IsDefaultOrNull() ?
                                    default(Task<TableQuerySegment<TEntity>>)
                                    :
                                    table.ExecuteQuerySegmentedAsync(query, token);
                                return yieldReturn(new (ITableEntity, string)[] { });
                            }
                        }
                        throw;
                    }
                },
                cancellationToken: cancellationToken);
        }

        public static async Task<(object, string)> FindQuerySegmentAsync<TEntity>(
            TableQuery<TEntity> query,
            CloudTable table,
            string lastTokenString)
            where TEntity : ITableEntity, new()
        {
            var lastToken = lastTokenString.HasBlackSpace()?
                JsonConvert.DeserializeObject<TableContinuationToken>(lastTokenString)
                :
                default(TableContinuationToken);
            try
            {
                var segment = await table.ExecuteQuerySegmentedAsync(query, lastToken);
                if (segment.IsDefaultOrNull())
                    return default;

                var token = segment.ContinuationToken;
                var tokenString = GetToken();

                var results = segment.Results
                    .ToArray();
                return (results, tokenString);

                string GetToken()
                {
                    if (token.IsDefaultOrNull())
                        return default;
                    return JsonConvert.SerializeObject(token);
                }
            }
            catch (AggregateException)
            {
                throw;
            }
            catch (StorageException)
            {
                if (!await table.ExistsAsync())
                    return (new TEntity[] { }, default(string));
                
                throw;
            }
        }

        #endregion

        #region With modifiers

        public async Task<TResult> CreateAsync<TEntity, TResult>(TEntity entity,
            Func<IAzureStorageTableEntity<TEntity>, TableResult, TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
           RetryDelegate onTimeout = default,
           E5CloudTable table = default)
        {
            var tableEntity = GetEntity(entity);
            if (tableEntity.RowKey.IsNullOrWhiteSpace())
                throw new ArgumentException("RowKey must have value.");

            if (table.IsDefaultOrNull())
                table = GetE5Table<TEntity>();
            return await await tableEntity.ExecuteCreateModifiersAsync<Task<TResult>>(this,
                async rollback =>
                {
                    while (true)
                    {
                        try
                        {
                            var insert = TableOperation.Insert(tableEntity);
                            var tableResult = await table.ExecuteAsync(insert);
                            return onSuccess(tableEntity, tableResult);
                        }
                        catch (StorageException ex)
                        {
                            return await await ex.ResolveCreate(table.cloudTable,
                                async () => await await CreateAsync<Task<TResult>>(tableEntity, table,
                                    (ite, tr) => onSuccess(tableEntity, tr).AsTask(),
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
                                            if (onFailure.IsDefaultOrNull())
                                                throw new Exception($"[{code}]:{msg}");
                                            return onFailure(code, msg);
                                        },
                                    onTimeout: onTimeout), // TODO: Handle rollback with timeout
                                onFailure:
                                    async (code, msg) =>
                                    {
                                        await rollback();
                                        if (onFailure.IsDefaultOrNull())
                                            throw new Exception($"[{code}]:{msg}");
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
                                onTimeout: onTimeout);
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
            Func<TableResult, TResult> onUpdated,
            Func<TResult> onDocumentHasBeenModified,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            RetryDelegate onTimeout = null,
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
                        var tableResult = await new E5CloudTable(table).ExecuteAsync(update);
                        return onUpdated(tableResult);
                    }
                    catch (StorageException ex)
                    {
                        await rollback();

                        if (ex.IsProblemDoesNotExist())
                            if (!onNotFound.IsDefaultOrNull())
                                return onNotFound();

                        return await ex.ParseStorageException(
                            async (errorCode, errorMessage) =>
                            {
                                switch (errorCode)
                                {
                                    case ExtendedErrorInformationCodes.Timeout:
                                        {
                                            var timeoutResult = default(TResult);
                                            if (default(RetryDelegate) == onTimeout)
                                                onTimeout = GetRetryDelegate();
                                            await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                                                async () =>
                                                {
                                                    timeoutResult = await UpdateIfNotModifiedAsync(data, currentDocument,
                                                        onUpdated: onUpdated,
                                                        onDocumentHasBeenModified: onDocumentHasBeenModified,
                                                        onNotFound: onNotFound,
                                                        onFailure: onFailure,
                                                        onModificationFailures: onModificationFailures,
                                                        onTimeout: onTimeout,
                                                            table: table);
                                                });
                                            return timeoutResult;
                                        }
                                    case ExtendedErrorInformationCodes.UpdateConditionNotSatisfied:
                                        {
                                            return onDocumentHasBeenModified();
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

        private async Task<TResult> UpdateIfNotModifiedAsync<TData, TResult>(TData data,
            Func<TResult> success,
            Func<TResult> documentModified,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            RetryDelegate onTimeout = null,
            CloudTable table = default(CloudTable))
        {
            if (table.IsDefaultOrNull())
                table = GetTable<TData>();
            var tableData = GetEntity(data);
            var update = TableOperation.Replace(tableData);
            try
            {
                await new E5CloudTable(table).ExecuteAsync(update);
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
                                    if (default(RetryDelegate) == onTimeout)
                                        onTimeout = GetRetryDelegate();
                                    await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                                        async () =>
                                        {
                                            timeoutResult = await UpdateIfNotModifiedAsync(data,
                                                success,
                                                documentModified,
                                                onFailure: onFailure,
                                                onTimeout: onTimeout,
                                                table: table);
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
        }

        public Task<TResult> ReplaceAsync<TData, TResult>(TData data,
            Func<TResult> success,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            RetryDelegate onTimeout = null) where TData : IReferenceable
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
            RetryDelegate onTimeout = null)
        {
            var table = GetTable<TData>();
            if (tableData.ETag.IsNullOrWhiteSpace())
                tableData.ETag = "*";
            var update = TableOperation.Replace(tableData);
            var rollback = await tableData.ExecuteUpdateModifiersAsync(tableData, this,
                rollbacks => rollbacks,
                (members) => throw new Exception("Modifiers failed to execute."));
            //var rollback = await tableData.ExecuteCreateModifiersAsync(this,
            //    rollbacks => rollbacks,
            //    (members) => throw new Exception("Modifiers failed to execute."));
            try
            {
                await new E5CloudTable(table).ExecuteAsync(update);
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
                                    if (default(RetryDelegate) == onTimeout)
                                        onTimeout = GetRetryDelegate();
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


        public async Task<TResult> InsertOrReplaceAsync<TData, TResult>(TData tableData,
            Func<bool, TData, TResult> onSuccess,
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            RetryDelegate onTimeout = null)
        {
            var tableEntity = GetEntity(tableData);
            var table = GetTable<TData>();
            var update = TableOperation.InsertOrReplace(tableEntity);
            return await await tableEntity.ExecuteInsertOrReplaceModifiersAsync(this,
                async rollback =>
                {
                    try
                    {
                        var result = await new E5CloudTable(table).ExecuteAsync(update);
                        var created = result.HttpStatusCode == ((int)HttpStatusCode.Created);
                        var entity = ((IAzureStorageTableEntity<TData>)result.Result).Entity;
                        return onSuccess(created, entity);
                        // Cosmos.Table.TableResult
                        // 	result.Result	{EastFive.Persistence.Azure.StorageTables.StorageTableAttribute.TableEntity<AffirmHealth.Computations.PatientQualityMeasureStatus>}	
                    }
                    catch (StorageException ex)
                    {
                        return await await ex.ResolveCreate(table,
                            async () => await await InsertOrReplaceAsync<TData, Task<TResult>>(tableData,
                                (created, entity) => onSuccess(created, entity).AsTask(),
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
                                onTimeout: onTimeout);

                        //await rollback();
                        //return await ex.ParseStorageException(
                        //    async (errorCode, errorMessage) =>
                        //    {
                        //        switch (errorCode)
                        //        {
                        //            case ExtendedErrorInformationCodes.Timeout:
                        //                {
                        //                    var timeoutResult = default(TResult);
                        //                    if (default(RetryDelegate) == onTimeout)
                        //                        onTimeout = GetRetryDelegate();
                        //                    await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                        //                        async () =>
                        //                        {
                        //                            timeoutResult = await InsertOrReplaceAsync(tableData,
                        //                                success, onModificationFailures, onFailure, onTimeout);
                        //                        });
                        //                    return timeoutResult;
                        //                }
                        //            default:
                        //                {
                        //                    if (onFailure.IsDefaultOrNull())
                        //                        throw ex;
                        //                    return onFailure(errorCode, errorMessage);
                        //                }
                        //        }
                        //    },
                        //    () =>
                        //    {
                        //        throw ex;
                        //    });
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

        #endregion

        #region Without Modifiers

        #region Mutation

        public async Task<TResult> CreateAsync<TResult>(ITableEntity tableEntity,
                E5CloudTable table,
            Func<ITableEntity, TableResult, TResult> onSuccess,
            Func<TResult> onAlreadyExists,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default)
        {
            while (true)
            {
                try
                {
                    var insert = TableOperation.Insert(tableEntity);
                    TableResult tableResult = await table.ExecuteAsync(insert);
                    return onSuccess(tableResult.Result as ITableEntity, tableResult);
                }
                catch (StorageException ex)
                {
                    var (isComplete, result) = await ex.ResolveCreate(table.cloudTable,
                        () => (false, default(TResult)),
                        onFailure:(codes, why) => (true, onFailure(codes, why)),
                        onAlreadyExists:() => (true, onAlreadyExists()),
                        onTimeout: onTimeout);

                    if (isComplete)
                        return result;

                    continue;
                }
                catch (Exception generalEx)
                {
                    var message = generalEx;
                    throw;
                }
            }
        }

        public Task<TResult> ReplaceAsync<TData, TResult>(ITableEntity tableEntity,
            Func<TResult> success,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            RetryDelegate onTimeout = null)
        {
            var table = GetTable<TData>();
            var e5Table = new E5CloudTable(table);
            return ReplaceAsync(tableEntity, e5Table,
                onSuccess: success,
                onFailure: onFailure,
                onTimeout: onTimeout);
        }

        public async Task<TResult> ReplaceAsync<TResult>(ITableEntity tableEntity, E5CloudTable table,
            Func<TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
            RetryDelegate onTimeout = null)
        {
            var update = TableOperation.Replace(tableEntity);
            try
            {
                await table.ExecuteAsync(update);
                return onSuccess();
            }
            catch (StorageException ex)
            {
                if(ex.IsProblemTableDoesNotExist())
                {
                    await table.cloudTable.CreateIfNotExistsAsync();
                    return await ReplaceAsync(tableEntity: tableEntity, table:table,
                        onSuccess: onSuccess,
                        onFailure: onFailure,
                        onTimeout: onTimeout);
                }
                return await ex.ParseStorageException(
                    async (errorCode, errorMessage) =>
                    {
                        switch (errorCode)
                        {
                            case ExtendedErrorInformationCodes.Timeout:
                                {
                                    var timeoutResult = default(TResult);
                                    if (default(RetryDelegate) == onTimeout)
                                        onTimeout = GetRetryDelegate();
                                    await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                                        async () =>
                                        {
                                            timeoutResult = await ReplaceAsync<TResult>(tableEntity, table,
                                                onSuccess, onFailure, onTimeout);
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

        public Task<TResult> InsertOrReplaceAsync<TData, TResult>(ITableEntity tableEntity,
            Func<bool, IAzureStorageTableEntity<TData>, TResult> success,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
                RetryDelegate onTimeout = null,
                string tableName = default)
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TData>();
            return InsertOrReplaceAsync<TResult>(tableEntity, new E5CloudTable(table),
                (created, result) =>
                {
                    var entity = result.Result as IAzureStorageTableEntity<TData>;
                    return success(created, entity);
                },
                onFailure: onFailure,
                    onTimeout: onTimeout);
        }

        public async Task<TResult> InsertOrReplaceAsync<TResult>(ITableEntity tableEntity, E5CloudTable table,
            Func<bool, TableResult, TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null,
                RetryDelegate onTimeout = null)
        {
            var update = TableOperation.InsertOrReplace(tableEntity);
            try
            {
                TableResult result = await table.ExecuteAsync(update);
                var created = result.HttpStatusCode == ((int)HttpStatusCode.Created); // sometimes 204 (No Content) is returned when a row is created
                return onSuccess(created, result);
            }
            catch (StorageException ex)
            {
                if (ex.IsProblemTableDoesNotExist())
                {
                    await table.cloudTable.CreateIfNotExistsAsync();
                    return await InsertOrReplaceAsync(tableEntity: tableEntity, table: table,
                        onSuccess: onSuccess,
                        onFailure: onFailure,
                        onTimeout: onTimeout);
                }
                return await ex.ParseStorageException(
                    async (errorCode, errorMessage) =>
                    {
                        switch (errorCode)
                        {
                            case ExtendedErrorInformationCodes.Timeout:
                                {
                                    var timeoutResult = default(TResult);
                                    if (default(RetryDelegate) == onTimeout)
                                        onTimeout = GetRetryDelegate();
                                    await onTimeout(ex.RequestInformation.HttpStatusCode, ex,
                                        async () =>
                                        {
                                            timeoutResult = await InsertOrReplaceAsync(tableEntity, table,
                                                onSuccess, onFailure, onTimeout);
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

        public async Task<TResult> DeleteAsync<TResult>(ITableEntity entity, CloudTable table,
            Func<TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<TResult> onModified = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default)
        {
            var delete = TableOperation.Delete(entity);
            try
            {
                var response = await new E5CloudTable(table).ExecuteAsync(delete);
                if (response.HttpStatusCode == (int)HttpStatusCode.NotFound)
                    return onNotFound();
                return onSuccess();
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
                                    if (default(RetryDelegate) == onTimeout)
                                        onTimeout = GetRetryDelegate();
                                    await onTimeout(se.RequestInformation.HttpStatusCode, se,
                                        async () =>
                                        {
                                            timeoutResult = await DeleteAsync<TResult>(
                                                entity, table, onSuccess, onNotFound, onModified, onFailure, onTimeout);
                                        });
                                    return timeoutResult;
                                }
                            case ExtendedErrorInformationCodes.TableNotFound:
                            case ExtendedErrorInformationCodes.TableBeingDeleted:
                                {
                                    return onNotFound();
                                }
                            case ExtendedErrorInformationCodes.UpdateConditionNotSatisfied:
                                {
                                    return onModified();
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

        #region Batch

        public async Task<TableResult[]> CreateOrReplaceBatchAsync<TDocument>(string partitionKey, TDocument[] entities,
            CloudTable table = default(CloudTable),
                RetryDelegate onTimeout = default(RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
            where TDocument : ITableEntity
        {
            if (!entities.Any())
                return new TableResult[] { };

            if (table.IsDefaultOrNull())
                table = GetTable<TDocument>();

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
                rowKeyHash.Add(row.RowKey);
                batch.InsertOrReplace(row);
            }

            // submit
            diagnostics.Trace($"Saving {batch.Count} records.");
            var resultList = await table.ExecuteBatchWithCreateAsync(batch);
            return resultList.ToArray();
        }

        public IEnumerableAsync<TableResult> DeleteAll<TEntity>(
            Expression<Func<TEntity, bool>> filter,
            CloudTable table = default(CloudTable),
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry)
            where TEntity : IReferenceable
        {
            var finds = FindAll(filter, table: table, numberOfTimesToRetry: numberOfTimesToRetry);
            var deleted = finds
                .Select(entity => GetEntity(entity))
                .GroupBy(doc => doc.PartitionKey)
                .Select(
                    rowsToDeleteGrp =>
                    {
                        var partitionKey = rowsToDeleteGrp.Key;
                        var deletions = rowsToDeleteGrp
                            .Batch()
                            .Select(items =>
                            {
                                return items
                                    .Split(index => 100)
                                    .Select(grp => DeleteBatchAsync<TEntity>(partitionKey, grp.ToArray()))
                                .Throttle()
                                .SelectMany();
                            })
                            .SelectAsyncMany();
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

            if (table.IsDefaultOrNull())
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

            try
            {
                var resultList = await table.ExecuteBatchAsync(batch);
                return resultList.ToArray();
            }
            catch (StorageException storageException)
            {
                if (storageException.IsProblemTableDoesNotExist())
                    return new TableResult[] { };
                throw;
            }
        }

        #endregion

        #endregion

        #region Delete

        public Task<TResult> DeleteLookupBy<TRefEntity, TEntity, TResult>(TEntity entity,
                    Expression<Func<TEntity, IRefOptional<TRefEntity>>> by,
                Func<TResult> onSuccess,
                Func<TResult> onFailure)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return DeleteLookupInternal(entity, by, onSuccess, onFailure);
        }

        public Task<TResult> DeleteLookupBy<TRefEntity, TEntity, TResult>(TEntity entity,
                    Expression<Func<TEntity, IRef<TRefEntity>>> by,
                Func<TResult> onSuccess,
                Func<TResult> onFailure)
            where TEntity : IReferenceable
            where TRefEntity : IReferenceable
        {
            return DeleteLookupInternal(entity, by, onSuccess, onFailure);
        }

        private Task<TResult> DeleteLookupInternal<TMatch, TDocument, TResult>(TDocument document,
                Expression<Func<TDocument, TMatch>> by,
            Func<TResult> onSuccess,
            Func<TResult> onFailure)
                where TDocument : IReferenceable
        {
            if (!typeof(TDocument).TryGetAttributeInterface(out IProvideEntity entityProvider))
                throw new ArgumentException($"`{typeof(TDocument).FullName}` does not contain attribute that implements `{nameof(IProvideEntity)}`");

            return by.MemberInfo(
                (memberCandidate, expr) =>
                {
                    if (!memberCandidate.TryGetAttributeInterface(out IModifyAzureStorageTableSave modifier))
                        throw new ArgumentException($"{memberCandidate.DeclaringType.FullName}..{memberCandidate.Name} is not a lookup attribute.");

                    var entity = entityProvider.GetEntity(document);
                    var entityProperties = entity.WriteEntity(null);

                    return modifier.ExecuteDeleteAsync<TDocument, TResult>(memberCandidate,
                            rowKeyRef: entity.RowKey, partitionKeyRef: entity.PartitionKey,
                            document, entityProperties,
                            this,
                        onSuccessWithRollback: (rollback) => onSuccess(),
                        onFailure: () => onFailure());
                },
                () => throw new ArgumentException($"{by} is not a member expression."));
        }

        #endregion

        #endregion

        #region CREATE

        #region No modifiers

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
            RetryDelegate onTimeout = default)
        {
            var table = this.TableClient.GetTableReference(tableName);
            return this.CreateAsync(tableEntity, new E5CloudTable(table),
                onSuccess: (entity, tr) => onSuccess(entity),
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
            RetryDelegate onTimeout = default)
        {
            var table = GetTable<TEntity>();
            return this.CreateAsync(tableEntity, new E5CloudTable(table),
                onSuccess: (entity, tr) => onSuccess(entity),
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
            RetryDelegate onTimeout = default)
        {
            var table = TableFromEntity(entityType, this.TableClient);
            return this.CreateAsync(tableEntity, new E5CloudTable(table),
                onSuccess: (entity, tr) => onSuccess(entity),
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                onTimeout: onTimeout);
        }

        #endregion

        public async Task<TResult> InsertOrReplaceAsync<TData, TResult>(TData data,
            Func<bool, TResult> onUpdate,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default,
            string tableName = default)
        {
            var table = default(CloudTable);
            if (tableName.HasBlackSpace())
                table = this.TableClient.GetTableReference(tableName);
            if (table.IsDefaultOrNull())
                table = tableName.HasBlackSpace() ?
                    this.TableClient.GetTableReference(tableName)
                    :
                    table = GetTable<TData>();

            var entity = GetEntity(data);

            var repo = this;
            return await await this.InsertOrReplaceAsync<TData, Task<TResult>>(entity,
                (created, updatedEntity) =>
                {
                    if (created)
                    {
                        return entity.ExecuteCreateModifiersAsync<TResult>(repo,
                            (discardRollback) => onUpdate(true),
                            (errors) => throw new Exception());
                    }
                    return entity.ExecuteUpdateModifiersAsync(updatedEntity, repo,
                        (discardRollback) => onUpdate(false),
                        (members) => throw new Exception("Modifiers failed to execute."));
                },
                onFailure: onFailure.AsAsyncFunc());
        }

        public Task<TResult> UpdateOrCreateAsync<TData, TResult>(string rowKey, string partitionKey,
            Func<bool, TData, Func<TData, Task<TableResult>>, Task<TResult>> onUpdate,
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default,
            string tableName = default(string))
        {
            var table = default(CloudTable);
            if (tableName.HasBlackSpace())
                table = this.TableClient.GetTableReference(tableName);
            if (table.IsDefaultOrNull())
                table = tableName.HasBlackSpace() ?
                    this.TableClient.GetTableReference(tableName)
                    :
                    table = GetTable<TData>();

            return this.UpdateAsyncAsync<TData, TResult>(rowKey, partitionKey,
                (doc, saveAsync) => onUpdate(false, doc, saveAsync),
                onNotFound: async () =>
                {
                    var doc = Activator.CreateInstance<TData>();
                    doc = doc
                        .StorageParseRowKey(rowKey)
                        .StorageParsePartitionKey(partitionKey);
                    var global = default(TResult);
                    var useGlobal = false;
                    var result = await onUpdate(true, doc,
                        async (docUpdated) =>
                        {
                            var useGlobalTableResult = await await this.CreateAsync<TData, Task<(bool, TableResult)>>(docUpdated,
                                onSuccess: (discard, tableResult) => (false, tableResult).AsTask(),
                                onAlreadyExists:
                                    async () =>
                                    {
                                        global = await this.UpdateOrCreateAsync<TData, TResult>(
                                                rowKey, partitionKey,
                                            onUpdate,
                                            onTimeoutAsync: onTimeoutAsync,
                                            tableName: tableName);
                                        return (true, default(TableResult));
                                    },
                                onFailure:
                                    (code, why) =>
                                    {
                                        if (onFailure.IsDefaultOrNull())
                                            throw new Exception($"Storage Exception:{code} -- {why}");
                                        global = onFailure(code, why);
                                        return (true, default(TableResult)).AsTask();
                                    },
                                // TODO:
                                //onModificationFailures:
                                //    () =>
                                //    {
                                //        // global = onModificationFailures();
                                //        throw new NotImplementedException();
                                //        return true.AsTask();
                                //    },
                                table: new E5CloudTable(table));
                            useGlobal = useGlobalTableResult.Item1;
                            return useGlobalTableResult.Item2;
                        });
                    if (useGlobal)
                        return global;
                    return result;
                },
                onModificationFailures: onModificationFailures,
                onFailure: onFailure.AsAsyncFunc(),
                table: table);
        }

        #endregion

        #region Find

        public Task<TResult> FindByIdAsync<TEntity, TResult>(
                Guid rowId,
            Func<TEntity, string, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            CloudTable table = default(CloudTable),
            RetryDelegate onTimeout =
                default(RetryDelegate))
            where TEntity : IReferenceable
        {
            var entityRef = rowId.AsRef<TEntity>();
            var (rowKey, partitionKey) = entityRef.StorageComputeRowAndPartitionKey();
            return FindByIdAsync<TEntity, TResult>(rowKey, partitionKey,
                onFound: (v, tr) => onSuccess(v, tr.Etag),
                onNotFound: onNotFound,
                onFailure: onFailure,
                table: table,
                onTimeout: onTimeout);
        }

        public IEnumerableAsync<TEntity> FindByIdsAsync<TEntity>(
                IRefAst[] rowKeys,
            CloudTable table = default(CloudTable),
            RetryDelegate onTimeout =
                default(RetryDelegate),
            int? readAhead = default)
            where TEntity : IReferenceable
        {
            if (table.IsDefaultOrNull())
                table = GetTable<TEntity>();
            return rowKeys
                .Select(
                    rowKey =>
                    {
                        return FindByIdAsync<TEntity, KeyValuePair<bool, TEntity>?>(rowKey.RowKey, rowKey.PartitionKey,
                            (entity, tableResult) => entity.PairWithKey(true),
                            () => default,
                            table: table,
                            onTimeout: onTimeout);
                    })
                .AsyncEnumerable(readAhead.HasValue ? readAhead.Value : 0)
                .SelectWhereHasValue()
                .SelectValues();
        }

        public IEnumerableAsync<TableResult> Copy(
            string filter,
            string tableName,
            AzureTableDriverDynamic copyTo,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var table = this.TableClient.GetTableReference(tableName);
            var query = new TableQuery<GenericTableEntity>();
            var filteredQuery = query.Where(filter);
            var allRows = FindAllInternal(filteredQuery, table, numberOfTimesToRetry, cancellationToken);
            return copyTo.CreateOrReplaceBatch(allRows,
                row => row.RowKey,
                row => row.PartitionKey,
                (row, action) => action,
                tableName);
        }

        public IEnumerableAsync<TEntity> FindAll<TEntity>(
            Expression<Func<TEntity, bool>> filter,
            CloudTable table = default,
            string tableName = default,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            ICacheEntites cache = default)
        {
            if (table.IsDefaultOrNull())
                if (tableName.HasBlackSpace())
                    table = this.TableClient.GetTableReference(tableName);
            Func<TEntity, bool> postFilter = (e) => true;
            var whereFilter = typeof(TEntity)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(member => member.ContainsAttributeInterface<IProvideTableQuery>())
                .Aggregate(string.Empty,
                    (currentQuery, member) =>
                    {
                        var queryModifier = member.GetAttributeInterface<IProvideTableQuery>();
                        var nextPartOfQuery = queryModifier.ProvideTableQuery(member, filter,
                            out Func<TEntity, bool> postFilterForMember);
                        if (currentQuery.IsNullOrWhiteSpace())
                        {
                            postFilter = postFilterForMember;
                            return nextPartOfQuery;
                        }
                        var lastPostFilter = postFilter;
                        postFilter = (e) => lastPostFilter(e) && postFilterForMember(e);
                        return TableQuery.CombineFilters(
                            currentQuery,
                            TableOperators.And,
                            nextPartOfQuery);
                    });

            return cache
                .ByQueryEx(whereFilter,
                    () =>
                    {
                        return RunQuery<TEntity>(whereFilter, table,
                            numberOfTimesToRetry: numberOfTimesToRetry);
                    })
                .Where(f => postFilter(f));
        }

        public IEnumerableAsync<TEntity> FindByQuery<TEntity>(
            string whereFilter,
            CloudTable table = default,
            string tableName = default,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            ICacheEntites cache = default)
        {
            if (table.IsDefaultOrNull())
                if (tableName.HasBlackSpace())
                    table = this.TableClient.GetTableReference(tableName);
            Func<TEntity, bool> postFilter = (e) => true;

            return cache
                .ByQueryEx(whereFilter,
                    () =>
                    {
                        return RunQuery<TEntity>(whereFilter, table,
                            numberOfTimesToRetry: numberOfTimesToRetry);
                    })
                .Where(f => postFilter(f));
        }

        public IEnumerableAsync<TEntity> FindBy<TEntity>(IQueryable<TEntity> entityQuery,
            string tableName = default,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var runQueryData = ParseFindBy(entityQuery, tableName);

            return RunQuery<TEntity>(runQueryData.Item2, runQueryData.Item1,
                cancellationToken: cancellationToken);
        }

        public IEnumerableAsync<(TEntity, string)> FindBySegmented<TEntity>(IQueryable<TEntity> entityQuery,
            TableContinuationToken token,
            string tableName = default,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var runQueryData = ParseFindBy(entityQuery, tableName);

            return RunQuerySegmented<TEntity>(runQueryData.Item2, runQueryData.Item1, token,
                cancellationToken: cancellationToken);
        }

        public (CloudTable, string) ParseFindBy<TEntity>(IQueryable<TEntity> entityQuery,
            string tableName = default)
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TEntity>();

            var assignments = entityQuery
                .Compile<Assignment[], IProvideQueryValues>(new Assignment[] { },
                    (currentAssignments, queryModifier, methodInfo, methodArguments) =>
                    {
                        return queryModifier
                            .GetStorageValues(methodInfo, methodArguments)
                            .Concat(currentAssignments)
                            .ToArray();
                    },
                    (queryCurrent, unrecognizedMethod, methodArguments) =>
                    {
                        if (unrecognizedMethod.Name == "Where")
                        {
                            return unrecognizedMethod.TryParseMemberAssignment(methodArguments,
                                (memberInfo, expressionType, memberValue) =>
                                {
                                    return queryCurrent
                                        .Append(
                                            new Assignment
                                            {
                                                member = memberInfo,
                                                type = expressionType,
                                                value = memberValue,
                                            })
                                        .ToArray();
                                },
                                () => throw new ArgumentException(
                                    $"Could not parse `{unrecognizedMethod}`({methodArguments})"));
                        }
                        throw new ArgumentException(
                            $"{unrecognizedMethod.DeclaringType.Name}..{unrecognizedMethod.Name} is not a valid story query method.");
                    })
                .ToArray();

            Func<TEntity, bool> postFilter = (e) => true;
            var (assignmentsUsedTotal, filter) = assignments
                .Distinct(assignment => assignment.member.Name)
                .TryWhere((Assignment assignment, out IProvideTableQuery tableQueryProvider)
                    => assignment.member.TryGetAttributeInterface(out tableQueryProvider))
                .Aggregate((assignmentsUsed:new string[] { }, query:string.Empty),
                    (assignmentsUsedQueryCurrentTpl, assignmentQueryTpl) =>
                    {
                        var (assignmentsUsedCurrent, queryCurrent) = assignmentsUsedQueryCurrentTpl;
                        var (assignment, tableQueryProvider) = assignmentQueryTpl;
                        //var queryValueProvider = assignment.member.GetAttributeInterface<IProvideTableQuery>();
                        var newFilter = tableQueryProvider.ProvideTableQuery<TEntity>(
                            assignment.member, assignments,
                            out Func<TEntity, bool> postFilterForMember,
                            out string [] assignmentsUsed);
                        var lastPostFilter = postFilter;
                        postFilter = (e) => lastPostFilter(e) && postFilterForMember(e);

                        var assignmentsUsedUpdated = assignmentsUsedCurrent.Concat(assignmentsUsed).ToArray();
                        if (queryCurrent.IsNullOrWhiteSpace())
                            return (assignmentsUsedUpdated, newFilter);
                        var combinedFilter = TableQuery.CombineFilters(queryCurrent, TableOperators.And, newFilter);
                        return (assignmentsUsedUpdated, combinedFilter);
                    });

            return assignments
                .Where(assignment => !assignmentsUsedTotal.Contains(assignment.member.Name))
                .First(
                    (assignment, next) =>
                    {
                        var memberDisplay = $"{assignment.member.DeclaringType.FullName}..{assignment.member.Name} {assignment.type} `{assignment.value}`";
                        var additionalAssignments = assignments
                            .Select(assign => $"{assign.member.DeclaringType.FullName}..{assign.member.Name} {assign.type} `{assign.value}`")
                            .Join(',');
                        throw new ArgumentException($"{memberDisplay} does not have an attribute implementing {nameof(IProvideTableQuery)}" +
                            $" but is used in a {typeof(TEntity).FullName} query. AdditionalAssignments includes: {additionalAssignments}");
                    },
                    () =>
                    {
                        return (table, filter);
                    });
        }

        private IEnumerableAsync<TEntity> RunQuery<TEntity>(string whereFilter, CloudTable table,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return RunQueryForTableEntries<TEntity>(whereFilter, table: table,
                    numberOfTimesToRetry: numberOfTimesToRetry, cancellationToken: cancellationToken)
                .Select(segResult => segResult.Entity);
        }

        public IEnumerableAsync<IWrapTableEntity<TEntity>> RunQueryForTableEntries<TEntity>(string whereFilter,
            CloudTable table = default,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var query = whereFilter.AsTableQuery<TEntity>();

            var tableEntityTypes = query.GetType().GetGenericArguments();
            if (table.IsDefaultOrNull())
            {
                var tableEntityType = tableEntityTypes.First();
                if (tableEntityType.IsSubClassOfGeneric(typeof(IWrapTableEntity<>)))
                {
                    tableEntityType = tableEntityType.GetGenericArguments().First();
                }
                table = AzureTableDriverDynamic.TableFromEntity(tableEntityType, this.TableClient);
            }
            var findAllIntermediate = typeof(AzureTableDriverDynamic)
                .GetMethod(nameof(FindAllInternal), BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(tableEntityTypes)
                .Invoke(null, new object[] { query, table, numberOfTimesToRetry, cancellationToken });
            return findAllIntermediate as IEnumerableAsync<IWrapTableEntity<TEntity>>;
        }

        private IEnumerableAsync<(TEntity, string)> RunQuerySegmented<TEntity>(string whereFilter,
            CloudTable table, TableContinuationToken token,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var query = whereFilter.AsTableQuery<TEntity>();

            var tableEntityTypes = query.GetType().GetGenericArguments();
            if (table.IsDefaultOrNull())
            {
                var tableEntityType = tableEntityTypes.First();
                if (tableEntityType.IsSubClassOfGeneric(typeof(IWrapTableEntity<>)))
                {
                    tableEntityType = tableEntityType.GetGenericArguments().First();
                }
                table = AzureTableDriverDynamic.TableFromEntity(tableEntityType, this.TableClient);
            }

            //AzureTableDriverDynamic.FindAllSegmented<TEntity>(query, token, table,
            //    numberOfTimesToRetry: numberOfTimesToRetry,
            //    cancellationToken: cancellationToken);

            var findAllIntermediate = typeof(AzureTableDriverDynamic)
                .GetMethod(nameof(FindAllSegmented), BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(tableEntityTypes)
                .Invoke(null, new object[] { query, token, table, numberOfTimesToRetry, cancellationToken });
            var findAllCasted = findAllIntermediate as IEnumerableAsync<(ITableEntity, string)>;
            return findAllCasted
                .Select(segResult => (
                    (segResult.Item1 as IWrapTableEntity<TEntity>).Entity,
                    segResult.Item2));
        }

        public IEnumerableAsync<TData> FindByPartition<TData>(string partitionKeyValue,
            string tableName = default,
            System.Threading.CancellationToken cancellationToken = default)
            where TData : ITableEntity, new()
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TData>();
            string filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKeyValue);
            var tableQuery = new TableQuery<TData>().Where(filter);
            return FindAllInternal(tableQuery, table, cancellationToken: cancellationToken);
        }

        public IEnumerableAsync<TEntity> FindEntityBypartition<TEntity>(string partitionKeyValue,
            string tableName = default,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TEntity>();
            string filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKeyValue);
            var tableQuery = TableQueryExtensions.GetTableQuery<TEntity>(filter);
            var tableEntityTypes = tableQuery.GetType().GetGenericArguments();
            var findAllIntermediate = typeof(AzureTableDriverDynamic)
                .GetMethod(nameof(FindAllInternal), BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(tableEntityTypes)
                .Invoke(null, new object[] { tableQuery, table, numberOfTimesToRetry, cancellationToken });
            var findAllCasted = findAllIntermediate as IEnumerableAsync<IWrapTableEntity<TEntity>>;
            return findAllCasted
                .Select(segResult => segResult.Entity);
        }

        public async Task<(TEntity[], string)> FindSegmentAsync<TEntity>(string filter, string lastTokenString,
            string tableName = default,
            int numberOfTimesToRetry = DefaultNumberOfTimesToRetry,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TEntity>();
            var tableQuery = TableQueryExtensions.GetTableQuery<TEntity>(filter);
            var tableEntityTypes = tableQuery.GetType().GetGenericArguments();
            var findAllIntermediateTaskObjObj = typeof(AzureTableDriverDynamic)
                .GetMethod(nameof(FindQuerySegmentAsync), BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(tableEntityTypes)
                .Invoke(null, new object[] { tableQuery, table, lastTokenString });
            var findAllIntermediateTaskObj = findAllIntermediateTaskObjObj.CastAsTaskObjectAsync(out Type resultType);
            var findAllIntermediateObj = await findAllIntermediateTaskObj;
            // var findAllIntermediateTask = findAllIntermediateTaskObj.CastTask<(IWrapTableEntity<TEntity>[], string)>();
            var (findAllCastedObj, nextToken) = ((object, string))findAllIntermediateObj;
            var findAllCastedIEnumerable = (IEnumerable)findAllCastedObj;
            var findAllCasted = GetCasted();
            var entities = findAllCasted
                .Select(segResult => segResult.Entity)
                .ToArray();
            return (entities, nextToken);

            IEnumerable<IWrapTableEntity<TEntity>> GetCasted()
            {
                foreach (var i in findAllCastedIEnumerable)
                {
                    var cast = (IWrapTableEntity<TEntity>)i;
                    yield return cast;
                }
            }
        }

        #endregion

        #region Update

        public Task<TResult> UpdateAsync<TData, TResult>(string rowKey, string partitionKey,
            Func<TData, Func<TData, Task<TableResult>>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            Func<ExtendedErrorInformationCodes, string, Task<TResult>> onFailure = default,
            CloudTable table = default(CloudTable),
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(RetryDelegateAsync<Task<TResult>>))
        {
            return UpdateAsyncAsync(rowKey, partitionKey,
                onUpdate,
                onNotFound.AsAsyncFunc(),
                onModificationFailures: onModificationFailures,
                onFailure: onFailure,
                    table: table, onTimeoutAsync: onTimeoutAsync);
        }

        public Task<TResult> UpdateAsync<TResult>(string rowKey, string partitionKey,
                Type typeData,
            Func<object, Func<object, Task>, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            string tableName = default,
            CloudTable table = default,
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(RetryDelegateAsync<Task<TResult>>))
        {
            return UpdateAsyncAsync(rowKey, partitionKey,
                    typeData,
                onUpdate,
                onNotFound.AsAsyncFunc(),
                    tableName: tableName,
                    table: table,
                    onTimeoutAsync: onTimeoutAsync);
        }

        public async Task<TResult> UpdateAsyncAsync<TData, TResult>(Guid documentId,
            Func<TData, Func<TData, Task<TableResult>>, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default(Func<Task<TResult>>),
            Func<ExtendedErrorInformationCodes, string, Task<TResult>> onFailure = default,
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(RetryDelegateAsync<Task<TResult>>))
            where TData : IReferenceable
        {
            var entityRef = documentId.AsRef<TData>();
            var (rowKey, partitionKey) = entityRef.StorageComputeRowAndPartitionKey();
            return await UpdateAsyncAsync(rowKey, partitionKey,
                onUpdate,
                onNotFound,
                onFailure: onFailure);
        }

        private class UpdateModificationFailure<TResult> : IHandleFailedModifications<Task<(bool, TableResult)>>
        {
            public IHandleFailedModifications<TResult>[] onModificationFailures;
            public TableResult tableResult;

            public Action<TResult> setGlobalCallback;

            public bool DoesMatchMember(MemberInfo[] membersWithFailures)
            {
                return true;
            }

            Task<(bool, TableResult)> IHandleFailedModifications<Task<(bool, TableResult)>>.ModificationFailure(MemberInfo[] membersWithFailures)
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
                return (true, tableResult).AsTask();
            }
        }

        public async Task<TResult> UpdateAsyncAsync<TData, TResult>(string rowKey, string partitionKey,
            Func<TData, Func<TData, Task<TableResult>>, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default(Func<Task<TResult>>),
            IHandleFailedModifications<TResult>[] onModificationFailures = default,
            Func<ExtendedErrorInformationCodes, string, Task<TResult>> onFailure = default,
            CloudTable table = default(CloudTable),
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(RetryDelegateAsync<Task<TResult>>))
        {
            return await await FindByIdAsync(rowKey, partitionKey,
                async (TData currentStorage, TableResult tableResult) =>
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
                        tableResult = tableResult,
                    };
                    var entity = typeof(TData).IsClass ?
                        GetEntity((TData)((object)currentStorage).CloneObject())
                        :
                        GetEntity(currentStorage);
                    var eTag = entity.ETag;
                    var resultLocal = await onUpdate(currentStorage,
                        async (documentToSave) =>
                        {
                            var useResultGlobalTableResult = await await UpdateIfNotModifiedAsync<TData, Task<(bool, TableResult)>>(documentToSave,
                                    entity,
                                onUpdated: (tr) =>
                                {
                                    return (false, tr).AsTask();
                                },
                                onDocumentHasBeenModified: async () =>
                                {
                                    var updatedETag = documentToSave.StorageGetETag();
                                    if (eTag != updatedETag)
                                        throw new ArgumentException($"Cannot change ETag in update. {typeof(TData).FullName}:{eTag} => {updatedETag}");

                                    var updatedRowKey = documentToSave.StorageGetRowKey();
                                    if (rowKey != updatedRowKey)
                                        throw new ArgumentException($"Cannot change row key in update. {typeof(TData).FullName}:{rowKey} => {updatedRowKey}");

                                    var updatedPartitionKey = documentToSave.StorageGetPartitionKey();
                                    if (partitionKey != updatedPartitionKey)
                                        throw new ArgumentException($"Cannot change partition key in update. {typeof(TData).FullName}:{partitionKey} => {updatedPartitionKey}");

                                    var trGlobal = default(TableResult);
                                    if (onTimeoutAsync.IsDefaultOrNull())
                                    {
                                        resultGlobal = await UpdateAsyncAsync<TData, TResult>(rowKey, partitionKey,
                                            onUpdate: (entity, saveAsync) =>
                                             {
                                                 return onUpdate(entity,
                                                     async (entityToSave) =>
                                                     {
                                                         trGlobal = await saveAsync(entityToSave);
                                                         return trGlobal;
                                                     });
                                             },
                                            onNotFound,
                                            onModificationFailures: onModificationFailures,
                                                table: table);
                                        return (true, trGlobal);
                                    }

                                    resultGlobal = await await onTimeoutAsync(
                                        async () => await UpdateAsyncAsync<TData, TResult>(rowKey, partitionKey,
                                            onUpdate: (entity, saveAsync) =>
                                            {
                                                return onUpdate(entity,
                                                    async (entityToSave) =>
                                                    {
                                                        trGlobal = await saveAsync(entityToSave);
                                                        return trGlobal;
                                                    });
                                            },
                                            onNotFound,
                                            onModificationFailures: onModificationFailures,
                                                table: table, onTimeoutAsync: onTimeoutAsync),
                                        (numberOfRetries) => { throw new Exception("Failed to gain atomic access to document after " + numberOfRetries + " attempts"); });
                                    return (true, trGlobal);
                                },
                                onNotFound: async () =>
                                {
                                    if (onNotFound.IsDefaultOrNull())
                                        throw new Exception($"Document [{partitionKey}/{rowKey}] of type {documentToSave.GetType().FullName} was not found.");
                                    resultGlobal = await onNotFound();
                                    return (true, tableResult);
                                },
                                onFailure: async (errorCodes, why) =>
                                {
                                    if (onFailure.IsDefaultOrNull())
                                        throw new Exception(why);
                                    resultGlobal = await onFailure(errorCodes, why);
                                    return (true, tableResult);
                                },
                                onModificationFailures: modificationFailure.AsArray(),
                                onTimeout: GetRetryDelegate(),
                                table: table);
                            useResultGlobal = useResultGlobalTableResult.Item1;
                            return useResultGlobalTableResult.Item2;
                        });

                    return useResultGlobal ? resultGlobal : resultLocal;
                },
                onNotFound,
                onFailure: default(Func<ExtendedErrorInformationCodes, string, Task<TResult>>),
                table: table,
                onTimeout: GetRetryDelegate());
        }

        public async Task<TResult> UpdateAsyncAsync<TResult>(string rowKey, string partitionKey,
                Type typeData,
            Func<object, Func<object, Task>, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default(Func<Task<TResult>>),
            string tableName = default,
            CloudTable table = default,
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync =
                default(RetryDelegateAsync<Task<TResult>>))
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
                    .GetMethod(nameof(AzureTableDriverDynamic.GetTable), BindingFlags.NonPublic | BindingFlags.Instance)
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
                                .GetMethod(nameof(AzureTableDriverDynamic.GetEntity), BindingFlags.NonPublic | BindingFlags.Static)
                                .MakeGenericMethod(typeData.AsArray())
                                .Invoke(this, currentStorage.AsArray());

                            Func<Task<bool>> success = () => false.AsTask();
                            Func<Task<bool>> documentModified = async () =>
                            {
                                if (onTimeoutAsync.IsDefaultOrNull())
                                    onTimeoutAsync = GetRetryDelegateContentionAsync<Task<TResult>>();

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
                            //    onTimeout: GetRetryDelegate(),
                            //    table: table);
                            useResultGlobal = await await (Task<Task<bool>>)typeof(AzureTableDriverDynamic)
                                .GetMethod(nameof(AzureTableDriverDynamic.UpdateIfNotModifiedAsync), BindingFlags.NonPublic | BindingFlags.Instance)
                                .MakeGenericMethod(new Type[] { typeData, typeof(Task<bool>) })
                                .Invoke(this, new object[] { documentToSave, entity, success, documentModified, null,
                                    GetRetryDelegate(), table });


                        });
                    return useResultGlobal ? resultGlobal : resultLocal;
                },
                onNotFound,
                default,
                table: table,
                onTimeout: GetRetryDelegate());
        }

        #endregion

        #region Batch

        public IEnumerableAsync<TResult> CreateOrReplaceBatch<TData, TResult>(IEnumerable<TData> datas,
                Func<ITableEntity, TableResult, TResult> perItemCallback,
                string tableName = default(string),
                RetryDelegate onTimeout = default(RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
        {
            var table = tableName.HasBlackSpace() ?
                   this.TableClient.GetTableReference(tableName)
                   :
                   GetTable<TData>();
            var batchedEntities = datas
                .Select(data => GetEntity(data));

            return CreateOrUpdateBatch(batchedEntities,
                perItemCallback,
                table: table,
                onTimeout: onTimeout,
                diagnostics: diagnostics);
        }

        public IEnumerableAsync<TResult> CreateOrUpdateBatch<TResult>(IEnumerableAsync<ITableEntity> entities,
            Func<ITableEntity, TableResult, TResult> perItemCallback,
            string tableName = default(string),
            RetryDelegate onTimeout = default(RetryDelegate),
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
                RetryDelegate onTimeout = default(RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
            where TDocument : class, ITableEntity
        {
            var table = tableName.HasBlackSpace() ?
                TableClient.GetTableReference(tableName)
                :
                default(CloudTable);
            return entities
                .Batch()
                .Select(
                    rows =>
                    {
                        return CreateOrReplaceBatch(rows, 
                            getRowKey, getPartitionKey, perItemCallback, 
                            table: table,
                            out Task<object[]> modifiersTask,
                            onTimeout);
                    })
                .SelectAsyncMany();
        }

        public IEnumerableAsync<TResult> CreateOrReplaceBatch<TData, TResult>(IEnumerableAsync<TData> datas,
                Func<ITableEntity, TableResult, TResult> perItemCallback,
                string tableName = default(string),
                RetryDelegate onTimeout = default(RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger),
                int? readAhead = default)
            where TData : IReferenceable
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TData>();

            var batchedEntities = datas
                .Select(data => GetEntity(data))
                .Batch();

            //var savingModifiers = batchedEntities
            //    .Select(
            //        rows => SaveModifiersAsync(rows))
            //    .ToArrayAsync();

            return batchedEntities
                .Select(
                    async rows =>
                    {
                        var x = CreateOrReplaceBatch(rows,
                                row => row.RowKey,
                                row => row.PartitionKey,
                                perItemCallback, table,
                                out Task<object[]> modifiersTask,
                                onTimeout: onTimeout,
                                diagnostics: diagnostics,
                                readAhead: readAhead)
                            .ToArrayAsync();
                        await modifiersTask;
                        return await x;
                    })
                .Await(readAhead: 50)
                .SelectMany();
                // .SelectAsyncMany();
                //.JoinTask(savingModifiers);

            //Task<object[]> SaveModifiersAsync(IEnumerable<IAzureStorageTableEntity<TData>> entities) => entities
            //    .SelectMany(
            //        resultDocument =>
            //        {
            //            return resultDocument.GetType()
            //                .IsSubClassOfGeneric(typeof(IAzureStorageTableEntityBatchable)) ?
            //                    (resultDocument as IAzureStorageTableEntityBatchable)
            //                        .BatchCreateModifiers()
            //                :
            //                    new IBatchModify[] { };
            //        })
            //    .GroupBy(modifier => modifier.GroupingKey)
            //    .Where(grp => grp.Any())
            //    .Select(
            //        async grp =>
            //        {
            //            var modifier = grp.First();
            //            if (!modifier.GroupLimit.HasValue)
            //                return await SaveItemsAsync(grp);

            //            return await grp
            //                .Segment(modifier.GroupLimit.Value)
            //                .Select(items => SaveItemsAsync(items))
            //                .AsyncEnumerable()
            //                .ToArrayAsync();

            //            async Task<object> SaveItemsAsync(IEnumerable<IBatchModify> items) =>
            //                await modifier.CreateOrUpdateAsync(this,
            //                    async (resourceToModify, saveAsync) =>
            //                    {
            //                        var modifiedResource = items.Aggregate(resourceToModify,
            //                            (resource, modifier) =>
            //                            {
            //                                return modifier.Modify(resource);
            //                            });
            //                        await saveAsync(modifiedResource);
            //                        return modifiedResource;
            //                    });
            //        })
            //    .AsyncEnumerable()
            //    .ToArrayAsync();
        }

        public IEnumerableAsync<TResult> CreateOrReplaceBatch<TData, TResult>(IEnumerable<TData> datas,
                Func<ITableEntity, TableResult, TResult> perItemCallback,
                string tableName = default(string),
                RetryDelegate onTimeout = default(RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger),
                int? readAhead = default)
            where TData : IReferenceable
        {
            var table = tableName.HasBlackSpace() ?
                this.TableClient.GetTableReference(tableName)
                :
                GetTable<TData>();

            return datas
                .Select(data => GetEntity(data))
                .Split(x => 100)
                .Select(
                    async rows =>
                    {
                        var rowsToCreate = rows.ToArray();
                        var x = CreateOrReplaceBatch(rowsToCreate,
                                row => row.RowKey,
                                row => row.PartitionKey,
                                perItemCallback, table,
                                out Task<object[]> modifiersTask,
                                onTimeout: onTimeout,
                                diagnostics: diagnostics,
                                readAhead: readAhead)
                            .ToArrayAsync();
                        await modifiersTask;
                        var rowsCreated = await x;
                        return rowsCreated;
                    })
                .AsyncEnumerable(readAhead: 50)
                .SelectMany();
        }

        public IEnumerableAsync<TResult> CreateOrUpdateBatch<TResult>(IEnumerable<ITableEntity> entities,
            Func<ITableEntity, TableResult, TResult> perItemCallback,
            string tableName = default(string),
            CloudTable table = default(CloudTable),
            RetryDelegate onTimeout = default(RetryDelegate),
            EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger))
        {
            if(table.IsDefaultOrNull())
                table = tableName.HasBlackSpace() ?
                    TableClient.GetTableReference(tableName)
                    :
                    default(CloudTable);
            var entitiesToCreate = entities.ToArray();
            return CreateOrReplaceBatch<ITableEntity, TResult>(entitiesToCreate,
                entity => entity.RowKey,
                entity => entity.PartitionKey,
                perItemCallback,
                table: table,
                out Task<object[]> modifiersTask,
                onTimeout: onTimeout,
                diagnostics: diagnostics);
        }

        public IEnumerableAsync<TResult> CreateOrReplaceBatch<TDocument, TResult>(
                TDocument[] entities, // Cannot be IEnumerable<TDocument> since GroupBy will iterate the whole thing
                Func<TDocument, string> getRowKey,
                Func<TDocument, string> getPartitionKey,
                Func<TDocument, TableResult, TResult> perItemCallback,
                CloudTable table,
                out Task<object[]> modifiersTask,
                RetryDelegate onTimeout = default(RetryDelegate),
                EastFive.Analytics.ILogger diagnostics = default(EastFive.Analytics.ILogger),
                int? readAhead = default)
            where TDocument : ITableEntity
        {
            const int raDefault = 100;
            modifiersTask = SaveModifiersAsync();
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
                .Select(grp => CreateOrReplaceBatchAsync(grp.Key, grp.Value, table: table, diagnostics: diagnostics))
                .AsyncEnumerable()
                .SelectMany(
                    trs =>
                    {
                        var collection = trs
                            .Select(
                                tableResult =>
                                {
                                    var resultDocument = (TDocument)tableResult.Result;
                                    var itemResult = perItemCallback(resultDocument, tableResult);
                                    return itemResult;
                                })
                            .ToArray();
                        return collection;
                    });

            Task<object[]> SaveModifiersAsync() => entities
                .SelectMany(
                    resultDocument =>
                    {
                        return resultDocument.GetType()
                            .IsSubClassOfGeneric(typeof(IAzureStorageTableEntityBatchable)) ?
                                (resultDocument as IAzureStorageTableEntityBatchable)
                                    .BatchCreateModifiers()
                            :
                                new IBatchModify[] { };
                    })
                .GroupBy(modifier => modifier.GroupingKey)
                .Where(grp => grp.Any())
                .Select(
                    async grp =>
                    {
                        var modifier = grp.First();
                        if (!modifier.GroupLimit.HasValue)
                            return await SaveItemsAsync(grp);

                        return await grp
                            .Segment(modifier.GroupLimit.Value)
                            .Select(items => SaveItemsAsync(items))
                            .AsyncEnumerable(readAhead: readAhead ?? raDefault)
                            .ToArrayAsync();

                        async Task<object> SaveItemsAsync(IEnumerable<IBatchModify> items) =>
                            await modifier.CreateOrUpdateAsync(this,
                                async (resourceToModify, saveAsync) =>
                                {
                                    var modifiedResource = items.Aggregate(resourceToModify,
                                        (resource, modifier) =>
                                        {
                                            return modifier.Modify(resource);
                                        });
                                    await saveAsync(modifiedResource);
                                    return modifiedResource;
                                });
                    })
                .AsyncEnumerable(readAhead: readAhead ?? raDefault)
                .ToArrayAsync();
        }

        #endregion

        #region DELETE

        public async Task<TResult> DeleteAsync<TData, TResult>(string rowKey, string partitionKey,
            Func<TData, TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<TData, Func<Task>, Task<TResult>> onModified = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure =
                default(Func<ExtendedErrorInformationCodes, string, TResult>),
            RetryDelegate onTimeout = default(RetryDelegate),
            string tableName = default(string))
        {
            var table = tableName.HasBlackSpace() ?
                    this.TableClient.GetTableReference(tableName)
                    :
                    GetTable<TData>();

            return await await FindByIdAsync(rowKey, partitionKey,
                (TData data, TableResult tableResult) =>
                {
                    var entity = GetEntity(data);
                    // entity.ETag = "*";
                    return DeleteAsync<TData, TResult>(entity,
                        () => onSuccess(data),
                        onNotFound,
                        onModified: () =>
                        {
                            return OnModifiedAsync();

                            Task<TResult> OnModifiedAsync()
                            {
                                return onModified(data,
                                    () => DeleteAsync<Task<TResult>>(entity, table,
                                        onSuccess: () => onSuccess(data).AsTask(),
                                        onNotFound: onNotFound.AsAsyncFunc(),
                                        onModified: () => OnModifiedAsync(),
                                        onFailure: onFailure.AsAsyncFunc(),
                                        onTimeout: onTimeout));
                            }
                        },
                        onFailure:onFailure,
                        onTimeout:onTimeout,
                        tableName: tableName);
                },
                onNotFound.AsAsyncFunc(),
                onFailure.AsAsyncFunc(),
                onTimeout: onTimeout,
                table:table);
        }

        public async Task<TResult> DeleteAsync<TEntity, TResult>(string rowKey, string partitionKey,
            Func<TEntity, Func<Task<IAzureStorageTableEntity<TEntity>>>, Task<TResult>> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default,
            string tableName = default)
        {
            return await await FindByIdAsync(rowKey, partitionKey,
                (TEntity entity, TableResult tableResult) =>
                {
                    return onFound(entity,
                        () =>
                        {
                            var data = GetEntity(entity);
                            data.ETag = tableResult.Etag;
                            return DeleteAsync(data,
                                success:() => data,
                                onNotFound:() => data,
                                onFailure:(a, b) => data,
                                onTimeout: onTimeout,
                                tableName: tableName);
                        });
                },
                onNotFound.AsAsyncFunc(),
                onFailure.AsAsyncFunc(),
                onTimeout: onTimeout,
                tableName: tableName);
        }

        public async Task<TResult> DeleteAsync<TData, TResult>(ITableEntity entity,
            Func<TResult> success,
            Func<TResult> onNotFound,
            Func<TResult> onModified,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default,
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
                onModified,
                onFailure,
                onTimeout);
        }

        public async Task<TResult> DeleteAsync<TResult>(string rowKey, string partitionKey, Type typeData,
            Func<ITableEntity, object, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default,
            string tableName = default)
        {
            return await await FindByIdAsync(rowKey, partitionKey, typeData,
                (data) =>
                {
                    // ITableEntity entity = GetEntity(data);
                    var getEntityMethod = typeof(AzureTableDriverDynamic)
                        .GetMethod(nameof(AzureTableDriverDynamic.GetEntity), BindingFlags.Static | BindingFlags.NonPublic);
                    var getEntityTyped = getEntityMethod.MakeGenericMethod(typeData);
                    var entity = (ITableEntity)getEntityTyped.Invoke(null, data.AsArray());
                    entity.ETag = "*";
                    var table = tableName.HasBlackSpace() ?
                        this.TableClient.GetTableReference(tableName)
                        :
                        TableFromEntity(typeData, this.TableClient);
                    return DeleteAsync(entity, table,
                        () => onFound(entity, data),
                        () => onNotFound(),
                        onFailure: onFailure,
                        onTimeout: onTimeout);
                },
                onNotFound.AsAsyncFunc(),
                onFailure.AsAsyncFunc(),
                onTimeout: onTimeout,
                tableName: tableName);
        }

        public async Task<TResult> DeleteAsync<TData, TResult>(IAzureStorageTableEntity<TData> entity,
            Func<TResult> success,
            Func<TResult> onNotFound,
            Func<Task<TResult>> onModified = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default,
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
                var response = await new E5CloudTable(table).ExecuteAsync(delete);
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
                                    if (default(RetryDelegate) == onTimeout)
                                        onTimeout = GetRetryDelegate();
                                    await onTimeout(se.RequestInformation.HttpStatusCode, se,
                                        async () =>
                                        {
                                            timeoutResult = await DeleteAsync<TData, TResult>(entity, 
                                                success:success,
                                                onNotFound:onNotFound,
                                                onFailure:onFailure,
                                                onTimeout: onTimeout);
                                        });
                                    return timeoutResult;
                                }
                            case ExtendedErrorInformationCodes.TableNotFound:
                            case ExtendedErrorInformationCodes.TableBeingDeleted:
                                {
                                    return onNotFound();
                                }
                            case ExtendedErrorInformationCodes.UpdateConditionNotSatisfied:
                                {
                                    if (!onModified.IsDefaultOrNull())
                                        return await onModified();

                                    if (onFailure.IsDefaultOrNull())
                                        throw se;
                                    
                                    return onFailure(errorCode, errorMessage);
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
            RetryDelegate onTimeout = default)
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
                                    var results = await this.DeleteBatchAsync<TData>(
                                        partitionDocsGrp.Key, partitionDocsGrp.ToArray());
                                    return results.Select(tr => result(tr));
                                })
                            .AsyncEnumerable()
                            .SelectMany();
                    })
                .SelectAsyncMany();

        }

        public IEnumerableAsync<TResult> DeleteBatch<TData, TResult>(IEnumerableAsync<TData> documents,
            Func<TableResult, TResult> result,
            RetryDelegate onTimeout = default)
            where TData : IReferenceable
        {
            return documents
                .Select(document => GetEntity(document))
                .Batch()
                .Select(
                    docs =>
                    {
                        return docs
                            .GroupBy(doc => doc.PartitionKey)
                            .Select(
                                async partitionDocsGrp =>
                                {
                                    var results = await this.DeleteBatchAsync<TData>(
                                        partitionDocsGrp.Key, partitionDocsGrp.ToArray());
                                    return results
                                        .Select(
                                            tr =>
                                            {
                                                var itemResult = result(tr);
                                                var modifierResult = tr.Result.GetType()
                                                        .IsSubClassOfGeneric(typeof(IAzureStorageTableEntityBatchable)) ?
                                                    (tr.Result as IAzureStorageTableEntityBatchable).BatchDeleteModifiers()
                                                    :
                                                    new IBatchModify[] { };
                                                return (itemResult, modifierResult);
                                            });
                                })
                            .AsyncEnumerable()
                            .SelectMany();
                    })
                .SelectAsyncMany()
                .OnCompleteAsync(
                    (itemResultAndModifiers) =>
                    {
                        return itemResultAndModifiers
                            .SelectMany(tpl => tpl.modifierResult)
                            .GroupBy(modifier => modifier.GroupingKey)
                            .Where(grp => grp.Any())
                            .Select(
                                async grp =>
                                {
                                    var modifier = grp.First();
                                    if(!modifier.GroupLimit.HasValue)
                                        return await SaveItemsAsync(grp);

                                    return await grp
                                        .Segment(modifier.GroupLimit.Value)
                                        .Select(items => SaveItemsAsync(items))
                                        .AsyncEnumerable()
                                        .ToArrayAsync();

                                    async Task<object> SaveItemsAsync(IEnumerable<IBatchModify> items) =>
                                        await modifier.CreateOrUpdateAsync(this,
                                            async (resourceToModify, saveAsync) =>
                                            {
                                                var modifiedResource = items.Aggregate(resourceToModify,
                                                    (resource, modifier) =>
                                                    {
                                                        return modifier.Modify(resource);
                                                    });
                                                await saveAsync(modifiedResource);
                                                return modifiedResource;
                                            });
                                })
                            .AsyncEnumerable()
                            .ToArrayAsync();
                    })
                .Select(itemResultAndModifiers => itemResultAndModifiers.itemResult);

        }

        public IEnumerableAsync<TResult> DeleteBatch<TData, TResult>(IEnumerable<Guid> documentIds,
            Func<TableResult, TResult> result,
            RetryDelegate onTimeout = default)
            where TData : IReferenceable
        {
            return documentIds
                .Select(subsetId => DeletableEntity<TData>.Delete(subsetId))
                .GroupBy(doc => doc.PartitionKey)
                .Select(
                    async partitionDocsGrp =>
                    {
                        var results = await this.DeleteBatchAsync<TData>(
                            partitionDocsGrp.Key, partitionDocsGrp.ToArray());
                        return results.Select(tr => result(tr));
                    })
                .AsyncEnumerable()
                .SelectMany();
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
                RetryDelegateAsync<Task<TResult>> onTimeout = default(RetryDelegateAsync<Task<TResult>>),
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
                RetryDelegateAsync<Task<TResult>> onTimeout = default(RetryDelegateAsync<Task<TResult>>),
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
            RetryDelegateAsync<Task<TResult>> onTimeout = default,
            Func<TDocument, TDocument> mutateUponLock = default)
            where TDocument : IReferenceable
        {
            if (onTimeout.IsDefaultOrNull())
                onTimeout = GetRetryDelegateContentionAsync<Task<TResult>>();

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
                async (TDocument document, TableResult tableResult) =>
                {
                    var originalDoc = GetEntity(document); // Not a deep, or even shallow, copy in most cases
                    originalDoc.ETag = tableResult.Etag;
                    async Task<TResult> execute()
                    {
                        if (!mutateUponLock.IsDefaultOrNull())
                            document = mutateUponLock(document);
                        // Save document in locked state
                        return await await this.UpdateIfNotModifiedAsync(document,
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

        async Task<BlobClient> GetBlobClientAsync(string containerReference, string blobName)
        {
            if (containerReference.IsNullOrWhiteSpace())
                throw new Exception("Empty containerReference");
            var blobContainerClient = BlobClient.GetBlobContainerClient(containerReference);
            global::Azure.Response<BlobContainerInfo> createResponse = await blobContainerClient.CreateIfNotExistsAsync();
            return blobContainerClient.GetBlobClient(blobName);
        }

        async Task<BlobClient> GetBlobClientAsync(AzureBlobFileSystemUri blobUri)
        {
            var blobContainerClient = BlobClient.GetBlobContainerClient(blobUri.containerName);
            global::Azure.Response<BlobContainerInfo> createResponse = await blobContainerClient.CreateIfNotExistsAsync();
            return blobContainerClient.GetBlobClient(blobUri.path);
        }

        async Task<BlockBlobClient> GetBlockBlobClientAsync(AzureBlobFileSystemUri blobUri)
        {
            var blobContainerClient = BlobClient.GetBlobContainerClient(blobUri.containerName);
            global::Azure.Response<BlobContainerInfo> createResponse = await blobContainerClient.CreateIfNotExistsAsync();
            var blobClient = blobContainerClient.GetBlockBlobClient(blobUri.path);
            return blobClient;
        }

        public Task<TResult> BlobCreateOrUpdateAsync<TResult>(byte[] content, Guid blobId, string containerName,
            Func<TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            string contentDisposition = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default) => BlobCreateOrUpdateAsync(
                    content, blobId.ToString("N"), containerName,
                onSuccess,
                onFailure,
                contentType:contentType,
                contentDisposition:contentDisposition,
                metadata,
                onTimeout);

        public async Task<TResult> BlobCreateOrUpdateAsync<TResult>(byte[] content, string blobName, string containerName,
            Func<TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default, string contentDisposition = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(containerName, blobName);
                using (var stream = new MemoryStream(content))
                {
                    var result = await blockClient.UploadAsync(stream,
                        new global::Azure.Storage.Blobs.Models.BlobUploadOptions
                        {
                            Metadata = metadata,
                            HttpHeaders = new global::Azure.Storage.Blobs.Models.BlobHttpHeaders()
                            {
                                ContentType = contentType,
                                ContentDisposition = contentDisposition,
                            },
                            
                        });
                }
                return onSuccess();
            }
            catch (global::Azure.RequestFailedException ex)
            {
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseStorageException(
                    (errorCode, errorMessage) =>
                        onFailure(errorCode, errorMessage),
                    () => throw ex);
            }
        }

        public async Task<TResult> BlobCreateOrUpdateAsync<TResult>(string blobName, string containerName,
                Func<Stream, Task> writeStreamAsync,
            Func<BlobContentInfo, TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            System.Net.Mime.ContentType contentType = default,
            string contentTypeString = default,
            System.Net.Mime.ContentDisposition contentDisposition = default,
            string contentDispositionString = default,
            string fileName = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(containerName, blobName);
                //var doesExistsTask = await blockClient.ExistsAsync();

                using (var stream = new MemoryStream())
                {
                    await writeStreamAsync(stream);
                    await stream.FlushAsync();
                    stream.Position = 0;
                    var disposition = GetDisposition(contentDisposition:contentDisposition,
                        contentDispositionString:contentDispositionString, fileName:fileName);
                    var contentTypeToUse = GetContentType();
                    //var doesExists = await doesExistsTask;
                    //if(!doesExists)
                    //{
                    //    var result = await blockClient.(stream,
                    //    new global::Azure.Storage.Blobs.Models.BlobUploadOptions
                    //    {
                    //        Metadata = metadata,
                    //        HttpHeaders = new global::Azure.Storage.Blobs.Models.BlobHttpHeaders()
                    //        {
                    //            ContentType = contentTypeToUse,
                    //            ContentDisposition = disposition,
                    //        }
                    //    });
                    //    return onSuccess(result.Value);
                    //}

                    var result = await blockClient.UploadAsync(stream,
                        new global::Azure.Storage.Blobs.Models.BlobUploadOptions
                        {
                            Metadata = metadata,
                            HttpHeaders = new global::Azure.Storage.Blobs.Models.BlobHttpHeaders()
                            {
                                ContentType = contentTypeToUse,
                                ContentDisposition = disposition,
                            }
                        });
                    return onSuccess(result.Value);
                }

                string GetContentType()
                {
                    if (contentType.IsNotDefaultOrNull())
                        return contentType.ToString();
                    return contentTypeString;
                }
            }
            catch (global::Azure.RequestFailedException ex)
            {
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseStorageException(
                    (errorCode, errorMessage) =>
                        onFailure(errorCode, errorMessage),
                    () => throw ex);
            }
        }

        private static string GetDisposition(
            System.Net.Mime.ContentDisposition contentDisposition = default,
            string contentDispositionString = default,
            string fileName = default)
        {
            if (contentDisposition.IsNotDefaultOrNull())
            {
                var contentDispositionSerialized = SerializeDisposition(contentDisposition);
                return contentDispositionSerialized;
            }
            
            if (contentDispositionString.HasBlackSpace())
                return contentDispositionString;

            if (fileName.IsNullOrWhiteSpace())
                return default;
            
            var dispositionCreated = new System.Net.Mime.ContentDisposition();
            dispositionCreated.FileName = fileName;
            var dispositionString = SerializeDisposition(dispositionCreated);
            return dispositionString;
            
            string SerializeDisposition(System.Net.Mime.ContentDisposition contentDisposition)
            {
                // There is a bug where if the filename contains 
                var chars = contentDisposition.FileName.ToCharArray();
                if(chars.Where(c => !char.IsAscii(c)).Any())
                {
                    var bytes = contentDisposition.FileName.GetBytes(Encoding.UTF8);
                    var cleanFileName = Encoding
                        .Convert(Encoding.UTF8, Encoding.ASCII, bytes)
                        .GetString(Encoding.ASCII);
                    contentDisposition.FileName = cleanFileName;
                }
                
                var contentDispositionString = contentDisposition.ToString();
                return contentDispositionString;
            }
        }

        public async Task<TResult> BlobCreateOrUpdateSegmentedAsync<TResult>(AzureBlobFileSystemUri blobUri,
                Func<Stream, Task<bool>> writeSegmentAsync,
            Func<BlobContentInfo, TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            System.Net.Mime.ContentType contentType = default,
            string contentTypeString = default,
            System.Net.Mime.ContentDisposition contentDisposition = default,
            string contentDispositionString = default,
            string fileName = default,
            IDictionary<string, string> metadata = default)
        {
            try
            {
                var blobClient = await GetBlockBlobClientAsync(blobUri);

                var hasMore = true;
                var blockIDArray = await EnumerableAsync
                    .Yield<(string, Task, MemoryStream)>(
                        async (yieldReturn, yieldBreak) =>
                        {
                            if (!hasMore)
                                return yieldBreak;

                            var stream = new MemoryStream();
                            hasMore = await writeSegmentAsync(stream);
                            if (stream.Position != 0)
                            {
                                stream.Position = 0;
                                var blockID = Convert.ToBase64String(
                                Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));

                                var blockWriteTask = blobClient.StageBlockAsync(blockID, stream);
                                return yieldReturn((blockID, blockWriteTask, stream));
                            }
                            return yieldReturn((default, default, default));
                        })
                    .Where(tpl => tpl.Item1.HasBlackSpace())
                    .Select(
                        async blockItemTpl =>
                        {
                            await blockItemTpl.Item2;
                            await blockItemTpl.Item3.DisposeAsync();
                            return blockItemTpl.Item1;
                        })
                    .Await(readAhead:10)
                    .ToArrayAsync();

                
                var disposition = GetDisposition(contentDisposition:contentDisposition,
                        contentDispositionString:contentDispositionString, fileName:fileName);
                var contentTypeToUse = GetContentType();
                var result = await blobClient.CommitBlockListAsync(blockIDArray,
                    new global::Azure.Storage.Blobs.Models.CommitBlockListOptions()
                    {
                        Metadata = metadata,
                        HttpHeaders = new global::Azure.Storage.Blobs.Models.BlobHttpHeaders()
                        {
                            ContentType = contentTypeToUse,
                            ContentDisposition = disposition,
                        }
                    });
                return onSuccess(result.Value);

                string GetContentType()
                {
                    if (contentType.IsNotDefaultOrNull())
                        return contentType.ToString();
                    return contentTypeString;
                }
            }
            catch (global::Azure.RequestFailedException ex)
            {
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseStorageException(
                    (errorCode, errorMessage) =>
                        onFailure(errorCode, errorMessage),
                    () => throw ex);
            }
        }

        public Task<TResult> BlobCreateAsync<TResult>(byte[] content, Guid blobId, string containerName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            System.Net.Mime.ContentDisposition disposition = default,
            string dispositionString = default,
            string fileName = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default) =>
                BlobCreateAsync<TResult>(content, blobId.ToString("N"), containerName,
                    onSuccess, 
                    onAlreadyExists: onAlreadyExists, 
                    onFailure: onFailure,
                        contentType: contentType,
                        disposition: disposition,
                        dispositionString: dispositionString,
                        fileName:fileName,
                        metadata: metadata, onTimeout: onTimeout);

        public async Task<TResult> BlobCreateAsync<TResult>(byte[] content, string blobName, string containerName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            System.Net.Mime.ContentDisposition disposition = default,
            string dispositionString = default,
            string fileName = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(containerName, blobName);

                if (await blockClient.ExistsAsync())
                {
                    if (onAlreadyExists.IsDefault())
                        throw new RecordAlreadyExistsException();
                    return onAlreadyExists();
                }

                using (var stream = new MemoryStream(content))
                {
                    
                    await blockClient.UploadAsync(stream,
                        new global::Azure.Storage.Blobs.Models.BlobUploadOptions
                        {
                            Metadata = metadata,
                            HttpHeaders = new global::Azure.Storage.Blobs.Models.BlobHttpHeaders()
                            {
                                ContentType = contentType,
                                ContentDisposition = GetDisposition(contentDisposition:disposition,
                                    contentDispositionString:dispositionString, fileName:fileName),
                            }
                        });
                }
                return onSuccess();
            }
            catch (global::Azure.RequestFailedException ex)
            {
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseStorageException(
                    (errorCode, errorMessage) =>
                        onFailure(errorCode, errorMessage),
                    () => throw ex);
            }
        }

        public Task<TResult> BlobCreateAsync<TResult>(Stream content, Guid blobId, string containerName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default, string fileName = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default) => BlobCreateAsync(blobId, containerName,
                    stream => content.CopyToAsync(stream),
                onSuccess: onSuccess,
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                contentType: contentType,
                fileName: fileName,
                metadata: metadata,
                onTimeout: onTimeout);

        public Task<TResult> BlobCreateAsync<TResult>(Stream content, string blobName, string containerName,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default, string fileName = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default) => BlobCreateAsync(blobName, containerName,
                    stream => content.CopyToAsync(stream),
                onSuccess: onSuccess,
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                contentType: contentType,
                fileName: fileName,
                metadata: metadata,
                onTimeout: onTimeout);

        public Task<TResult> BlobCreateAsync<TResult>(Guid blobId, string containerName,
                Func<Stream, Task> writeAsync,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default, string fileName = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default) => BlobCreateAsync(
                    blobId.ToString("N"), containerName, writeAsync:writeAsync,
                onSuccess: onSuccess,
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                contentType: contentType,
                fileName: fileName,
                metadata: metadata,
                onTimeout: onTimeout);

        public async Task<TResult> BlobCreateAsync<TResult>(string blobName, string containerName,
                Func<Stream, Task> writeAsync,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default, string fileName = default,
            System.Net.Mime.ContentDisposition disposition = default,
            string dispositionString = default,
            IDictionary<string, string> metadata = default,
            RetryDelegate onTimeout = default)
        {
            var container = this.BlobClient.GetBlobContainerClient(containerName);
            var createResponse = await container.CreateIfNotExistsAsync();
            //global::Azure.ETag created = createResponse.Value.ETag;
            var blockClient = container.GetBlobClient(blobName);
            try
            {
                if (await blockClient.ExistsAsync())
                {
                    if (onAlreadyExists.IsDefault())
                        throw new RecordAlreadyExistsException();
                    return onAlreadyExists();
                }
                using (var stream = new MemoryStream())
                {
                    await writeAsync(stream);
                    stream.Position = 0;
                    global::Azure.Response<BlobContentInfo> response = await blockClient.UploadAsync(stream,
                        new BlobUploadOptions
                        {
                            Metadata = metadata,
                            HttpHeaders = new global::Azure.Storage.Blobs.Models.BlobHttpHeaders()
                            {
                                ContentType = contentType,
                                ContentDisposition = GetDisposition(disposition,
                                    contentDispositionString:dispositionString, fileName:fileName),
                            }
                        });
                }
                return onSuccess();
            }
            catch (global::Azure.RequestFailedException ex)
            {
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseStorageException(
                    (errorCode, errorMessage) =>
                        onFailure(errorCode, errorMessage),
                    () => throw ex);
            }
        }

        public Task<TResult> BlobInformationAsync<TResult>(Guid blobId, string containerName,
            Func<BlobProperties, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default) => BlobInformationAsync(
                    blobId.ToString("N"), containerName,
                onFound,
                onNotFound,
                onFailure,
                onTimeout);

        public async Task<TResult> BlobInformationAsync<TResult>(string blobName, string containerName,
            Func<BlobProperties, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(containerName, blobName);
                var doesExists = await blockClient.ExistsAsync();
                if(!doesExists.Value)
                    return onNotFound();
                var properties = await blockClient.GetPropertiesAsync();
                return onFound(properties.Value);
            }
            catch (global::Azure.RequestFailedException ex)
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

        public async Task<TResult> BlobDeleteIfExistsAsync<TResult>(string containerName, string blobName,
            Func<TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(containerName, blobName);
                return await await BlobInformationAsync(blobName, containerName,
                    async props =>
                    {
                        if(props.Metadata.TryGetValue("hdi_isfolder", out string isFolderStr))
                        {
                            if(bool.TryParse(isFolderStr, out bool isFolder))
                            {
                                if(isFolder)
                                {
                                    var blobContainerClient = this.BlobClient.GetBlobContainerClient(containerName);
                                    var prefix = blobName.EndsWith('/') ?
                                        blobName
                                        :
                                        blobName + '/';
                                    var blobItems = blobContainerClient.GetBlobsAsync(prefix: prefix);
                                    Func<TResult> seed = () => onSuccess();
                                    var getResult = await await blobItems.AggregateAsync(
                                        seed.AsTask(),
                                        async (onResultTask, blobItem) =>
                                        {
                                            var onResult = await onResultTask;
                                            if (blobItem.Name == blobName)
                                                return onResult;

                                            return await BlobDeleteIfExistsAsync(containerName, blobItem.Name,
                                                 () => onResult,
                                                 onFailure: (codes, why) =>
                                                 {
                                                     Func<TResult> onFailureClosed = () => onFailure(codes, why);
                                                     return onFailureClosed;
                                                 });
                                        });
                                    return getResult();
                                }
                            }
                        }
                        var response = await blockClient.DeleteIfExistsAsync(snapshotsOption: DeleteSnapshotsOption.IncludeSnapshots);
                        return onSuccess();
                    },
                    onNotFound: () => onSuccess().AsTask(),
                    onFailure: (codes, msg) =>
                    {
                        if (onFailure.IsDefaultOrNull())
                            throw new Exception();
                        return onFailure(codes, msg).AsTask();
                    });
            }
            catch (global::Azure.RequestFailedException ex)
            {
                return await ex.ParseStorageException(
                    async (errorCode, errorMessage) =>
                    {
                        if(errorCode == ExtendedErrorInformationCodes.OperationNotSupportedOnDirectory)
                        {
                            var blobContainerClient = this.BlobClient.GetBlobContainerClient(containerName);
                            var blobItems = blobContainerClient.GetBlobsAsync(prefix: blobName);
                            await foreach (BlobItem blobItem in blobItems)
                            {
                                if (blobItem.Name == blobName)
                                    continue;
                                BlobClient blobClient = blobContainerClient.GetBlobClient(blobItem.Name);
                                await blobClient.DeleteIfExistsAsync();
                            }
                            return onSuccess();
                        }
                        if (onFailure.IsDefaultOrNull())
                            throw ex;
                        return onFailure(errorCode, errorMessage);
                    },
                    () => throw ex);
            }
        }

        public Task<TResult> BlobLoadBytesAsync<TResult>(Guid blobId, string containerName,
            Func<byte[], string, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default) => BlobLoadBytesAsync(
                    blobId.ToString("N"), containerName,
                (bytes, properties) => onFound(bytes, properties.ContentType),
                onNotFound,
                onFailure: onFailure,
                onTimeout: onTimeout);

        public async Task<TResult> BlobLoadBytesAsync<TResult>(string blobName, string containerName,
            Func<byte[], BlobProperties, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(
                    containerName, blobName);

                if (!await blockClient.ExistsAsync())
                    return onNotFound();

                using (var returnStream = await blockClient.OpenReadAsync(
                    new BlobOpenReadOptions(true)
                    {
                        Conditions = new BlobRequestConditions()
                        {
                        }
                    }))
                {
                    var propertiesTask = blockClient.GetPropertiesAsync();
                    var bytes = await returnStream.ToBytesAsync();
                    var properties = await propertiesTask;
                    return onFound(bytes, properties.Value);
                }
            }
            catch (global::Azure.RequestFailedException ex)
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
            //catch (Exception ex)
            //{
            //    throw ex;
            //}
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
        public Task<TResult> BlobLoadStreamAsync<TResult>(Guid blobId, string containerName,
            Func<System.IO.Stream, string, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default) => BlobLoadStreamAsync(blobId.ToString("N"), containerName,
                (stream, properties) => onFound(stream, properties.ContentType),
                onNotFound,
                onFailure,
                onTimeout);

        public Task<TResult> BlobLoadStreamAsync<TResult>(Guid blobId, string containerName,
            Func<System.IO.Stream, string, IDictionary<string, string>, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default) => BlobLoadStreamAsync(blobId.ToString("N"), containerName,
                (stream, properties) => onFound(stream, properties.ContentType, properties.Metadata),
                onNotFound,
                onFailure,
                onTimeout);

        public async Task<TResult> BlobLoadStreamAsync<TResult>(string blobName, string containerName,
            Func<System.IO.Stream, BlobProperties, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default)
        {
            if (containerName.IsNullOrWhiteSpace())
            {
                if(onNotFound.IsDefaultOrNull())
                    throw new Exception("Empty containerReference");
                return onNotFound();
            }
            try
            {
                var blockClient = await GetBlobClientAsync(
                    containerName, blobName);
                var returnStreamTask = blockClient.OpenReadAsync(
                    new BlobOpenReadOptions(true)
                    {
                        Conditions = new BlobRequestConditions()
                        {
                        }
                    });
                var properties = await blockClient.GetPropertiesAsync();
                var returnStream = await returnStreamTask;
                return onFound(returnStream, properties.Value);
            }
            catch (global::Azure.RequestFailedException ex)
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

        public async Task<TResult> BlobLoadToAsync<TResult>(string blobName, string containerName,
                System.IO.Stream stream,
            Func<BlobProperties, TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(
                    containerName, blobName);
                var responseGetting = blockClient.DownloadToAsync(stream);
                var properties = await blockClient.GetPropertiesAsync();
                var response = await responseGetting;
                var responseStatus = (HttpStatusCode)response.Status;
                if (responseStatus.IsSuccess())
                    return onFound(properties.Value);

                return onNotFound();
            }
            catch (global::Azure.RequestFailedException ex)
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

        public Task<TResult> BlobDeleteIfExistsAsync<TResult>(Guid blobId, string containerName,
            Func<bool, // existed
                TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default) => BlobDeleteIfExistsAsync(blobId.ToString("N"), containerName,
                onSuccess,
                onFailure);

        public async Task<TResult> BlobDeleteIfExistsAsync<TResult>(string blobName, string containerName,
            Func<bool, // existed
                TResult> onSuccess,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(containerName, blobName);
                var result = await blockClient.DeleteIfExistsAsync();
                return onSuccess(result.Value);
            }
            catch (global::Azure.RequestFailedException ex)
            {
                if (onFailure.IsDefaultOrNull())
                    throw;
                return ex.ParseExtendedErrorInformation(
                    (code, msg) => onFailure(code, msg),
                    () => throw ex);
            }
        }

        public async Task<TResult> BlobListFilesAsync<TResult>(string containerName,
            Func<BlobItem[], TResult> onFound,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            RetryDelegate onTimeout = default)
        {
            try
            {
                var containerClient = BlobClient.GetBlobContainerClient(containerName);
                var blobItemsPager = containerClient.GetBlobsAsync();
                var blobItems = await blobItemsPager.ToArrayAsync();
                return onFound(blobItems);
            }
            catch (global::Azure.RequestFailedException ex)
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

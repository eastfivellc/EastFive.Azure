using EastFive.Azure.StorageTables.Driver;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Persistence;
using Newtonsoft.Json;
using EastFive.Api;
using EastFive.Persistence.Azure.StorageTables;
using System.Net.Http;
using EastFive.Azure.Functions;
using EastFive.Analytics;
using EastFive.Web.Configuration;
using BlackBarLabs.Persistence.Azure.Attributes;

namespace EastFive.Azure.Persistence.AzureStorageTables.Backups
{
    [FunctionViewController(
        Route = "TableBackupOperation",
        Resource = typeof(TableBackupOperation),
        ContentType = "x-application/table-backup-operation",
        ContentTypeVersion = "0.1")]
    [StorageResourceNoOp]
    [StorageTable]
    public struct TableBackupOperation : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => tableBackupOperationRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 3)]
        public IRef<TableBackupOperation> tableBackupOperationRef;

        public const string WhenPropertyName = "when";
        [ApiProperty(PropertyName = WhenPropertyName)]
        [JsonProperty(PropertyName = WhenPropertyName)]
        [Storage]
        public DateTime when;

        public const string PreviousPropertyName = "previous";
        [ApiProperty(PropertyName = PreviousPropertyName)]
        [JsonProperty(PropertyName = PreviousPropertyName)]
        [Storage]
        public IRefOptional<TableBackupOperation> previous;

        public const string TableBackupPropertyName = "table_backup";
        [ApiProperty(PropertyName = TableBackupPropertyName)]
        [JsonProperty(PropertyName = TableBackupPropertyName)]
        [Storage]
        public IRef<TableBackup> backup;

        public const string OperationSetPropertyName = "operation_set";
        [ApiProperty(PropertyName = OperationSetPropertyName)]
        [JsonProperty(PropertyName = OperationSetPropertyName)]
        [Storage]
        [StringStandardPartitionLookup]
        public string operationSet;

        [Storage]
        [JsonIgnore]
        public string continuationToken;

        [Storage]
        [JsonIgnore]
        public long rowsCopied;

        [Storage]
        [JsonIgnore]
        public Guid etagsBlobId;

        #endregion

        #region Http Methods

        [HttpPost]
        public static async Task<IHttpResponse> CreateAsync(
                [Property(Name = IdPropertyName)]IRef<TableBackupOperation> tableBackupOperationRef,
                [Property(Name = WhenPropertyName)]DateTime when,
                [Property(Name = TableBackupPropertyName)]IRef<TableBackup> tableBackupRef,
                [Property(Name = OperationSetPropertyName)]string operationSet,
                [Resource]TableBackupOperation tableBackup,
                RequestMessage<TableBackupOperation> requestQuery,
                IHttpRequest request,
                EastFive.Analytics.ILogger logger,
            CreatedBodyResponse<InvocationMessage> onCreated,
            AlreadyExistsResponse onAlreadyExists)
        {
            return await await tableBackup.StorageCreateAsync(
                async (discard) =>
                {
                    var invocationMessage = await requestQuery
                        .ById(tableBackupOperationRef)
                        .HttpPatch(default)
                        .CompileRequest(request)
                        .FunctionAsync();

                    logger.Trace($"Invocation[{invocationMessage.id}] will begin backup operation `{operationSet}`.");
                    return onCreated(invocationMessage);
                },
                () => onAlreadyExists().AsTask());
        }

        [HttpPatch]
        public static async Task<IHttpResponse> UpdateAsync(
                [UpdateId(Name = IdPropertyName)]IRef<TableBackup> tableBackupRef,
                RequestMessage<TableBackup> requestQuery,
                IHttpRequest request,
                EastFive.Analytics.ILogger logger,
            CreatedBodyResponse<InvocationMessage> onContinued,
            NoContentResponse onComplete,
            NotFoundResponse onNotFound)
        {
            return await await tableBackupRef.StorageGetAsync(
                async entity =>
                {
                    return await await entity.backup.StorageGetAsync(
                        async (repoBackup) =>
                        {
                        var invocationMaybe = await entity.Copy(
                            repoBackup.storageSettingCopyFrom,
                            repoBackup.storageSettingCopyTo,
                            requestQuery,
                            request,
                            logger);
                        if (invocationMaybe.HasValue)
                            return onContinued(invocationMaybe.Value);

                        return onComplete();

                        //var complete = await entity.Copy(
                        //    repoBackup.storageSettingCopyFrom,
                        //    repoBackup.storageSettingCopyTo,
                        //    TimeSpan.FromSeconds(seconds),
                        //    logger);
                        //if (complete)
                        //    return onComplete();

                        //var invocationMessage = await requestQuery
                        //    .ById(tableBackupRef)
                        //    .HttpPatch(default)
                        //    .CompileRequest(request)
                        //    .FunctionAsync();

                        //return onContinued(invocationMessage);
                        });
                },
                () => onNotFound().AsTask());
        }

        [HttpPatch]
        public static async Task<IHttpResponse> UpdateAsync(
                [UpdateId(Name = IdPropertyName)]IRef<TableBackup> tableBackupRef,
                [Property]string continuationToken,
                RequestMessage<TableBackup> requestQuery,
                IHttpRequest request,
                EastFive.Analytics.ILogger logger,
            CreatedBodyResponse<InvocationMessage> onContinued,
            NoContentResponse onComplete,
            NotFoundResponse onNotFound)
        {
            return await await tableBackupRef.StorageGetAsync(
                async entity =>
                {
                    return await await entity.backup.StorageGetAsync(
                        async (repoBackup) =>
                        {
                            var invocationMaybe = await entity.Copy(
                                repoBackup.storageSettingCopyFrom,
                                repoBackup.storageSettingCopyTo,
                                requestQuery,
                                request,
                                logger);
                            if (invocationMaybe.HasValue)
                                return onContinued(invocationMaybe.Value);

                            return onComplete();
                        });
                },
                () => onNotFound().AsTask());
        }

        #endregion

        #region Copy Functions

        public struct RowData
        {
            public string eTag;
            public string rowKey;
            public string partitionKey;
        }

        public async Task<TResult> Copy<TResult>(
            string tableName,
            string storageSettingCopyFrom,
            string storageSettingCopyTo,
            TimeSpan limit,
            EastFive.Analytics.ILogger logger,
            Dictionary<string, KeyValuePair<string, string>> existingRows,
            Func<List<RowData>, TResult> onComplete,
            Func<List<RowData>, TResult> onIncomplete)
        {
            var cloudStorageFromAccount = CloudStorageAccount.Parse(storageSettingCopyFrom);
            var cloudStorageToAccount = CloudStorageAccount.Parse(storageSettingCopyTo);
            var cloudStorageFromClient = cloudStorageFromAccount.CreateCloudTableClient();
            var cloudStorageToClient = cloudStorageToAccount.CreateCloudTableClient();

            var tableFrom = cloudStorageFromClient.GetTableReference(tableName);
            var tableTo = cloudStorageToClient.GetTableReference(tableName);
            var query = new TableQuery<GenericTableEntity>();

            var token = default(TableContinuationToken);
            if (continuationToken.HasBlackSpace())
            {
                token = JsonConvert.DeserializeObject<TableContinuationToken>(continuationToken);
                //token = new TableContinuationToken();
                //var tokenReader = XmlReader.Create(new StringReader(continuationToken));
                //token.ReadXml(tokenReader);
            }

            var timer = Stopwatch.StartNew();

            var segmentFetching = tableFrom.ExecuteQuerySegmentedAsync(query, token);
            var resultsProcessing = new TableResult[] { }.AsTask();
            var backoff = TimeSpan.FromSeconds(1.0);


            var recordsProcessed = new List<RowData>();
            while (true)
            {
                try
                {
                    if (segmentFetching.IsDefaultOrNull())
                    {
                        var savedResultsFinal = await resultsProcessing;
                        logger.Trace($"Wrote {savedResultsFinal.Length} records [{tableName}]");
                        recordsProcessed.AddRange(savedResultsFinal
                            .Select(
                                result =>
                                {
                                    var entity = result.Result as GenericTableEntity;
                                    return new RowData
                                    {
                                        eTag = entity.ETag,
                                        rowKey = entity.RowKey,
                                        partitionKey = entity.PartitionKey,
                                    };
                                }));

                        bool saved = await this.tableBackupOperationRef.StorageUpdateAsync(
                            async (backup, saveAsync) =>
                            {
                                backup.continuationToken = default;
                                backup.rowsCopied = recordsProcessed.Count;
                                await saveAsync(backup);
                                return true;
                            },
                            () => false);
                        logger.Trace($"Table complete: {recordsProcessed.Count} total records [{tableName}]");
                        return onComplete(recordsProcessed);
                    }

                    var segment = await segmentFetching;
                    var newResults = segment.Results
                        .Where(result => !existingRows.ContainsKey(result.ETag))
                        .ToArray();

                    var resultsProcessingNext = CreateOrReplaceBatch(newResults, tableTo);

                    token = segment.ContinuationToken;
                    if (timer.Elapsed > limit)
                    {
                        var secondToLast = await resultsProcessing;
                        logger.Trace($"Wrote {secondToLast.Length} records [{tableName}]");
                        recordsProcessed.AddRange(secondToLast
                            .Select(
                                result =>
                                {
                                    var entity = result.Result as GenericTableEntity;
                                    return new RowData
                                    {
                                        eTag = entity.ETag,
                                        rowKey = entity.RowKey,
                                        partitionKey = entity.PartitionKey,
                                    };
                                }));

                        var lastWrite = await resultsProcessingNext;
                        logger.Trace($"Wrote {lastWrite.Length} records [{tableName}]");
                        recordsProcessed.AddRange(lastWrite
                            .Select(
                                result =>
                                {
                                    var entity = result.Result as GenericTableEntity;
                                    return new RowData
                                    {
                                        eTag = entity.ETag,
                                        rowKey = entity.RowKey,
                                        partitionKey = entity.PartitionKey,
                                    };
                                }));

                        var tokenToSave = string.Empty;
                        if (!token.IsDefaultOrNull())
                        {
                            tokenToSave = JsonConvert.SerializeObject(token);
                            //using (var writer = new StringWriter())
                            //{
                            //    using (var xmlWriter = XmlWriter.Create(writer))
                            //    {
                            //        token.WriteXml(xmlWriter);
                            //    }
                            //    tokenToSave = writer.ToString();
                            //}
                        }

                        bool saved = await this.tableBackupOperationRef.StorageUpdateAsync(
                            async (backup, saveAsync) =>
                            {
                                backup.continuationToken = tokenToSave;
                                backup.rowsCopied = recordsProcessed.Count;
                                await saveAsync(backup);
                                return true;
                            },
                            () => false);
                        logger.Trace($"Token Saved = {saved}, token = `{tokenToSave}`");

                        logger.Trace($"Table will be continued: {recordsProcessed.Count} partial records [{tableName}]");
                        return onIncomplete(recordsProcessed);
                    }

                    segmentFetching = token.IsDefaultOrNull() ?
                        default
                        :
                        tableFrom.ExecuteQuerySegmentedAsync(query, token);

                    var saveResults = await resultsProcessing;
                    if (saveResults.Length != 0)
                    {
                        logger.Trace($"Wrote {saveResults.Length} records [{tableName}]");
                        recordsProcessed.AddRange(saveResults
                            .Select(
                                result =>
                                {
                                    var entity = result.Result as GenericTableEntity;
                                    return new RowData
                                    {
                                        eTag = entity.ETag,
                                        rowKey = entity.RowKey,
                                        partitionKey = entity.PartitionKey,
                                    };
                                }));
                    }

                    resultsProcessing = resultsProcessingNext;
                    if (backoff != TimeSpan.FromSeconds(1.0))
                    {
                        backoff = TimeSpan.FromSeconds(1.0);
                        logger.Trace($"Adjusted backoff to {backoff.TotalSeconds} seconds");
                    }
                    continue;
                }
                catch (StorageException storageEx)
                {
                    if (storageEx.IsProblemTimeout())
                    {
                        backoff = backoff + TimeSpan.FromSeconds(1.0);
                        logger.Trace($"Adjusted backoff to {backoff.TotalSeconds} seconds and pausing");
                        await Task.Delay(backoff);
                        segmentFetching = token.IsDefaultOrNull() ?
                            default
                            :
                            tableFrom.ExecuteQuerySegmentedAsync(query, token);
                        continue;
                    }
                }
                catch (Exception e)
                {
                    logger.Warning(JsonConvert.SerializeObject(e));
                    throw;
                };
            }
        }

        private static async Task<TableResult[]> CreateOrReplaceBatch(GenericTableEntity[] entities,
                CloudTable table)
        {
            return await entities
                .GroupBy(row => row.PartitionKey)
                .SelectMany(
                    grp =>
                    {
                        return grp
                            .Split(index => 100)
                            .Select(set => set.ToArray());
                    })
                .Select(grp => CreateOrReplaceBatchAsync(grp, table: table))
                .AsyncEnumerable()
                .SelectMany()
                .ToArrayAsync();
        }

        private static async Task<TableResult[]> CreateOrReplaceBatchAsync(GenericTableEntity[] entities,
            CloudTable table)
        {
            if (!entities.Any())
                return new TableResult[] { };

            var batch = new TableBatchOperation();
            var rowKeyHash = new HashSet<string>();
            foreach (var row in entities)
            {
                if (rowKeyHash.Contains(row.RowKey))
                {
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
                        () => true);
                    if (shouldRetry)
                        continue;

                }
            }
        }

        #endregion
    }
}

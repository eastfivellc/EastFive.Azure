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
using System.Collections.Concurrent;
using EastFive.Azure.Auth;
using EastFive.Api.Auth;

namespace EastFive.Azure.Persistence.AzureStorageTables.Backups
{
    [FunctionViewController(
        Route = "TableBackup",
        ContentType = "x-application/table-backup",
        ContentTypeVersion = "0.1")]
    [StorageResourceNoOp]
    [StorageTable]
    public struct TableBackup : IReferenceable
    {
        private static readonly TimeSpan maxDuration = TimeSpan.FromSeconds(90);
        private static readonly int maxRows = 150_000;
        private static readonly TableQuery<GenericTableEntity> query = new TableQuery<GenericTableEntity>();

        #region Properties

        [JsonIgnore]
        public Guid id => tableBackupRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 3)]
        public IRef<TableBackup> tableBackupRef;

        public const string WhenPropertyName = "when";
        [ApiProperty(PropertyName = WhenPropertyName)]
        [JsonProperty(PropertyName = WhenPropertyName)]
        [Storage]
        public DateTime when;

        [Storage]
        [JsonIgnore]
        public string continuationToken;

        public const string TableNamePropertyName = "table_name";
        [ApiProperty(PropertyName = TableNamePropertyName)]
        [JsonProperty(PropertyName = TableNamePropertyName)]
        [Storage]
        public string tableName;

        public const string BackupPropertyName = "backup";
        [ApiProperty(PropertyName = BackupPropertyName)]
        [JsonProperty(PropertyName = BackupPropertyName)]
        [Storage]
        public IRef<RepositoryBackup> backup;

        [Storage]
        [JsonIgnore]
        public long rowsCopied;

        [Storage]
        [JsonIgnore]
        public Guid etagsBlobId;

        private bool StillScanning => continuationToken.HasBlackSpace();

        #endregion

        #region Http Methods

        [RequiredClaim(
            System.Security.Claims.ClaimTypes.Role,
            ClaimValues.Roles.SuperAdmin)]
        [HttpPost]
        public static async Task<IHttpResponse> CreateAsync(
                [Property(Name = IdPropertyName)]IRef<TableBackup> tableBackupRef,
                [Property(Name = WhenPropertyName)]DateTime when,
                [Property(Name = TableNamePropertyName)]string tableName,
                [Property(Name = BackupPropertyName)]IRef<RepositoryBackup> repositoryBackupRef,
                [Resource]TableBackup tableBackup,
                RequestMessage<TableBackup> requestQuery,
                IHttpRequest request,
                EastFive.Api.Security security,
                EastFive.Analytics.ILogger logger,
            CreatedBodyResponse<InvocationMessage> onCreated,
            AlreadyExistsResponse onAlreadyExists)
        {
            return await await tableBackup.StorageCreateAsync(
                async (discard) =>
                {
                    var invocationMessage = await requestQuery
                        .ById(tableBackupRef)
                        .HttpPatch(default)
                        .CompileRequest(request)
                        .FunctionAsync();

                    logger.Trace($"Invocation[{invocationMessage.id}] will next backup table `{tableBackup.tableName}`.");
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
                        });
                },
                () => onNotFound().AsTask());
        }

        #endregion

        #region Copy Functions

        public async Task<InvocationMessage?> Copy(
            string storageSettingCopyFrom,
            string storageSettingCopyTo,
            RequestMessage<TableBackup> requestQuery,
            IHttpRequest request,
            ILogger logger)
        {
            var watch = new Stopwatch();
            watch.Start();

            var cloudStorageFromAccount = CloudStorageAccount.Parse(storageSettingCopyFrom);
            var cloudStorageToAccount = CloudStorageAccount.Parse(storageSettingCopyTo);
            var cloudStorageFromClient = cloudStorageFromAccount.CreateCloudTableClient();
            var cloudStorageToClient = cloudStorageToAccount.CreateCloudTableClient();

            var tableFrom = cloudStorageFromClient.GetTableReference(tableName);
            var tableTo = cloudStorageToClient.GetTableReference(tableName);

            var token = default(TableContinuationToken);
            if (StillScanning)
            {
                token = JsonConvert.DeserializeObject<TableContinuationToken>(continuationToken);
                //token = new TableContinuationToken();
                //var tokenReader = XmlReader.Create(new StringReader(continuationToken));
                //token.ReadXml(tokenReader);
            }

            var segmentFetching = tableFrom.ExecuteQuerySegmentedAsync(query, token);
            var backoff = TimeSpan.FromSeconds(1.0);

            var completeMutex = new System.Threading.ManualResetEvent(false);
            var rowList = new ConcurrentBag<GenericTableEntity>();
            //var listLock = new object();
            //var rowList = new List<GenericTableEntity>();

            var readingData = ReadData(this);
            var writingData = Task.Run(() =>  WriteData());
            continuationToken = await readingData;
            var tokenClosure = continuationToken;
            long aggr = await this.tableBackupRef.StorageUpdateAsync(
                async (backup, saveAsync) =>
                {
                    backup.continuationToken = tokenClosure;
                    backup.rowsCopied += rowList.Count;
                    await saveAsync(backup);
                    logger.Trace($"token saved [{backup.tableName}]");
                    return backup.rowsCopied;
                },
                () => 0L);

            completeMutex.Set();
            await writingData;

            // dispatch after write is finished so that the system doesn't get progressively more loaded
            var invoMsg = StillScanning ?
                await requestQuery
                    .ById(tableBackupRef)
                    .HttpPatch(default)
                    .CompileRequest(request)
                    .FunctionAsync()
                :
                default(InvocationMessage?);

            if (invoMsg.IsDefault())
                logger.Trace($"Table complete: {aggr} total records [{tableName}]");
            else
                logger.Trace($"Table will be continued: {aggr} partial records [{tableName}]");

            return invoMsg;

            async Task<string> ReadData(TableBackup tableBackup)
            {
                while (true)
                {
                    try
                    {
                        if (segmentFetching.IsDefaultOrNull())
                            return default;

                        var segment = await segmentFetching;
                        foreach (var item in segment.Results)
                            rowList.Add(item);

                        //lock (listLock)
                        //{
                        //    rowList.AddRange(segment.Results);
                        //    readCount += segment.Results.Count;
                        //}

                        token = segment.ContinuationToken;
                        if (watch.Elapsed >= maxDuration || rowList.Count >= maxRows) // some tables read quicker b/c they don't have as much data per row so sometimes we read more than can be written in a single function duration
                        {
                            if (token.IsDefaultOrNull())
                                return default;

                            logger.Trace($"{rowList.Count} rows read [{tableBackup.tableName}]");
                            
                            return JsonConvert.SerializeObject(token);
                            //using (var writer = new StringWriter())
                            //{
                            //    using (var xmlWriter = XmlWriter.Create(writer))
                            //    {
                            //        token.WriteXml(xmlWriter);
                            //    }
                            //    return writer.ToString();
                            //}
                        }

                        segmentFetching = token.IsDefaultOrNull() ?
                            default
                            :
                            tableFrom.ExecuteQuerySegmentedAsync(query, token);
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
                    }
                }
            }

            async Task WriteData()
            {
                // TODO: Interleave writes to improve performance
                while (true)
                {
                    var entities = new List<GenericTableEntity>();
                    //bool finished = completeMutex.WaitOne(TimeSpan.FromSeconds(0.1));
                    bool finished = completeMutex.WaitOne();

                    while (rowList.TryTake(out GenericTableEntity result))
                        entities.Add(result);

                    //lock (listLock)
                    //{
                    //    entities = rowList.ToArray();
                    //    rowList.Clear();
                    //}
                    if(entities.IsDefaultNullOrEmpty())
                    {
                        if (finished)
                            return;
                        continue;
                    }
                    try
                    {
                        TableResult[] resultsProcessingNext = await CreateOrReplaceBatch(entities.ToArray(), tableTo);
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
        }

        //public async Task<bool> Copy(
        //    string storageSettingCopyFrom,
        //    string storageSettingCopyTo,
        //    TimeSpan limit,
        //    EastFive.Analytics.ILogger logger)
        //{
        //    var cloudStorageFromAccount = CloudStorageAccount.Parse(storageSettingCopyFrom);
        //    var cloudStorageToAccount = CloudStorageAccount.Parse(storageSettingCopyTo);
        //    var cloudStorageFromClient = cloudStorageFromAccount.CreateCloudTableClient();
        //    var cloudStorageToClient = cloudStorageToAccount.CreateCloudTableClient();

        //    var tableFrom = cloudStorageFromClient.GetTableReference(tableName);
        //    var tableTo = cloudStorageToClient.GetTableReference(tableName);
        //    var query = new TableQuery<GenericTableEntity>();

        //    var token = default(TableContinuationToken);
        //    if (continuationToken.HasBlackSpace())
        //    {
        //        token = new TableContinuationToken();
        //        var tokenReader = XmlReader.Create(new StringReader(continuationToken));
        //        token.ReadXml(tokenReader);
        //    }

        //    var timer = Stopwatch.StartNew();

        //    var segmentFetching = tableFrom.ExecuteQuerySegmentedAsync(query, token);
        //    var resultsProcessing = new TableResult[] { }.AsTask();
        //    var backoff = TimeSpan.FromSeconds(1.0);
        //    while (true)
        //    {
        //        try
        //        {
        //            if (segmentFetching.IsDefaultOrNull())
        //            {
        //                var savedResultsFinal = await resultsProcessing;
        //                logger.Trace($"Wrote {savedResultsFinal.Length} records [{tableName}]");
        //                rowsCopied += savedResultsFinal.Length;

        //                var rowsCopiedClosure = rowsCopied;
        //                bool saved = await this.tableBackupRef.StorageUpdateAsync(
        //                    async (backup, saveAsync) =>
        //                    {
        //                        backup.continuationToken = default;
        //                        backup.rowsCopied = rowsCopiedClosure;
        //                        await saveAsync(backup);
        //                        return true;
        //                    },
        //                    () => false);
        //                logger.Trace($"Table complete: {rowsCopied} total records [{tableName}]");
        //                return true;
        //            }
        //            var segment = await segmentFetching;
        //            var priorResults = segment.Results.ToArray();

        //            var resultsProcessingNext = CreateOrReplaceBatch(priorResults, tableTo);

        //            token = segment.ContinuationToken;
        //            if (timer.Elapsed > limit)
        //            {
        //                var secondToLast = await resultsProcessing;
        //                logger.Trace($"Wrote {secondToLast.Length} records [{tableName}]");
        //                rowsCopied += secondToLast.Length;

        //                var lastWrite = await resultsProcessingNext;
        //                logger.Trace($"Wrote {lastWrite.Length} records [{tableName}]");
        //                rowsCopied += lastWrite.Length;

        //                var tokenToSave = string.Empty;
        //                if (!token.IsDefaultOrNull())
        //                {
        //                    using (var writer = new StringWriter())
        //                    {
        //                        using (var xmlWriter = XmlWriter.Create(writer))
        //                        {
        //                            token.WriteXml(xmlWriter);
        //                        }
        //                        tokenToSave = writer.ToString();
        //                    }
        //                }
        //                var rowsCopiedClosure = rowsCopied;
        //                bool saved = await this.tableBackupRef.StorageUpdateAsync(
        //                    async (backup, saveAsync) =>
        //                    {
        //                        backup.continuationToken = tokenToSave;
        //                        backup.rowsCopied = rowsCopiedClosure;
        //                        await saveAsync(backup);
        //                        return true;
        //                    },
        //                    () => false);
        //                logger.Trace($"Token Saved = {saved}, token = `{tokenToSave}`");

        //                logger.Trace($"Table will be continued: {rowsCopied} partial records [{tableName}]");
        //                return false;
        //            }

        //            segmentFetching = token.IsDefaultOrNull() ?
        //                default
        //                :
        //                tableFrom.ExecuteQuerySegmentedAsync(query, token);
                    
        //            var saveResults = await resultsProcessing;
        //            if (saveResults.Length != 0)
        //            {
        //                logger.Trace($"Wrote {saveResults.Length} records [{tableName}]");
        //                rowsCopied += saveResults.Length;
        //            }

        //            resultsProcessing = resultsProcessingNext;
        //            if (backoff != TimeSpan.FromSeconds(1.0))
        //            {
        //                backoff = TimeSpan.FromSeconds(1.0);
        //                logger.Trace($"Adjusted backoff to {backoff.TotalSeconds} seconds");
        //            }
        //            continue;
        //        }
        //        catch (StorageException storageEx)
        //        {
        //            if (storageEx.IsProblemTimeout())
        //            {
        //                backoff = backoff + TimeSpan.FromSeconds(1.0);
        //                logger.Trace($"Adjusted backoff to {backoff.TotalSeconds} seconds and pausing");
        //                await Task.Delay(backoff);
        //                segmentFetching = token.IsDefaultOrNull() ?
        //                    default
        //                    :
        //                    tableFrom.ExecuteQuerySegmentedAsync(query, token);
        //                continue;
        //            }
        //        }
        //        catch (Exception e)
        //        {
        //            logger.Warning(JsonConvert.SerializeObject(e));
        //            throw;
        //        };
        //    }
        //}

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

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
    [FunctionViewController6(
        Route = "TableBackup",
        Resource = typeof(TableBackup),
        ContentType = "x-application/table-backup",
        ContentTypeVersion = "0.1")]
    [StorageResourceNoOp]
    [StorageTable]
    public struct TableBackup : IReferenceable
    {
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

        #endregion

        #region Http Methods

        [HttpPost]
        public static async Task<HttpResponseMessage> CreateAsync(
                [Property(Name = IdPropertyName)]IRef<TableBackup> tableBackupRef,
                [Property(Name = WhenPropertyName)]DateTime when,
                [Property(Name = TableNamePropertyName)]string tableName,
                [Property(Name = BackupPropertyName)]IRef<RepositoryBackup> repositoryBackupRef,
                [Resource]TableBackup tableBackup,
                RequestMessage<TableBackup> requestQuery,
                HttpRequestMessage request,
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
        public static async Task<HttpResponseMessage> UpdateAsync(
                [UpdateId(Name = IdPropertyName)]IRef<TableBackup> tableBackupRef,
                RequestMessage<TableBackup> requestQuery,
                HttpRequestMessage request,
                EastFive.Analytics.ILogger logger,
            CreatedBodyResponse<InvocationMessage> onContinued,
            NoContentResponse onComplete,
            NotFoundResponse onNotFound)
        {
            return await await tableBackupRef.StorageGetAsync(
                async entity =>
                {
                    return await await entity.backup.StorageGetAsync(
                        repoBackup =>
                        {
                            return EastFive.Azure.Persistence.AppSettings.Backup.SecondsGivenToCopyRows.ConfigurationDouble(
                                async (seconds) =>
                                {
                                    var complete = await entity.Copy(
                                        repoBackup.storageSettingCopyFrom,
                                        repoBackup.storageSettingCopyTo,
                                        TimeSpan.FromSeconds(seconds),
                                        logger);
                                    if (complete)
                                        return onComplete();

                                    var invocationMessage = await requestQuery
                                        .ById(tableBackupRef)
                                        .HttpPatch(default)
                                        .CompileRequest(request)
                                        .FunctionAsync();

                                    return onContinued(invocationMessage);
                                },
                                (why) => onComplete().AsTask());
                        });
                },
                () => onNotFound().AsTask());
        }

        #endregion

        #region Copy Functions

        public async Task<bool> Copy(
            string storageSettingCopyFrom,
            string storageSettingCopyTo,
            TimeSpan limit,
            EastFive.Analytics.ILogger logger)
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
                token = new TableContinuationToken();
                var tokenReader = XmlReader.Create(new StringReader(continuationToken));
                token.ReadXml(tokenReader);
            }

            var timer = Stopwatch.StartNew();

            var segmentFetching = tableFrom.ExecuteQuerySegmentedAsync(query, token);
            var resultsProcessing = new TableResult[] { }.AsTask();
            var backoff = TimeSpan.FromSeconds(1.0);
            while (true)
            {
                try
                {
                    if (segmentFetching.IsDefaultOrNull())
                    {
                        var savedResultsFinal = await resultsProcessing;
                        logger.Trace($"Wrote {savedResultsFinal.Length} records [{tableName}]");
                        rowsCopied += savedResultsFinal.Length;

                        var rowsCopiedClosure = rowsCopied;
                        bool saved = await this.tableBackupRef.StorageUpdateAsync(
                            async (backup, saveAsync) =>
                            {
                                backup.continuationToken = default;
                                backup.rowsCopied = rowsCopiedClosure;
                                await saveAsync(backup);
                                return true;
                            },
                            () => false);
                        logger.Trace($"Table complete: {rowsCopied} total records [{tableName}]");
                        return true;
                    }
                    var segment = await segmentFetching;
                    var priorResults = segment.Results.ToArray();

                    var resultsProcessingNext = CreateOrReplaceBatch(priorResults, tableTo);

                    token = segment.ContinuationToken;
                    if (timer.Elapsed > limit)
                    {
                        var secondToLast = await resultsProcessing;
                        logger.Trace($"Wrote {secondToLast.Length} records [{tableName}]");
                        rowsCopied += secondToLast.Length;

                        var lastWrite = await resultsProcessingNext;
                        logger.Trace($"Wrote {lastWrite.Length} records [{tableName}]");
                        rowsCopied += lastWrite.Length;

                        var tokenToSave = string.Empty;
                        if (!token.IsDefaultOrNull())
                        {
                            using (var writer = new StringWriter())
                            {
                                using (var xmlWriter = XmlWriter.Create(writer))
                                {
                                    token.WriteXml(xmlWriter);
                                }
                                tokenToSave = writer.ToString();
                            }
                        }
                        var rowsCopiedClosure = rowsCopied;
                        bool saved = await this.tableBackupRef.StorageUpdateAsync(
                            async (backup, saveAsync) =>
                            {
                                backup.continuationToken = tokenToSave;
                                backup.rowsCopied = rowsCopiedClosure;
                                await saveAsync(backup);
                                return true;
                            },
                            () => false);
                        logger.Trace($"Token Saved = {saved}, token = `{tokenToSave}`");

                        logger.Trace($"Table will be continued: {rowsCopied} partial records [{tableName}]");
                        return false;
                    }

                    segmentFetching = token.IsDefaultOrNull() ?
                        default
                        :
                        tableFrom.ExecuteQuerySegmentedAsync(query, token);
                    
                    var saveResults = await resultsProcessing;
                    if (saveResults.Length != 0)
                    {
                        logger.Trace($"Wrote {saveResults.Length} records [{tableName}]");
                        rowsCopied += saveResults.Length;
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

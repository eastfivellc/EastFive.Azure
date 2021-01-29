using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Linq;
using System.Threading.Tasks;
using EastFive.Persistence;
using Newtonsoft.Json;
using EastFive.Api;
using EastFive.Persistence.Azure.StorageTables;
using System.Net.Http;
using EastFive.Azure.Functions;
using EastFive.Api.Azure;
using EastFive.Analytics;
using EastFive.Persistence.Azure.StorageTables.Driver;
using System.Collections.Concurrent;
using BlackBarLabs.Persistence.Azure.Attributes;
using EastFive.Azure.Persistence.StorageTables.Backups;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.Auth;
using EastFive.Api.Auth;

namespace EastFive.Azure.Persistence.AzureStorageTables.Backups
{
    [FunctionViewController(
        Route = "RepositoryBackup",
        ContentType = "x-application/repository-backup",
        ContentTypeVersion = "0.1")]
    [StorageResourceNoOp]
    [StorageTable]
    public struct RepositoryBackup : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => repositoryBackupRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 2)]
        public IRef<RepositoryBackup> repositoryBackupRef;

        public const string WhenPropertyName = "when";
        [ApiProperty(PropertyName = WhenPropertyName)]
        [JsonProperty(PropertyName = WhenPropertyName)]
        [Storage]
        public DateTime when;

        public const string FrequencyPropertyName = "frequency";
        [ApiProperty(PropertyName = FrequencyPropertyName)]
        [JsonProperty(PropertyName = FrequencyPropertyName)]
        [Storage]
        public string frequency;

        public const string StorageSettingCopyFromPropertyName = "storage_setting_copy_from";
        [ApiProperty(PropertyName = StorageSettingCopyFromPropertyName)]
        [JsonProperty(PropertyName = StorageSettingCopyFromPropertyName)]
        [Storage]
        public string storageSettingCopyFrom;

        public const string StorageSettingCopyToPropertyName = "storage_setting_copy_to";
        [ApiProperty(PropertyName = StorageSettingCopyToPropertyName)]
        [JsonProperty(PropertyName = StorageSettingCopyToPropertyName)]
        [Storage]
        public string storageSettingCopyTo;

        #endregion

        #region Http Methods

        [RequiredClaim(
            System.Security.Claims.ClaimTypes.Role,
            ClaimValues.Roles.SuperAdmin)]
        [HttpPost]
        public static async Task<IHttpResponse> QueueUpBackupPartitions(
                [Property(Name = IdPropertyName)]IRef<RepositoryBackup> repositoryBackupRef,
                [Property(Name = StorageSettingCopyFromPropertyName)]string storageSettingCopyFrom,
                [Property(Name = StorageSettingCopyToPropertyName)]string storageSettingCopyTo,
                [Resource]RepositoryBackup repositoryBackup,
                AzureApplication application,
                EastFive.Api.Security security,
                RequestMessage<TableBackup> requestQuery,
                IHttpRequest request,
                EastFive.Analytics.ILogger logger,
            MultipartAsyncResponse<InvocationMessage> onQueued,
            AlreadyExistsResponse onAlreadyExists)
        {
            CloudStorageAccount account = CloudStorageAccount
                    .Parse(storageSettingCopyFrom);
            CloudTableClient tableClient =
                new CloudTableClient(account.TableEndpoint, account.Credentials);

            return await repositoryBackup.StorageCreateAsync(
                (discard) =>
                {
                    var includedTables = BackupFunction.DiscoverStorageResources()
                        .Where(x => x.message.Any())
                        .Select(x => x.tableName)
                        .ToArray();

                    var resourceInfoToProcess = tableClient.GetTables()
                        .Where(table => includedTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase))
                        .Distinct()
                        .Select(
                            async cloudTable =>
                            {
                                var tableBackup = new TableBackup()
                                {
                                    tableBackupRef = Ref<TableBackup>.NewRef(),
                                    backup = repositoryBackupRef,
                                    tableName = cloudTable.Name,
                                    when = DateTime.UtcNow,
                                };
                                var invocationMessage = await requestQuery
                                    .HttpPost(tableBackup)
                                    .CompileRequest(request)
                                    .FunctionAsync();

                                logger.Trace($"Invocation[{invocationMessage.id}] will backup table `{tableBackup.tableName}`.");
                                return invocationMessage;
                            })
                        .Await(readAhead:10);
                    return onQueued(resourceInfoToProcess);
                },
                () => onAlreadyExists());
        }

        [HttpPatch]
        public static async Task<IHttpResponse> QueueUpBackupPartitions(
                [Property(Name = IdPropertyName)]IRef<RepositoryBackup> repositoryBackupRef,
                [Property(Name = WhenPropertyName)]DateTime when,
                RequestMessage<TableBackup> requestQuery,
                IHttpRequest request,
                EastFive.Analytics.ILogger logger,
            MultipartAsyncResponse<InvocationMessage> onQueued,
            NoContentResponse onTooEarly,
            NotFoundResponse onNotFound)
        {
            return await repositoryBackupRef.StorageUpdateAsync(
                async (repoBack, saveAsync) =>
                {
                    var needsToRun = NCrontab.CrontabSchedule.TryParse(repoBack.frequency,
                        chronSchedule =>
                        {
                            var next = chronSchedule.GetNextOccurrence(repoBack.when);
                            if (when > next)
                                return true;
                            return false;
                        },
                        ex =>
                        {
                            return false;
                        });
                    if (!needsToRun)
                        return onTooEarly();

                    var includedTables = BackupFunction.DiscoverStorageResources()
                        .Where(x => x.message.Any())
                        .Select(x => x.tableName)
                        .ToArray();

                    CloudStorageAccount account = CloudStorageAccount
                        .Parse(repoBack.storageSettingCopyFrom);
                    CloudTableClient tableClient =
                        new CloudTableClient(account.TableEndpoint, account.Credentials);

                    var tables = tableClient.GetTables();
                    var resourceInfoToProcess = tables
                        .Where(table => includedTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase))
                        .Distinct()
                        .Select(
                            async cloudTable =>
                            {
                                var tableBackup = new TableBackup()
                                {
                                    tableBackupRef = Ref<TableBackup>.NewRef(),
                                    backup = repositoryBackupRef,
                                    tableName = cloudTable.Name,
                                    when = DateTime.UtcNow,
                                };
                                var invocationMessage = await requestQuery
                                    .HttpPost(tableBackup)
                                    .CompileRequest(request)
                                    .FunctionAsync();

                                logger.Trace($"Invocation[{invocationMessage.id}] will backup table `{tableBackup.tableName}`.");
                                return invocationMessage;
                            })
                        .Await(readAhead:10);
                    repoBack.when = when;
                    await saveAsync(repoBack);

                    return onQueued(resourceInfoToProcess);


                },
                () => onNotFound());
        }

        #endregion

        #region Utility Functions

        //public static Task DeleteAllAsync(AzureTableDriverDynamic destRepo)
        //{
        //    var cloudTable = destRepo.TableClient.GetTableReference(typeof(StorageTables.Backups.BackupFunction.Backup).Name);
        //    return cloudTable.DeleteIfExistsAsync();
        //}

        // Azure recommends static variables to reuse them across invokes
        // and reduce overall connection count
        private static readonly ConcurrentDictionary<string, AzureTableDriverDynamic> repositories = new ConcurrentDictionary<string, AzureTableDriverDynamic>();

        internal static AzureTableDriverDynamic GetRepository(string connectionString)
        {
            if (!repositories.TryGetValue(connectionString, out AzureTableDriverDynamic repository))
            {
                repository = AzureTableDriverDynamic.FromStorageString(connectionString);
                if (!repositories.TryAdd(connectionString, repository))
                {
                    repositories.TryGetValue(connectionString, out repository);
                }
            }
            return repository;
        }

        #endregion
    }
}

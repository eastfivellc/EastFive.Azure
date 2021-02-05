using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Persistence.StorageTables.Backups
{
    public struct TableMessage
    {
        public string sourceConnectionString;
        public string destConnectionString;
        public string tableName;
        public WhereInformation where;
    }

    public struct TableInformation
    {
        public string assembly;
        public string document;
    }

    public struct StorageResourceInfo
    {
        public string tableName;
        public WhereInformation[] message;
        public string sortKey;
    }

    public static class BackupFunction
    {
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


        //[StorageTable]
        //[StorageResourceNoOp]
        //public struct Backup : IReferenceable
        //{
        //    [JsonIgnore]
        //    public Guid id => backupRef.id;

        //    [RowKey]
        //    [StandardParititionKey]
        //    public IRef<Backup> backupRef;

        //    [Storage]
        //    public string resource;

        //    [Storage]
        //    public string filter;

        //    [Storage]
        //    public int? count;

        //    [Storage]
        //    public double? seconds;

        //    public static IRef<Backup> GetBackupRef(string resource, string filter)
        //    {
        //        return $"{resource}/{filter}".MD5HashGuid().AsRef<Backup>();
        //    }

        //    public static Task CreateOrUpdateAsync(AzureTableDriverDynamic destRepo, string resource, string filter, int? count = default, double? seconds = default)
        //    {
        //        var backupRef = GetBackupRef(resource, filter);
        //        return destRepo.UpdateOrCreateAsync<Backup, Backup>(
        //                backupRef.StorageComputeRowKey(),
        //                backupRef.StorageComputePartitionKey(),
        //            async (created, storageToUpdate, saveAsync) =>
        //            {
        //                storageToUpdate.resource = resource;
        //                storageToUpdate.filter = filter;
        //                storageToUpdate.count = count;
        //                storageToUpdate.seconds = seconds;
        //                await saveAsync(storageToUpdate);
        //                return storageToUpdate;
        //            });
        //    }
        //}

        public static TResult GetBackupDestConnectionString<TResult>(
            Func<string, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            string GetDestinationKey()
            {
                var now = DateTime.Now;
                if (now.Day == 1)
                {
                    if ((1 == now.Month) || (4 == now.Month) || (7 == now.Month) || (10 == now.Month))
                        return EastFive.Azure.Persistence.AppSettings.Backup.QuarterDestination;

                    return EastFive.Azure.Persistence.AppSettings.Backup.MonthDestination;
                }
                switch(now.DayOfWeek.ToString())
                {
                    case "Sunday":
                        return EastFive.Azure.Persistence.AppSettings.Backup.SundayDestination;
                    case "Monday":
                        return EastFive.Azure.Persistence.AppSettings.Backup.MondayDestination;
                    case "Tuesday":
                        return EastFive.Azure.Persistence.AppSettings.Backup.TuesdayDestination;
                    case "Wednesday":
                        return EastFive.Azure.Persistence.AppSettings.Backup.WednesdayDestination;
                    case "Thursday":
                        return EastFive.Azure.Persistence.AppSettings.Backup.ThursdayDestination;
                    case "Friday":
                        return EastFive.Azure.Persistence.AppSettings.Backup.FridayDestination;
                    default: //Saturday
                        return EastFive.Azure.Persistence.AppSettings.Backup.SaturdayDestination;
                }
            }

            return EastFive.Web.Configuration.Settings.GetString(GetDestinationKey(),
                (connString) => onSuccess(connString),
                (why) =>
                {
                    return EastFive.Azure.Persistence.AppSettings.Backup.DefaultDestination.ConfigurationString(
                        (connString) => onSuccess(connString),
                        onFailure);
                });
        }

        //public static async Task ProcessBackupPartitionsAsync(TableMessage message, EastFive.Analytics.ILogger logger)
        //{
        //    logger.Trace($"Starting backup for table {message.tableName}");
        //    var sw = new Stopwatch();
        //    sw.Start();

        //    var table = message.tableName;
        //    var sourceRepo = GetRepository(message.sourceConnectionString);
        //    var destRepo = GetRepository(message.destConnectionString);
        //    var filter = message.where.FormatWhereInformation()
        //        .Join(" and ");
        //    await Backup.CreateOrUpdateAsync(destRepo, table, filter);
        //    var completedRows = await sourceRepo.Copy(filter, table, destRepo)
        //        .ToArrayAsync();
        //    sw.Stop();

        //    await Backup.CreateOrUpdateAsync(destRepo, table, filter, completedRows.Length, sw.Elapsed.TotalSeconds);
        //    logger.Trace($"copied {completedRows.Length} rows in {sw.Elapsed.TotalSeconds} seconds for {filter}");
        //}

        //public static async Task QueueUpBackupPartitions(string serviceBusQueueName, string sourceConnectionString, string destConnectionString,
        //    AzureApplication application, EastFive.Analytics.ILogger logger)
        //{
        //    logger.Trace($"Cleaning backup results");
        //    var repo = AzureTableDriverDynamic.FromStorageString(destConnectionString);
        //    await Backup.DeleteAllAsync(GetRepository(destConnectionString));

        //    var resourceInfoToProcess = DiscoverAllStorageResources()
        //        .Distinct(info => info.tableName);

        //    var backupMessages = resourceInfoToProcess
        //        .SelectMany(
        //            resourceInfo =>
        //            {
        //                logger.Trace($"Queuing backup messages for table {resourceInfo.tableName}");
        //                return resourceInfo.message.Select(
        //                    whereInfo =>
        //                    {
        //                        var message = new TableMessage
        //                        {
        //                            destConnectionString = destConnectionString,
        //                            sourceConnectionString = sourceConnectionString,
        //                            tableName = resourceInfo.tableName,
        //                            where = whereInfo,
        //                        };
        //                        var backupMessage = JsonConvert.SerializeObject(message);
        //                        return backupMessage;
        //                    });
                        
        //            });

        //    await application.SendServiceBusMessageAsync(serviceBusQueueName, backupMessages);
            
        //    var totalMessages = backupMessages.Count();
        //    logger.Trace($"Total of {totalMessages} messages queued for all tables");

        //    var storageResources = resourceInfoToProcess
        //        .Select(sri => sri.tableName)
        //        .ToArray();

        //    await ThrowIfTableIsMissingStorageResourceAttribute(sourceConnectionString, storageResources);
        //}

        private static async Task ThrowIfTableIsMissingStorageResourceAttribute(string sourceConnectionString, string[] configuredTables)
        {
            var configuredTableNames = configuredTables
                .Select(configuredTable => configuredTable.ToLower())
                .ToArray();

            var queryResult = await GetRepository(sourceConnectionString)
                .TableClient
                .ListTablesSegmentedAsync(null);
            var tablesMissingAttribute = queryResult
                .Results
                .Select(queryTable => queryTable.Name.ToLower())
                .Where(queryName => !configuredTableNames.Contains(queryName))
                .ToArray();

            if (tablesMissingAttribute.Any())
                throw new Exception($"These tables in production not configured for backup! [{tablesMissingAttribute.Join(",")}]");
        }

        public static IEnumerable<StorageResourceInfo> DiscoverStorageResources()
        {
            return EastFive.Azure.Persistence.AppSettings.Backup.StorageResourceAssemblies.ConfigurationString(
                (assemblyString) =>
                {
                    var assemblyNames = assemblyString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var infos = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => assemblyNames.Contains(a.ManifestModule.Name))
                        .SelectMany(a => a.GetTypes())
                        .SelectMany(
                            (t) =>
                            {
                                return Yield();
                                IEnumerable<StorageResourceInfo> Yield()
                                {
                                    var backupStorageTypeAttrs = t.GetAttributesInterface<IBackupStorageType>();
                                    if (backupStorageTypeAttrs.Any())
                                    {
                                        var backupStorageTypeAttr = backupStorageTypeAttrs.First();
                                        foreach (var storageResourceInfo in backupStorageTypeAttr.GetStorageResourceInfos(t))
                                            yield return storageResourceInfo;
                                    }
                                    var memberResults = t
                                        .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                                        .SelectMany(
                                            memberInfo =>
                                            {
                                                return memberInfo
                                                    .GetAttributesInterface<IBackupStorageMember>(inherit: true, multiple: true)
                                                    .SelectMany(
                                                        backupStorageMemberAttr =>
                                                        {
                                                            return backupStorageMemberAttr.GetStorageResourceInfos(memberInfo);
                                                        });
                                            });
                                    foreach (var memberResult in memberResults)
                                        yield return memberResult;
                                }
                            })
                        .OrderBy(x => x.sortKey);

                    return infos;
                },
                (why) => throw new Exception(why));
        }

        private static string EnsureTableExistsInCode(TableInformation table)
        {
            return table.document;
            var documentType = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.ManifestModule.Name == table.assembly)
                .SelectMany(a => a.GetTypes())
                .Where(t => t.FullName == table.document)
                .FirstOrDefault();
            if (documentType == null)
                throw new Exception($"table name {table.document} not found in code");

            return documentType.Name.ToLower();
        }
    }
}

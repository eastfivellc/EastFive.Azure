using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Analytics;
using EastFive.Api.Azure;
using EastFive.Web.Configuration;
using EastFive.Azure.Persistence.Attributes;

namespace EastFive.Azure.Persistence.StorageTables.Backups
{
    public static class Containers
    {
        public struct ContainerMessage
        {
            public string sourceConnectionString;
            public string destConnectionString;
            public string name;
            public string prefix;
        }

        public static async Task QueueUpBackupContainers(string serviceBusQueueName, string sourceConnectionString, string destConnectionString,
            AzureApplication application, EastFive.Analytics.ILogger logger)
        {
            var containerResources = DiscoverAllContainers();

            var blocksSent = await containerResources
                .Where(info => !ignoreContainerNames.Contains(info.name.ToLower()))
                .Select(
                    async (info) =>
                    {
                        var sw = new Stopwatch();
                        sw.Start();
                        logger.Trace($"Queuing backup messages for container {info.name}");
                        var backupMessages = GeneratePrefixInformation(info.prefixGenerator)
                            .Select(
                                (prefix) =>
                                {
                                    return new ContainerMessage
                                    {
                                        sourceConnectionString = sourceConnectionString,
                                        destConnectionString = destConnectionString,
                                        name = info.name,
                                        prefix = prefix,
                                    };
                                })
                            .Select(message => JsonConvert.SerializeObject(message))
                            .Select(message => Encoding.UTF8.GetBytes(message))
                            .ToArray();
                        await application.SendServiceBusMessageAsync(serviceBusQueueName, backupMessages);
                        sw.Stop();
                        logger.Trace($"{backupMessages.Length} messages queued for {info.name} in {sw.Elapsed.TotalSeconds} seconds");
                        GC.Collect();
                        return backupMessages.Length;
                    })
                .AsyncEnumerable() //.Throttle(desiredRunCount: 2)
                .ToArrayAsync();

            var totalMessages = blocksSent.Sum();
            logger.Trace($"Total of {totalMessages} messages queued for all containers");

            await ThrowIfContainerIsMissingStorageResourceAttribute(sourceConnectionString, containerResources);
        }

        private static readonly string[] ignoreContainerNames =
            new[]
            {
                "azure-webjobs-hosts",
                "azure-webjobs-secrets",
                "spa",
            };


        private static IEnumerable<string> GeneratePrefixInformation(PrefixGenerator prefixGenerator)
        {
            var prefixes = prefixGenerator.GetPrefixes().ToArray();

            // if no prefixes, return an empty prefix to get everything
            if (!prefixes.Any())
                return new[] { string.Empty };

            return prefixes;
        }

        public struct ContainerResourceInfo
        {
            public string name;
            public PrefixGenerator prefixGenerator;
            public string sortKey;
        }

        private static ContainerResourceInfo[] DiscoverAllContainers()
        {
            return EastFive.Azure.Persistence.AppSettings.Backup.StorageResourceAssemblies.ConfigurationString(
                (assemblyString) =>
                {
                    var assemblyNames = assemblyString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var infos = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => assemblyNames.Contains(a.ManifestModule.Name))
                        .SelectMany(a => a.GetTypes())
                        .Where(t => t.CustomAttributes
                            .Any(a => a.AttributeType == typeof(StorageResourceAttribute)))
                        .Select(
                            (t) =>
                            {
                                return t.GetCustomAttributes<ContainerResourceAttribute>()
                                    .Select(
                                        attr =>
                                        {
                                            return new ContainerResourceInfo
                                            {
                                                name = attr.Name,
                                                prefixGenerator = attr.PrefixGenerator.Invoke(),
                                                sortKey = attr.Name,
                                            };
                                        });
                            })
                        .SelectMany()
                        .OrderBy(x => x.sortKey)
                        .ToArray();

                    return infos;
                },
                (why) => throw new Exception(why));
        }


        private static async Task ThrowIfContainerIsMissingStorageResourceAttribute(string sourceConnectionString,
            ContainerResourceInfo[] configuredContainers)
        {
            var configuredNames = configuredContainers
                .Select(configuredContainer => configuredContainer.name.ToLower())
                .ToArray();

            var queryResult = await BackupFunction.GetRepository(sourceConnectionString)
                .BlobClient
                .GetBlobContainersAsync()
                .AsPages()
                .ToListAsync();
            var containersMissingAttribute = queryResult
                .SelectMany(x => x.Values)
                .Select(queryContainer => queryContainer.Name.ToLower())
                //.Where(queryName => !ignoreContainerNames.Contains(queryName))
                .Where(queryName => !configuredNames.Contains(queryName))
                .ToArray();

            if (containersMissingAttribute.Any())
                throw new Exception($"These containers in production not configured for backup! [{containersMissingAttribute.Join(",")}]");
        }
    }
}

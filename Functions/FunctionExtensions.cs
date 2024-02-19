using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Collections.Generic;

using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Models;

using EastFive;
using EastFive.Linq;
using EastFive.Collections;
using EastFive.Collections.Generic;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.Blobs;
using EastFive.Extensions;
using EastFive.Analytics;
using EastFive.Linq.Async;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Azure.Search.Documents.Models;
using EastFive.Serialization.Text;

namespace EastFive.Azure.Functions
{
    public static class FunctionExtensions
    {
        public static IEnumerableAsync<(T, string)> SelectWhereNotAlreadyRunning<T>(this IEnumerableAsync<T> resources,
            IDurableOrchestrationClient orchestrationClient,
            Func<T, string> getInstanceId,
            Microsoft.Extensions.Logging.ILogger log = default,
            int? readAhead = default(int?))
        {
            var tpls = resources
                .Select(
                    async resource =>
                    {
                        var instanceId = getInstanceId(resource);
                        var status = await orchestrationClient.GetStatusAsync(instanceId: instanceId);
                        if (status.IsDefaultOrNull())
                        {
                            if (log.IsNotDefaultOrNull())
                                log.LogInformation($"Provider {instanceId} is Fresh");
                            return (true, resource, instanceId);
                        }
                        if (status.RuntimeStatus == OrchestrationRuntimeStatus.Pending)
                        {
                            if (log.IsNotDefaultOrNull())
                                log.LogInformation($"Provider {instanceId} is Pending");
                            return (false, resource, instanceId);
                        }
                        if (status.RuntimeStatus == OrchestrationRuntimeStatus.Running)
                        {
                            if (log.IsNotDefaultOrNull())
                                log.LogInformation($"Provider {instanceId} is Running");
                            return (false, resource, instanceId);
                        }

                        if (log.IsNotDefaultOrNull())
                            log.LogInformation($"Provider {instanceId} is `{Enum.GetName(status.RuntimeStatus)}`");
                        return (true, resource, instanceId);
                    });

            if (readAhead.HasValue)
                return tpls
                    .Await(readAhead: readAhead.Value)
                    .SelectWhere();

            return tpls.Await().SelectWhere();
        }

        public static IEnumerableAsync<BlobItem> GetDatalakeFiles(this IExportFromDatalake import)
        {
            var practiceFolder = import.exportFolder;
            if (import.exportContainer.IsNullOrWhiteSpace())
                return EnumerableAsync.Empty<BlobItem>();
            return import.exportContainer.BlobFindFilesAsync(
                    practiceFolder, fileSuffix: ".parquet",
                    connectionStringConfigKey: EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString)
                .FoldTask();
        }

        public static async Task<TResult> PopulateInstanceAsync<TInstance, TResult>(this IExportFromDatalake import, TInstance instance,
                IDurableOrchestrationClient starter, string nameOfFunctionToRun,
            Func<TInstance, TResult> onPopulated,
            Func<string, TResult> onFailure)
            where TInstance : DataLakeImportInstance
        {
            try
            {
                var now = DateTime.UtcNow;
                instance.exportContainer = import.exportContainer;
                instance.exportFolder = import.exportFolder;
                instance.when = now;
                instance.cancelled = false;
                instance.ignoreFaultedFiles = false;
                instance.sourceId = import.sourceId;
                var instanceIdToTry = $"{instance.id}:{now.Year}{now.Month}{now.Day}{now.Hour}{now.Minute}{now.Second}";
                string instanceId = await starter.StartNewAsync(nameOfFunctionToRun,
                    instanceIdToTry, instance);
                instance.instance = instanceId;

                return await instance.StorageCreateAsync(
                    (result) =>
                    {
                        return onPopulated(result.Entity);
                    },
                    onAlreadyExists: () =>
                    {
                        return onFailure($"ID is already in use.");
                    });
            }
            catch (Exception ex)
            {
                return onFailure(ex.Message);
            }
        }

        public static async Task<DataLakeImportReport> DataLakeIngestAsync<TResource, TImportInstance, TDataLakeItem>(
            this IDurableActivityContext context,
            Func<TImportInstance, TDataLakeItem, TResource, (string path, string container), int, Task<(bool, string)>> processAsync,
            Microsoft.Extensions.Logging.ILogger log)
            where TImportInstance : DataLakeImportInstance
            where TDataLakeItem : DataLakeItem
        {
            return await await IngestAndProduceReportAsync<TDataLakeItem, TResource, TImportInstance, Task<DataLakeImportReport>>(context, processAsync,
                onProcessed:async (report) =>
                {
                    try
                    {
                        return await report.StorageCreateAsync(
                            result => result.Entity,
                            onAlreadyExists: () =>
                            {
                                log.LogCritical($"Duplicate report:{report.export}, {report.instanceId}");
                                return report;
                            });
                    }
                    catch (Exception ex)
                    {
                        if (log.IsNotDefaultOrNull())
                            log.LogError(ex.Message);
                        return new DataLakeImportReport
                        {
                            status = DataLakeImportStatus.FaultedInstance,
                        };
                    }
                },
                onParsingIssue: () =>
                {
                    return new DataLakeImportReport
                    {
                        status = DataLakeImportStatus.FaultedInstance,
                    }.AsTask();
                },
                onException: (ex) =>
                {
                    return new DataLakeImportReport
                    {
                        status = DataLakeImportStatus.FaultedInstance,
                    }.AsTask();
                },
                log);
            
        }

        private static async Task<TResult> IngestAndProduceReportAsync<TDataLakeItem, TResource, TImportInstance, TResult>(
                IDurableActivityContext context,
                Func<TImportInstance, TDataLakeItem, TResource, (string path, string container), int, Task<(bool, string)>> processAsync,
            Func<DataLakeImportReport, TResult> onProcessed,
            Func<TResult> onParsingIssue,
            Func<Exception, TResult> onException,
                Microsoft.Extensions.Logging.ILogger log)
            where TDataLakeItem : DataLakeItem
            where TImportInstance : DataLakeImportInstance
        {
            try
            {
                var instanceId = context.InstanceId.Replace(':', '_');
                var (dataLakeImportInstance, dataLakeItem) = context.GetInput<(TImportInstance, TDataLakeItem)>();
                if (dataLakeImportInstance.IsDefaultOrNull() || dataLakeItem.IsDefaultOrNull())
                    return onParsingIssue();

                var path = dataLakeItem.path;
                var skip = dataLakeItem.skip;


                var isCancelled = await dataLakeImportInstance.id.AsRef<TImportInstance>().StorageGetAsync(
                    onFound: (updatedInstance) => updatedInstance.cancelled,
                    onDoesNotExists:() => true);
                if (isCancelled)
                {
                    var report = dataLakeItem
                        .GenerateReport(dataLakeImportInstance.exportFromDataLake, DataLakeImportStatus.Cancelled, 0);
                    return onProcessed(report);
                }

                var containerName = dataLakeImportInstance.exportContainer;
                var folder = dataLakeImportInstance.exportFolder;

                var intervals = new (int, int)[] { };
                if (folder.IsNullOrWhiteSpace())
                {
                    var report = dataLakeItem.GenerateReport(dataLakeImportInstance.exportFromDataLake, DataLakeImportStatus.FaultyExport, 0);
                    return onProcessed(report);
                }
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                var timeout = TimeSpan.FromMinutes(4);
                try
                {
                    var ingestedReport = await dataLakeImportInstance.DataLakeIngestFileAsync(dataLakeItem,
                        processAsync: (TResource res, int index) => processAsync(dataLakeImportInstance, dataLakeItem, res, (path, containerName), index),
                        onFileIngested: (report) =>
                        {
                            return report;
                        },
                        onFileNotFound: () =>
                        {
                            var report = dataLakeItem.GenerateReport(
                                dataLakeImportInstance.exportFromDataLake, DataLakeImportStatus.FaultedFile, 0);
                            return report;
                        },
                        isTimedOut: () =>
                        {
                            return startTime.Elapsed > timeout;
                        },
                        log: log);
                    return onProcessed(ingestedReport);
                }
                catch (Exception ex)
                {
                    log.LogError(ex.Message);
                    log.LogError(ex.StackTrace);

                    var report = dataLakeItem.GenerateReport(
                        dataLakeImportInstance.exportFromDataLake, DataLakeImportStatus.FaultedFile, 0);
                    return onProcessed(report);
                }
            }
            catch (Newtonsoft.Json.JsonReaderException parsingException)
            {
                log.LogError(parsingException.Message);
                log.LogError("Bad resource input -- discarding");
                return onParsingIssue();
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);
                return onException(ex);
            }
        }

        public static Task<TResult> ListExportedFilesAsync<TResult>(this IExportFromDatalake dataLakeExported,
            Func<BlobItem[], TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<Persistence.StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string extension = default,
            AzureTableDriverDynamic.RetryDelegate onTimeout = null,
            string connectionStringConfigKey = EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString)
        {
            return dataLakeExported.exportContainer.BlobListFilesAsync(
                files =>
                {
                    var folderFiles = files
                        .Where(
                            file =>
                            {
                                if (!file.Name.StartsWith(dataLakeExported.exportFolder))
                                    return false;

                                if (extension.IsNullOrWhiteSpace())
                                    return true;

                                if (file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                                        return true;

                                return false;
                            })
                        .ToArray();
                    return onSuccess(folderFiles);
                },
                onNotFound:() => onNotFound(),
                    connectionStringConfigKey: connectionStringConfigKey);
        }

        public static TDataLakeItem[] ConvertToDataLakeItems<TDataLakeItem>(this BlobItem[] blobItems,
            Guid dataLakeInstanceId, DataLakeImportReport [] priorRuns,
            bool shouldRunErrors,
            Func<Guid, string, int, int[], TDataLakeItem> constructItem)
            where TDataLakeItem : DataLakeItem
        {
            return blobItems
                .Select(
                    item =>
                    {
                        var priorRun = priorRuns
                            .NullToEmpty()
                            .Where(
                                pr => String.Equals(pr.path, item.Name))
                            .ToArray();

                        var didComplete = DataLakeImportReport.DidComplete(priorRun,
                            out var skip, out var missingLines, out var priorErrors);

                        if (DoesNotNeedToRun(out var linesToRun))
                            return null;

                        return constructItem(dataLakeInstanceId, item.Name, skip, linesToRun);

                        bool DoesNotNeedToRun(out int[] linesToRun)
                        {
                            if (shouldRunErrors)
                            {
                                if (priorErrors.Any())
                                {
                                    linesToRun = missingLines.Concat(priorErrors).ToArray();
                                    return linesToRun.None();
                                }
                            }
                            linesToRun = missingLines;
                            return didComplete;
                        }
                    })
                .SelectWhereNotNull()
                .ToArray();
        }

        public static async Task<DataLakeItem[]> ConvertToDataLakeItemsAsync(this BlobItem[] blobItems,
            Guid dataLakeImportId, Guid importId, DateTime? usePriorRunsFromMaybe, bool shouldRunErrors)
        {
            var priorRuns = usePriorRunsFromMaybe.HasValue ?
                    await DataLakeImportReport.GetFromStorage(importId, usePriorRunsFromMaybe.Value)
                        .ToArrayAsync()
                    :
                    new DataLakeImportReport[] { };
            return blobItems.ConvertToDataLakeItems(
                dataLakeImportId, priorRuns, shouldRunErrors,
                (dataLakeInstanceId, path, skip, linesToRun) =>
                {
                    return new DataLakeItem
                    {
                        dataLakeInstance = dataLakeInstanceId,
                        path = path,
                        skip = skip,
                    }
                        .SetLinesToRun(linesToRun);
                });
        }

        public static async Task<TDataLakeItem[]> ConvertToDataLakeItemsAsync<TDataLakeItem>(this BlobItem[] blobItems,
            Guid dataLakeImportId, Guid importId, DateTime? usePriorRunsFromMaybe, bool shouldRunErrors,
            Func<Guid, string, int, int[], TDataLakeItem> constructItem)
            where TDataLakeItem : DataLakeItem
        {
            var priorRuns = usePriorRunsFromMaybe.HasValue ?
                    await DataLakeImportReport.GetFromStorage(importId, usePriorRunsFromMaybe.Value)
                        .ToArrayAsync()
                    :
                    new DataLakeImportReport[] { };
            return blobItems.ConvertToDataLakeItems(
                dataLakeImportId, priorRuns, shouldRunErrors,
                constructItem);
        }

        public static async Task<DataLakeImportReport> RunActivityFromDurableFunctionAsync<TImportInstance, TDataLakeItem>(this IDurableOrchestrationContext context,
            string functionName,
            Microsoft.Extensions.Logging.ILogger log)
            where TImportInstance : DataLakeImportInstance
            where TDataLakeItem : IDataLakeItem
        {
            //log.LogInformation($"[{context.InstanceId}/{context.Name}]...Starting");
            var (input, dataLakeItem) = context.GetInput<(TImportInstance, TDataLakeItem)>();
            while (true)
            {
                var report = await context.CallActivityAsync<DataLakeImportReport>(
                    functionName, (input, dataLakeItem));
                var status = report.status;
                dataLakeItem.skip = report.GetLastLineProcessed();
                var linesProcessed = report.GetTotalLinesProcessed();
                
                if (status == DataLakeImportStatus.Running || status == DataLakeImportStatus.Partial)
                    continue;

                log.LogInformation($"[{input.instance}/{dataLakeItem.path}]...Finishing status ({status}) with `{linesProcessed}` records");
                return report;
            }
        }
    }
}
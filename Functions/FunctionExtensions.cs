using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask;

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
using EastFive.Serialization.Text;
using EastFive.Configuration;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Serialization.Json;

namespace EastFive.Azure.Functions
{
    public static class FunctionExtensions
    {
        // public static IEnumerableAsync<(T, string)> SelectWhereNotAlreadyRunning<T>(this IEnumerableAsync<T> resources,
        //     DurableTaskClient orchestrationClient,
        //     Func<T, string> getInstanceId,
        //     Microsoft.Extensions.Logging.ILogger log = default,
        //     int? readAhead = default(int?))
        // {
        //     var tpls = resources
        //         .Select(
        //             async resource =>
        //             {
        //                 var instanceId = getInstanceId(resource);
        //                 var status = await orchestrationClient.GetStatusAsync(instanceId: instanceId);
        //                 if (status.IsDefaultOrNull())
        //                 {
        //                     if (log.IsNotDefaultOrNull())
        //                         log.LogInformation($"Provider {instanceId} is Fresh");
        //                     return (true, resource, instanceId);
        //                 }
        //                 if (status.RuntimeStatus == OrchestrationRuntimeStatus.Pending)
        //                 {
        //                     if (log.IsNotDefaultOrNull())
        //                         log.LogInformation($"Provider {instanceId} is Pending");
        //                     return (false, resource, instanceId);
        //                 }
        //                 if (status.RuntimeStatus == OrchestrationRuntimeStatus.Running)
        //                 {
        //                     if (log.IsNotDefaultOrNull())
        //                         log.LogInformation($"Provider {instanceId} is Running");
        //                     return (false, resource, instanceId);
        //                 }

        //                 if (log.IsNotDefaultOrNull())
        //                     log.LogInformation($"Provider {instanceId} is `{Enum.GetName(status.RuntimeStatus)}`");
        //                 return (true, resource, instanceId);
        //             });

        //     if (readAhead.HasValue)
        //         return tpls
        //             .Await(readAhead: readAhead.Value)
        //             .SelectWhere();

        //     return tpls.Await().SelectWhere();
        // }

        public static IEnumerableAsync<BlobItem> GetDatalakeFiles(this IExportFromDatalake import)
        {
            return import.exportUri.BlobFindFilesAsync(
                    fileSuffix: ".parquet")
                .FoldTask();
        }

        public static async Task<TResult> PopulateInstanceAsync<TInstance, TResult>(this IExportFromDatalake import, TInstance instance,
                DurableTaskClient client, string nameOfFunctionToRun,
            Func<TInstance, TResult> onPopulated,
            Func<string, TResult> onFailure)
            where TInstance : DataLakeImportInstance
        {
            try
            {
                var now = DateTime.UtcNow;
                instance.exportUri = import.exportUri;
                instance.when = now;
                instance.cancelled = false;
                instance.ignoreFaultedFiles = false;
                instance.sourceId = import.sourceId;
                return await instance.JsonSerialize(
                    async (instanceJson) =>
                    {
                        var instanceIdToTry = $"{instance.id}:{now.Year}{now.Month}{now.Day}{now.Hour}{now.Minute}{now.Second}";
                        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameOfFunctionToRun,
                            instanceJson, new Microsoft.DurableTask.StartOrchestrationOptions
                            {
                                InstanceId = instanceIdToTry,
                            });
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
                    },
                    (why) =>
                    {
                        return onFailure($"Could not serialize instance `{instance.GetType().FullName}: {why}`").AsTask();
                    });
            }
            catch (Exception ex)
            {
                return onFailure(ex.Message);
            }
        }

        public static async Task<DataLakeImportReport> DataLakeIngestAsync<TResource, TImportInstance, TDataLakeItem>(
            // this IDurableActivityContext context,
            this TImportInstance dataLakeImportInstance, TDataLakeItem dataLakeItem,
            Func<TImportInstance, TDataLakeItem, TResource, AzureBlobFileSystemUri, int, Task<(bool, string)>> processAsync,
            Microsoft.Extensions.Logging.ILogger log)
            where TImportInstance : DataLakeImportInstance
            where TDataLakeItem : DataLakeItem
        {
            return await await IngestAndProduceReportAsync<TDataLakeItem, TResource, TImportInstance, Task<DataLakeImportReport>>(
                    dataLakeImportInstance, dataLakeItem,
                processAsync,
                onProcessed: async (report) =>
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
                // IDurableActivityContext context,
                TImportInstance dataLakeImportInstance, TDataLakeItem dataLakeItem,
                Func<TImportInstance, TDataLakeItem, TResource, AzureBlobFileSystemUri, int, Task<(bool, string)>> processAsync,
            Func<DataLakeImportReport, TResult> onProcessed,
            Func<TResult> onParsingIssue,
            Func<Exception, TResult> onException,
                Microsoft.Extensions.Logging.ILogger log)
            where TDataLakeItem : DataLakeItem
            where TImportInstance : DataLakeImportInstance
        {
            try
            {
                // var instanceId = context.InstanceId.Replace(':', '_');
                // var (dataLakeImportInstance, dataLakeItem) = context.GetInput<(TImportInstance, TDataLakeItem)>();
                if (dataLakeImportInstance.IsDefaultOrNull() || dataLakeItem.IsDefaultOrNull())
                    return onParsingIssue();

                var path = dataLakeItem.path;
                var skip = dataLakeItem.skip;


                var isCancelled = await dataLakeImportInstance.id.AsRef<TImportInstance>().StorageGetAsync(
                    onFound: (updatedInstance) => updatedInstance.cancelled,
                    onDoesNotExists: () => true);
                if (isCancelled)
                {
                    var report = dataLakeItem
                        .GenerateReport(dataLakeImportInstance.exportFromDataLake, DataLakeImportStatus.Cancelled, 0);
                    return onProcessed(report);
                }

                var intervals = new (int, int)[] { };
                if (!dataLakeImportInstance.exportUri.IsValid())
                {
                    var report = dataLakeItem.GenerateReport(dataLakeImportInstance.exportFromDataLake, DataLakeImportStatus.FaultyExport, 0);
                    return onProcessed(report);
                }
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                var timeout = TimeSpan.FromMinutes(4);
                try
                {
                    var ingestedReport = await dataLakeImportInstance.DataLakeIngestFileAsync(dataLakeItem,
                        processAsync: (TResource res, int index) => processAsync(dataLakeImportInstance, dataLakeItem, res, dataLakeImportInstance.exportUri, index),
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

        public static IEnumerableAsync<BlobItem> ListExportedFilesAsync(
            this IExportFromDatalake dataLakeExported,
            string extension = default)
        {
            return dataLakeExported.exportUri
                .BlobFindFilesAsync()
                .FoldTask()
                .Where(
                    file =>
                    {
                        if (extension.IsNullOrWhiteSpace())
                            return true;
                        if (file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                            return true;
                        return false;
                    });
        }

        public static TDataLakeItem[] ConvertToDataLakeItems<TDataLakeItem>(this BlobItem[] blobItems,
            Guid dataLakeInstanceId, DataLakeImportReport[] priorRuns,
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

        public static async Task<string> RunActivityFromDurableFunctionAsync<TImportInstance, TDataLakeItem>(
            this TaskOrchestrationContext context,
            string functionName,
            Microsoft.Extensions.Logging.ILogger log)
            where TImportInstance : DataLakeImportInstance
            where TDataLakeItem : IDataLakeItem
        {
            var inputTpl = context.ParseDataLakeParameter<(TImportInstance, TDataLakeItem)>(out var inputJson);
            var input = inputTpl.Item1;
            var dataLakeItem = inputTpl.Item2;
            while (true)
            {
                var report = await context
                    .CallActivityAsync<string>(functionName, inputJson)
                    .ParseDataLakeJsonAsync<DataLakeImportReport>();

                var status = report.status;
                dataLakeItem.skip = report.GetLastLineProcessed();
                var linesProcessed = report.GetTotalLinesProcessed();

                if (linesProcessed == 0)
                {
                    log.LogInformation($"[{input.instance}/{dataLakeItem.path}]...Finishing status ({status}) with `{linesProcessed}` records");
                    report.status = DataLakeImportStatus.FaultedInstance;
                    return report.SerializeDataLakeJson();
                }
                if (status == DataLakeImportStatus.Running || status == DataLakeImportStatus.Partial)
                    continue;

                log.LogInformation($"[{input.instance}/{dataLakeItem.path}]...Finishing status ({status}) with `{linesProcessed}` records");
                return report.SerializeDataLakeJson();
            }
        }

        public static T ParseDataLakeParameter<T>(this TaskOrchestrationContext context) =>
            context.GetInput<string>().ParseDataLakeJson<T>();

        public static T ParseDataLakeParameter<T>(this TaskOrchestrationContext context, out string json)
        {
            json = context.GetInput<string>();
            return json.ParseDataLakeJson<T>();
        }

        public static async Task<T> ParseDataLakeJsonAsync<T>(this Task<string> inputJsonMaybeTask)
        {
            var inputJsonMaybe = await inputJsonMaybeTask;
            return inputJsonMaybe.ParseDataLakeJson<T>();
        }

#nullable enable

        public static async Task<T> ParseDataLakeJsonMaybeAsync<T>(this Task<string?> inputJsonMaybeTask)
        {
            var inputJsonMaybe = await inputJsonMaybeTask;
            return inputJsonMaybe.ParseDataLakeJson<T>();
        }

        public static T ParseDataLakeJson<T>(this string? inputJsonMaybe)
        {
            if (string.IsNullOrWhiteSpace(inputJsonMaybe))
            {
                var errorMsg = $"Input is null or empty";
                throw new InvalidOperationException(errorMsg);
            }
            var inputJson = inputJsonMaybe;
            return inputJson.JsonParse(
                (T input) => input,
                onFailureToParse: (why) =>
                {
                    var errorMsg = $"Could not parse input:`{why}`";
                    throw new InvalidOperationException(errorMsg);
                },
                onException: (ex) =>
                {
                    var errorMsg = $"Server error parsing input:`{ex.Message}`";
                    throw new InvalidOperationException(errorMsg);
                });
        }

        public static string SerializeDataLakeJson<T>(this T resourceToPass)
        {
            var parameterJson = resourceToPass
                .JsonSerialize(
                    (json) => json,
                    (why) =>
                    {
                        var errorMsg = $"Could not serialize input:`{why}`";
                        throw new InvalidOperationException(errorMsg);
                    });
            return parameterJson;
        }
    }
}
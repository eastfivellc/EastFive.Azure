using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;

using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.Blobs;
using EastFive.Extensions;
using EastFive.Analytics;
using EastFive.Linq.Async;

using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Models;
using static EastFive.Azure.Monitoring.MessageCard.ActionCard;

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

            if(readAhead.HasValue)
                return tpls
                    .Await(readAhead:readAhead.Value)
                    .SelectWhere();

            return tpls.Await().SelectWhere();
        }

        public static async Task<(bool, int)> DataLakeIngestAsync<TResource>(
            this IDurableActivityContext context,
            Func<TResource, (string path, string container), int, Task<(bool, string)>> processAsync,
            Microsoft.Extensions.Logging.ILogger log)
        {
            try
            {
                var instanceId = context.InstanceId.Replace(':', '_');
                var (path, skip, containerName, folder) = context.GetInput<(string, int, string, string)>();
                if (folder.IsNullOrWhiteSpace())
                    return (true, 0);
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                var timeout = TimeSpan.FromMinutes(4);
                return await instanceId.DataLakeIngestFileAsync(
                        path: path, containerName: containerName, folder:folder, skip:skip,
                    processAsync: (TResource res, int index) => processAsync(res, (path, containerName), index),
                    isTimedOut: () =>
                    {
                        return startTime.Elapsed > timeout;
                    },
                    log:log);
            }
            catch (Newtonsoft.Json.JsonReaderException parsingException)
            {
                log.LogError(parsingException.Message);
                log.LogError("Bad resource input -- discarding");
                return (true, 0);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);
                throw;
            }
        }

        public async static Task<(bool, int)> DataLakeIngestFileAsync<TResource>(
            this string instanceId,
            string path, string containerName,  string folder, int skip,
            Func<TResource, int, Task<(bool, string)>> processAsync,
            Func<bool> isTimedOut,
            Microsoft.Extensions.Logging.ILogger log)
        {
            var capturedLog = new EastFive.Analytics.CaptureLog(
                new Analytics.AnalyticsLogger(log));
            var directoryFileSplit = path.LastIndexOf('/');
            var file = path.Substring(directoryFileSplit+1);
            var dateTime = DateTime.UtcNow;
            try
            {
                return await await path.ReadParquetDataFromDataLakeAsync(containerName,
                    async (TResource[] resourceLines) =>
                    {
                        capturedLog.Information($"[{path}] Loaded--{resourceLines.Length} lines (skipping {skip})");

                        var report = await resourceLines
                            .Skip(skip)
                            .Aggregate(
                                new DataLakeImportReport
                                {
                                    container = containerName,
                                    directory = folder,

                                    file = file,
                                    instanceId = instanceId,
                                    when = dateTime,

                                    path = path,
                                    linesProcessedStart = skip,
                                    linesProcessedEnd = skip,
                                    isComplete = true,
                                    errorsProcessed = new DataLakeImportReport.Error[] { },
                                }.AsTask(),
                                async (reportTask, resource) =>
                                {
                                    var currentReport = await reportTask;
                                    var index = currentReport.linesProcessedEnd;

                                    var isTimedOutResult = isTimedOut();
                                    if (isTimedOutResult)
                                    {
                                        currentReport.isComplete = false;
                                        return currentReport;
                                    }

                                    try
                                    {
                                        var (wasSuccessful, errorMessage) = await processAsync(resource, index);
                                        if(!wasSuccessful)
                                        {
                                            currentReport.errorsProcessed = currentReport.errorsProcessed
                                                .Append(
                                                    new DataLakeImportReport.Error
                                                    {
                                                        index = index,
                                                        message = errorMessage,
                                                    })
                                                .ToArray();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        capturedLog.Critical($"EXCEPTION [{path}] Line {index}:{ex.Message}");
                                        currentReport.errorsProcessed = currentReport.errorsProcessed
                                            .Append(
                                                new DataLakeImportReport.Error
                                                {
                                                    index = index,
                                                    message = ex.Message,
                                                })
                                            .ToArray();
                                    }
                                    finally
                                    {
                                        currentReport.linesProcessedEnd = currentReport.linesProcessedEnd + 1;
                                    }
                                    return currentReport;
                                });

                        var state = report.isComplete ? "completed" : "terminated";

                        capturedLog.Information($"[{path}] {state} at index {report.linesProcessedEnd} after processing {report.linesProcessedEnd - report.linesProcessedStart} lines with {report.errorsProcessed.Length} errors.");

                        try
                        {
                            bool success = await report.StorageCreateAsync(
                                result => true,
                                onAlreadyExists: () =>
                                {
                                    log.LogCritical($"Duplicate report:{report.file}, {report.instanceId}");
                                    return false;
                                });
                        }
                        catch (Exception ex)
                        {
                            if (log.IsNotDefaultOrNull())
                                log.LogError(ex.Message);
                        }    

                        return (report.isComplete, report.linesProcessedEnd);
                    },
                    onNotFound: () =>
                    {
                        capturedLog.Information($"[{path}]: Not found");
                        return (true, skip).AsTask();
                    });
            }
            catch (Exception ex)
            {
                capturedLog.Information($"[{path}]:Failure--{ex.Message}");
                return (true, 0);
            }
            finally
            {
                
            }
        }

        public static DataLakeItem[] ConvertToResources(this BlobItem[] blobItems,
            string containerName, string folder)
        {
            return blobItems
                .Select(
                    item =>
                    {
                        return new DataLakeItem
                        {
                            container = containerName,
                            folder = folder,
                            path = item.Name,
                            display = item.Name,
                        };
                    })
                .ToArray();
        }

        public static async Task<int> RunActivityFromDurableFunctionAsync<TInput>(this IDurableOrchestrationContext context,
            string functionName,
            Microsoft.Extensions.Logging.ILogger log)
        {
            //log.LogInformation($"[{context.InstanceId}/{context.Name}]...Starting");
            var (input, resource) = context.GetInput<(TInput, DataLakeItem)>();
            var skip = 0;
            var complete = false;

            while (!complete)
            {
                (complete, skip) = await context.CallActivityAsync<(bool, int)>(
                    functionName,
                    (resource.path, skip, resource.container, resource.folder));
                log.LogInformation($"[{context.InstanceId}/{context.Name}/{resource.display}]...SEGMENT:{skip} records");
            }

            log.LogInformation($"[{context.InstanceId}/{context.Name}/{resource.display}]...Complete ({complete}) with `{skip}` records");
            return skip;
        }
    }
}
using System;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.Blobs;
using EastFive.Extensions;
using EastFive.Linq.Async;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace EastFive.Azure.Functions
{
	public static class FunctionExtensions
	{
		public static IEnumerableAsync<(T, string)> SelectWhereNotAlreadyRunning<T>(this IEnumerableAsync<T> resources,
            IDurableOrchestrationClient orchestrationClient,
            Func<T, string> getInstanceId,
            ILogger log = default,
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
            Func<TResource, (string path, string container), int, Task<bool>> processAsync,
            ILogger log)
        {
            try
            {
                var (path, skip, containerName) = context.GetInput<(string, int, string)>();
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                var timeout = TimeSpan.FromMinutes(4);
                return await DataLakeIngestFileAsync(
                    (path: path, containerName: containerName, skip:skip),
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
            this (string path, string containerName, int skip) tpl,
            Func<TResource, int, Task<bool>> processAsync,
            Func<bool> isTimedOut,
            ILogger log)
        {
            var (path, containerName, skip) = tpl;
            try
            {
                return await await path.ReadParquetDataFromDataLakeAsync(containerName,
                    async (TResource[] resourceLines) =>
                    {
                        if(log.IsNotDefaultOrNull())
                            log.LogInformation($"[{path}] Loaded--{resourceLines.Length} lines (skipping {skip})");

                        var (linesProcessed, isComplete, countProcessed, errorsProcessed) = await resourceLines
                            .Skip(skip)
                            .Aggregate(
                                (index: skip, complete: true, count: 0, errorCount: 0).AsTask(),
                                async (indexTask, resource) =>
                                {
                                    var (index, complete, count, errorCount) = await indexTask;

                                    var isTimedOutResult = isTimedOut();
                                    if (isTimedOutResult)
                                        return (index, false, count, errorCount);

                                    try
                                    {
                                        var shouldCount = await processAsync(resource, index);
                                        return (index + 1, true, count + 1, errorCount);
                                    }
                                    catch (Exception ex)
                                    {
                                        if (log.IsNotDefaultOrNull())
                                            log.LogError($"EXCEPTION [{path}] Line {index}:{ex.Message}");
                                        return (index + 1, true, count, errorCount + 1);
                                    }
                                });

                        var state = isComplete ? "completed" : "terminated";

                        if (log.IsNotDefaultOrNull())
                            log.LogInformation($"[{path}] {state} at index {linesProcessed} after processing {countProcessed} and {errorsProcessed} errors.");
                        return (isComplete, linesProcessed);
                    },
                    onNotFound: () =>
                    {
                        if (log.IsNotDefaultOrNull())
                            log.LogInformation($"[{path}]: Not found");
                        return (true, skip).AsTask();
                    });
            }
            catch (Exception ex)
            {
                if (log.IsNotDefaultOrNull())
                    log.LogInformation($"[{path}]:Failure--{ex.Message}");
                return (true, 0);
            }
        }
    }
}
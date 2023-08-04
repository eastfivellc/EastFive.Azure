using System;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EastFive.Azure.Persistence.AzureStorageTables;
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
            Func<TResource, Task<bool>> processAsync,
            ILogger log)
        {
            try
            {
                var (path, skip, containerName) = context.GetInput<(string, int, string)>();
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                var timeout = TimeSpan.FromMinutes(4);
                return await DataLakeIngestFileAsync(
                    (path: path, containerName: containerName, skip:skip),
                    processAsync: processAsync,
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
            Func<TResource, Task<bool>> processAsync,
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

                        var (linesProcessed, isComplete) = await resourceLines
                            .Skip(skip)
                            .Aggregate(
                                (index: skip, complete: false).AsTask(),
                                async (indexTask, resource) =>
                                {
                                    var (index, complete) = await indexTask;
                                    if (complete)
                                        return (index, true);

                                    var timedOut = isTimedOut();
                                    if (timedOut)
                                        return (index, true);

                                    try
                                    {
                                        var shouldCount = await processAsync(resource);
                                        if (shouldCount)
                                            return (index + 1, false);
                                        return (index, false);
                                    }
                                    catch (Exception ex)
                                    {
                                        if (log.IsNotDefaultOrNull())
                                            log.LogError($"EXCEPTION [{path}] Line {index}:{ex.Message}");
                                        return (index + 1, complete);
                                    }
                                });

                        var state = isComplete ? "completed" : "terminated";

                        if (log.IsNotDefaultOrNull())
                            log.LogInformation($"[{path}] {state} at index {linesProcessed}");
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
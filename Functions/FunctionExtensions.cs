using System;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.Blobs;
using EastFive.Extensions;
using EastFive.Analytics;
using EastFive.Linq.Async;

using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System.Text;
using System.IO;
using EastFive.Serialization.Json;

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
            Func<TResource, (string path, string container), int, Task<bool>> processAsync,
            Microsoft.Extensions.Logging.ILogger log)
        {
            try
            {
                var instanceId = context.InstanceId.Replace(':', '_');
                var (path, skip, containerName) = context.GetInput<(string, int, string)>();
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                var timeout = TimeSpan.FromMinutes(4);
                return await DataLakeIngestFileAsync(
                    (path: path, containerName: containerName, skip:skip),
                    instanceId:instanceId,
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

        public struct Report
        {
            public string path;
            public int linesProcessed;
            public bool isComplete;
            public int countProcessed;
            public Error[] errorsProcessed;

            public struct Error
            {
                public int index;
                public string message;
            }
        }

        public async static Task<(bool, int)> DataLakeIngestFileAsync<TResource>(
            this (string path, string containerName, int skip) tpl,
            string instanceId,
            Func<TResource, int, Task<bool>> processAsync,
            Func<bool> isTimedOut,
            Microsoft.Extensions.Logging.ILogger log)
        {
            var capturedLog = new EastFive.Analytics.CaptureLog(
                new Analytics.AnalyticsLogger(log));
            var (path, containerName, skip) = tpl;
            var dateTime = DateTime.UtcNow;
            var logPath = path + $".instance-{instanceId}-{dateTime.Year}.{dateTime.Month}.{dateTime.Day}-{dateTime.Hour}_{dateTime.Minute}_{dateTime.Second}.json";
            try
            {
                return await await path.ReadParquetDataFromDataLakeAsync(containerName,
                    async (TResource[] resourceLines) =>
                    {
                        capturedLog.Information($"[{path}] Loaded--{resourceLines.Length} lines (skipping {skip})");

                        var report = await resourceLines
                            .Skip(skip)
                            .Aggregate(
                                new Report
                                {
                                    path = path,
                                    linesProcessed = skip,
                                    isComplete = true,
                                    countProcessed = 0,
                                    errorsProcessed = new Report.Error[] { },
                                }.AsTask(),
                                async (reportTask, resource) =>
                                {
                                    var currentReport = await reportTask;
                                    var index = currentReport.linesProcessed;
                                    var complete = currentReport.isComplete;
                                    var count = currentReport.countProcessed;

                                    var isTimedOutResult = isTimedOut();
                                    if (isTimedOutResult)
                                    {
                                        currentReport.isComplete = false;
                                        return currentReport;
                                    }

                                    try
                                    {
                                        var shouldCount = await processAsync(resource, index);
                                        currentReport.linesProcessed = currentReport.linesProcessed + 1;
                                        currentReport.countProcessed = currentReport.countProcessed + 1;
                                        return currentReport;
                                    }
                                    catch (Exception ex)
                                    {
                                        capturedLog.Critical($"EXCEPTION [{path}] Line {index}:{ex.Message}");
                                        currentReport.errorsProcessed = currentReport.errorsProcessed
                                            .Append(
                                                new Report.Error
                                                {
                                                    index = index,
                                                    message = ex.Message,
                                                })
                                            .ToArray();
                                        return currentReport;
                                    }
                                });

                        var state = report.isComplete ? "completed" : "terminated";

                        capturedLog.Information($"[{path}] {state} at index {report.linesProcessed} after processing {report.countProcessed} and {report.errorsProcessed.Length} errors.");

                        try
                        {
                            bool success = await logPath.BlobCreateOrUpdateAsync(containerName,
                                        async stream =>
                                        {
                                            var json = report.JsonSerialize(
                                                j => j);
                                            var writer = new StreamWriter(stream);
                                            await writer.WriteAsync(json);
                                            await writer.FlushAsync();
                                        },
                                    onSuccess: contentInfo =>
                                    {
                                        return true;
                                    },
                                    onFailure: (errorinfo, why) =>
                                    {
                                        if (log.IsNotDefaultOrNull())
                                            log.LogError(why);
                                        return false;
                                    },
                                    contentTypeString: "text/log", fileName: $"{instanceId}.{DateTime.UtcNow.Ticks}.log",
                                    connectionStringConfigKey:EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString);
                        }
                        catch (Exception ex)
                        {
                            if (log.IsNotDefaultOrNull())
                                log.LogError(ex.Message);
                        }    

                        return (report.isComplete, report.linesProcessed);
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
    }
}
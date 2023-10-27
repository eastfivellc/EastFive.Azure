using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Newtonsoft.Json;

using EastFive;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Extensions;
using EastFive.Analytics;

namespace EastFive.Azure.Functions
{
    /// <summary>
    /// This class should be subclassed for each type of data lake import.
    /// One instance of the subclass is created each time the orchistration is run.
    /// </summary>
	[StorageTable]
	public class DataLakeImportInstance : IReferenceable
    {
        #region Base

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 2)]
        public Guid id { get; set; }

        [ETag]
        [JsonIgnore]
        public string eTag;

        public const string LastModifiedPropertyName = "lastModified";
        [ApiProperty(PropertyName = LastModifiedPropertyName)]
        [JsonProperty(PropertyName = LastModifiedPropertyName)]
        [LastModified]
        public DateTime lastModified;

        #endregion

        [Storage]
        [DateTimeLookup(Partition = TimeSpanUnits.weeks, Row = TimeSpanUnits.days)]
        public DateTime when;

        /// <summary>
        /// ID of resource that implements IExportFromDatalake
        /// </summary>
        [Storage]
        [StorageQuery]
        public Guid export;

        [Storage]
        public string exportContainer;

        [Storage]
        public string exportFolder;

        [Storage]
        public DateTime? usePriorRunsFromMaybe;

        [Storage]
        public bool shouldRunErrors;

        [Storage]
        public bool cancelled;

        /// <summary>
        /// Instance ID of the orchestration that is running.
        /// </summary>
        [Storage]
        public string instance;

        /// <summary>
        /// Id that is the unique to the file being sourced.
        /// </summary>
        [Storage]
        public Guid sourceId;

        public async Task<TResult> DataLakeIngestFileAsync<TResource, TResult>(DataLakeItem item,
                Func<TResource, int, Task<(bool, string)>> processAsync,
            Func<DataLakeImportReport, TResult> onFileIngested,
            Func<TResult> onFileNotFound,
                Func<bool> isTimedOut,
                Microsoft.Extensions.Logging.ILogger log)
        {
            var import = this;
            var capturedLog = new EastFive.Analytics.CaptureLog(
                new Analytics.AnalyticsLogger(log));
            var dateTime = DateTime.UtcNow;
            return await await item.path.ReadParquetDataFromDataLakeAsync(import.exportContainer,
                    async (TResource[] resourceLines) =>
                    {
                        capturedLog.Information($"[{item.path}] Loaded--{resourceLines.Length} lines (skipping {item.skip})");

                        var indexesToRun = item.lines
                            .Append(DataLakeImportReport.Interval.Create(item.skip, resourceLines.Length - item.skip))
                            .SelectMany(
                                interval =>
                                {
                                    return Enumerable.Range(interval.start, interval.length);
                                })
                            .ToArray();

                        bool didTimeOut = false;
                        var rowsProcessed = await resourceLines
                            .SelectWithIndexes(indexesToRun)
                            .Select(
                                async resourceTpl =>
                                {
                                    var (index, resource) = resourceTpl;
                                    var isTimedOutResult = didTimeOut || isTimedOut();
                                    if (isTimedOutResult)
                                    {
                                        didTimeOut = true;
                                        return default((int, bool, string)?);
                                    }

                                    try
                                    {
                                        var (wasSuccessful, errorMessage) = await processAsync(resource, index);
                                        if (!wasSuccessful)
                                        {
                                            return (index, true, errorMessage);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        capturedLog.Critical($"EXCEPTION [{item.path}] Line {index}:{ex.Message}");
                                        return (index, true, ex.Message);
                                    }
                                    return (index, false, "");
                                })
                            .AsyncEnumerable()
                            .ToArrayAsync();

                        var report = item.GenerateReport(this.export,
                            didTimeOut ?
                                DataLakeImportStatus.Partial
                                :
                                DataLakeImportStatus.Complete,
                            resourceLines.Length);

                        foreach (var rowProcessed in rowsProcessed)
                            if (rowProcessed.HasValue)
                            {
                                report = report.AddLineToInterval(rowProcessed.Value.Item1);
                                if (rowProcessed.Value.Item2)
                                    report = report.AppendError(rowProcessed.Value.Item1, rowProcessed.Value.Item3);
                            }

                        var state = report.status.ToString();

                        capturedLog.Information($"[{item.path}] {state} at index {report.GetLastLineProcessed()} after processing {report.GetTotalLinesProcessed()} lines with {report.errorsProcessed.Length} errors.");

                        return onFileIngested(report);

                    },
                    onNotFound: () =>
                    {
                        capturedLog.Information($"[{item.path}]: Not found");
                        return onFileNotFound().AsTask();
                    });
        }
    }
}


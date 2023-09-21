using System;
using System.Linq;

using EastFive.Linq;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using EastFive.Extensions;
using System.Collections.Generic;

namespace EastFive.Azure.Functions
{
    public enum DatalakeImportStatus
    {
        Running,
        Cancelled,
        Complete,
        Partial,
        FaultedLocal,
        FaultedFile,
        FaultedInstance,
        FaultyExport,
    }


    [StorageTable]
	public struct DataLakeImportReport
    {
        #region Base

        [ETag]
        [JsonIgnore]
        public string eTag;

        public const string LastModifiedPropertyName = "lastModified";
        [ApiProperty(PropertyName = LastModifiedPropertyName)]
        [JsonProperty(PropertyName = LastModifiedPropertyName)]
        [LastModified]
        public DateTime lastModified;

        #endregion

        // Path is _not_ always folder + file
        // because there are often sub-directories
        // based on the group-by statements in the datalake export.
        [Storage]
        [ScopedRowKey]
        [ScopeString(
            ScopedRowKeyAttribute.Scoping,
            Order = 1.0,
            Separator = "_")]
        public string path; 

        [Storage]
        [ScopeDateTime(
            ScopedRowKeyAttribute.Scoping,
            Order = 2.0,
            Separator = ":",
            Format = "dd:HH:mm:ss")]
        [ScopeDateTime(
            ScopedPartitionAttribute.Scoping,
            Order = 2.0,
            Separator = ":",
            SpanUnits = TimeSpanUnits.days)]
        public DateTime when;

        /// <summary>
        /// This is the DataLakeImport subclass's id. It is unique to every single run.
        /// </summary>
        [ScopeId(
            ScopedRowKeyAttribute.Scoping,
            Order = 3.0,
            Separator = "_")]
        public Guid instanceId;

        /// <summary>
        /// This is the same value as the subclass of DataLakeImportInstance's export property.
        /// This is _not_ the DataLakeImport subclass's id.
        /// </summary>
        [ScopedPartition]
        [ScopeId(ScopedPartitionAttribute.Scoping, Order = 1.0)]
        public Guid export;

        [Storage]
        public Interval[] intervalsProcessed;

        public struct Interval
        {
            public int start;
            public int length;
            public int GetLastIndex() => start + length;

            internal static Interval Create(int start, int length)
            {
                return new Interval
                {
                    start = start,
                    length = length,
                };
            }

            internal static Interval[] CreateFromIndexes(int[] indexes)
            {
                var ordered = indexes
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray();

                var subsets = ordered
                    .Split(
                        (last, next) =>
                        {
                            var shouldSplit = next - last != 1;
                            return shouldSplit;
                        })
                    .ToArray();

                var intervals = subsets
                    .Select(
                        (intervalIndexes) =>
                        {
                            var start = intervalIndexes.Min();
                            var end = intervalIndexes.Max();
                            var length = (end - start) + 1;
                            return Create(start, length);
                        })
                    .ToArray();

                return intervals;
            }
        }

        [Storage]
        public int linesTotal;

        [Storage]
        public DatalakeImportStatus status;

        
        [JsonIgnore]
        public string File
        {
            get
            {
                var file = "";
                if (this.path.HasBlackSpace())
                {
                    var directoryFileSplit = this.path.LastIndexOf('/');
                    file = path.Substring(directoryFileSplit + 1);
                }
                return file;
            }
        }

        [Storage]
        public Error[] errorsProcessed;

        public struct Error
        {
            public int index;
            public string message;
        }

        public static IEnumerableAsync<DataLakeImportReport> GetFromStorage(
            Guid import, DateTime when)
        {
            return import
                .StorageGetQueryForPropertyEquals(
                    (DataLakeImportReport r) => r.export)
                .Where(
                    (DataLakeImportReport r) => r.when == when)
                .StorageGet();
        }

        public int[] GetLinesProcessed()
        {
            return intervalsProcessed
                .NullToEmpty()
                .SelectMany(
                    interval =>
                    {
                        return Enumerable.Range(interval.start, interval.length);
                    })
                .ToArray();
        }

        public int[] MissingLines(int[] linesToCheck)
        {
            var linesProcessed = GetLinesProcessed();
            var linesRemaining = linesToCheck.Except(linesProcessed).ToArray();
            return linesRemaining;
        }

        public static bool DidComplete(DataLakeImportReport[] reports,
            out int maxLineProcessed, out int[] missingLines, out int[] erroredLines)
        {
            if(reports.IsDefaultNullOrEmpty())
            {
                maxLineProcessed = 0;
                missingLines = new int[] { };
                erroredLines = new int[] { };
                return false;
            }
            var didComplete = reports.Any(report => report.status == DatalakeImportStatus.Complete);
            maxLineProcessed = reports.MaxOrEmpty(v => v.linesTotal,
                (v, maxValue) => maxValue,
                () => 0);
            var linesToCheck = Enumerable.Range(0, maxLineProcessed).ToArray();
            missingLines = reports
                .Aggregate(
                    linesToCheck,
                    (lines, report) =>
                    {
                        return report.MissingLines(lines);
                    });
            erroredLines = reports
                .SelectMany(r => r.errorsProcessed.Select(e => e.index))
                .ToArray();
            var didCompleteFinal = didComplete && missingLines.None();
            return didCompleteFinal;
        }

        internal DataLakeImportReport AppendError(int index, string errorMessage)
        {
            this.errorsProcessed = this.errorsProcessed
                .Append(
                    new DataLakeImportReport.Error
                    {
                        index = index,
                        message = errorMessage,
                    })
                .ToArray();
            return this;
        }

        internal DataLakeImportReport AddLineToInterval(int index)
        {
            var linesProcessed = this.GetLinesProcessed();
            var linesUpdated = linesProcessed.Append(index).ToArray();
            this.intervalsProcessed = Interval.CreateFromIndexes(linesUpdated);
            return this;
        }

        public int GetTotalLinesProcessed()
        {
            var processedSuccess = this.GetLinesProcessed().Length;
            return processedSuccess; // Errors are included in the processed items;
            //var processedErrors = this.errorsProcessed
            //    .NullToEmpty()
            //    .Count();
            //return processedErrors + processedSuccess;
        }

        public int GetLastLineProcessed()
        {
            var errorLinesProcessed = this.errorsProcessed
                .NullToEmpty()
                .Select(err => err.index)
                .ToArray();
            var linesProcessed = this
                .GetLinesProcessed()
                .NullToEmpty()
                .Concat(errorLinesProcessed)
                .ToArray();

            return linesProcessed.MaxOrEmpty(i => i,
                (maxIndex, max) => max,
                () => 0);
        }
    }
}


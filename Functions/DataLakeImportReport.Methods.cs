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
	public partial struct DataLakeImportReport
    {

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
            var didComplete = reports.Any(report => report.status == DataLakeImportStatus.Complete);
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


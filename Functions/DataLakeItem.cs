using System;
using System.Linq;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Serialization;

namespace EastFive.Azure.Functions
{
	public class DataLakeItem
    {
        public Guid dataLakeInstance;
        public string path;
        public DataLakeImportReport.Interval[] lines;
        public int skip;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dataLakeImportId">ID of resource implementing IExportFromDatalake</param>
        /// <param name="status"></param>
        /// <param name="linesTotal"></param>
        /// <returns></returns>
        internal DataLakeImportReport GenerateReport(Guid dataLakeExportId,
            DatalakeImportStatus status, int linesTotal)
        {
            return new DataLakeImportReport
            {
                instanceId = this.dataLakeInstance,
                export = dataLakeExportId,
                status = status,
                path = this.path,
                intervalsProcessed = new DataLakeImportReport.Interval[] { },
                errorsProcessed = new DataLakeImportReport.Error[] { },
                linesTotal = linesTotal,
                when = DateTime.UtcNow,
            };
        }

        public DataLakeItem SetLinesToRun(int[] linesToRun)
        {
            this.lines = DataLakeImportReport.Interval.CreateFromIndexes(linesToRun);
            return this;
        }
    }
}


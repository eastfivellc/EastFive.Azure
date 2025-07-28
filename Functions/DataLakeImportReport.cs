using System;
using System.Linq;
using System.Collections.Generic;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.Functions
{
    [StorageTable]
	public partial struct DataLakeImportReport
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

        [StorageOverflow]
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
        public DataLakeImportStatus status;

        
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

        [StorageOverflow]
        public Error[] errorsProcessed;

        public struct Error
        {
            public int index;
            public string message;
        }
    }
}


using System;
using EastFive.Api;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;

namespace EastFive.Azure.Functions
{
	[StorageTable]
	public class DataLakeImportReport
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

        [ScopedRowKey]
        [ScopeString(
            ScopedRowKeyAttribute.Scoping,
            Order = 0.0)]
        [Storage]
        public string file;

        [ScopedRowKey]
        [ScopeString(
            ScopedRowKeyAttribute.Scoping,
            Order = 1.0,
            Separator = "-")]
        [Storage]
        public string instanceId;

        [ScopeDateTime(
            ScopedRowKeyAttribute.Scoping,
            SpanUnits = TimeSpanUnits.seconds,
            Order = 2.0,
            Separator = "-")]
        [Storage]
        public DateTime when;

        [ScopeString(
            ScopedPartitionAttribute.Scoping,
            Order = 0.0)]
        [ScopedPartition]
        [Storage]
        public string container;

        [ScopeString(
            ScopedPartitionAttribute.Scoping,
            Order = 1.0,
            Separator = ":")]
        [Storage]
        public string directory;

        [Storage]
        public string path; // Path is _not_ always directory + file
                            // because there are often sub-directories
                            // based on the group-by statements in the datalake export.

        [Storage]
        public int linesProcessedStart;

        [Storage]
        public int linesProcessedEnd;

        [Storage]
        public bool isComplete;

        [Storage]
        public Error[] errorsProcessed;

        public struct Error
        {
            public int index;
            public string message;
        }
    }
}


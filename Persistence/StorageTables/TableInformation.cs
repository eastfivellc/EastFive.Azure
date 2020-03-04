using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using EastFive.Api;

namespace EastFive.Azure.Persistence.StorageTables
{
    [JsonSerializationDictionarySafeProvider]
    public class TableInformation
    {
        public long total;
        public long mismatchedRowKeys;
        public long mismatchedPartitionKeys;
        public IDictionary<string, IDictionary<object, long>> properties;
        public IDictionary<string, PartitionSummary> partitions;
    }

    [JsonSerializationDictionarySafeProvider]
    public class PartitionSummary
    {
        public long total;
        public IDictionary<string, IDictionary<object, long>> properties;
    }

}

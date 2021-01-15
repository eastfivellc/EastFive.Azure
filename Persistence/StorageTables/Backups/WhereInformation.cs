using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using BlackBarLabs.Persistence.Azure.Attributes;

namespace EastFive.Azure.Persistence.StorageTables.Backups
{
    public struct WhereInformation
    {
        public static readonly string[] stringColumns = new[] { "PartitionKey", "RowKey" };

        // these are capitalized to match the column name in stringColumns
        public string PartitionKeyEqual;
        public string PartitionKeyNotEqual;
        public string PartitionKeyGreaterThan;
        public string PartitionKeyGreaterThanOrEqual;
        public string PartitionKeyLessThan;
        public string PartitionKeyLessThanOrEqual;
        public string RowKeyEqual;
        public string RowKeyNotEqual;
        public string RowKeyGreaterThan;
        public string RowKeyGreaterThanOrEqual;
        public string RowKeyLessThan;
        public string RowKeyLessThanOrEqual;

        public static WhereInformation GetWithPartitionKey(StringKey partionKey)
        {
            return new WhereInformation
            {
                PartitionKeyEqual = partionKey.Equal,
                PartitionKeyNotEqual = partionKey.NotEqual,
                PartitionKeyGreaterThan = partionKey.GreaterThan,
                PartitionKeyGreaterThanOrEqual = partionKey.GreaterThanOrEqual,
                PartitionKeyLessThan = partionKey.LessThan,
                PartitionKeyLessThanOrEqual = partionKey.LessThanOrEqual,
            };
        }

        public static WhereInformation GetWithPartitionKeyAndRowKey(StringKey partitionKey, StringKey rowKey)
        {
            var where = GetWithPartitionKey(partitionKey);
            where.RowKeyEqual = rowKey.Equal;
            where.RowKeyNotEqual = rowKey.NotEqual;
            where.RowKeyGreaterThan = rowKey.GreaterThan;
            where.RowKeyGreaterThanOrEqual = rowKey.GreaterThanOrEqual;
            where.RowKeyLessThan = rowKey.LessThan;
            where.RowKeyLessThanOrEqual = rowKey.LessThanOrEqual;
            return where;
        }

        internal static IEnumerable<WhereInformation> GenerateWhereInformation(StringKeyGenerator partitionKeyGenerator, StringKeyGenerator rowKeyGenerator)
        {
            var partitionKeys = partitionKeyGenerator.GetKeys().ToArray();
            var rowKeys = rowKeyGenerator.GetKeys().ToArray();

            foreach (var partitionKey in partitionKeys)
            {
                if (!rowKeys.Any())
                {
                    yield return WhereInformation.GetWithPartitionKey(partitionKey);
                    continue;
                }
                foreach (var rowKey in rowKeys)
                    yield return WhereInformation.GetWithPartitionKeyAndRowKey(partitionKey, rowKey);
            }

            // if no keys, return an empty filter to get everything
            if (!partitionKeys.Any() && !rowKeys.Any())
                yield return new WhereInformation();
        }

        private static readonly IDictionary<string, string> ComparisonMap = new Dictionary<string, string>
            {
                { nameof(QueryComparisons.Equal), QueryComparisons.Equal },
                { nameof(QueryComparisons.NotEqual), QueryComparisons.NotEqual },
                { nameof(QueryComparisons.GreaterThan), QueryComparisons.GreaterThan },
                { nameof(QueryComparisons.GreaterThanOrEqual), QueryComparisons.GreaterThanOrEqual },
                { nameof(QueryComparisons.LessThan), QueryComparisons.LessThan },
                { nameof(QueryComparisons.LessThanOrEqual), QueryComparisons.LessThanOrEqual },
            };

        public IEnumerable<string> FormatWhereInformation()
        {
            var where = this;
            foreach (string columnName in WhereInformation.stringColumns)
                foreach (var op in ComparisonMap)
                {
                    var value = (string)where.GetType().GetField($"{columnName}{op.Key}").GetValue(where); // i.e. "PartitionKeyEqual"
                    if (value.HasBlackSpace())
                        yield return TableQuery.GenerateFilterCondition(columnName, op.Value, value);
                }
        }

    }
}

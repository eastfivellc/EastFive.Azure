using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Documents.SystemFunctions;

namespace EastFive.Persistence.Azure.StorageTables;

[StorageTable]
public struct StorageLookupTable
{
    [RowKey]
    public string rowKey;

    [PartitionKey]
    public string partitionKey;

    [ETag]
    public string eTag;

    [LastModified]
    public DateTime lastModified;

    [StorageLookupTableRowAndPartitionKeys]
    public KeyValuePair<string, string>[] rowAndPartitionKeys;
}

public class StorageLookupTableRowAndPartitionKeysAttribute : StorageOverflowAttribute,
    IPersistInAzureStorageTables
{
        private const string overflowToken = "8d40521b-7d71-47b3-92c5-46e4a804e7de";
        private const string overflowTokenString = "9a9a2e13d0ed44d7aa39c2549aff176a";

        // public override KeyValuePair<string, EntityProperty>[] ConvertValue<EntityType>(MemberInfo memberInfo,
        //     object value, IWrapTableEntity<EntityType> tableEntityWrapper)
        // {
        //     // StorageOverflowAttribute.ConvertValue();
        //     var propertyName = this.GetTablePropertyName(memberInfo);
        //     var valueType = memberInfo.GetPropertyOrFieldType();

        //     return CastValue(valueType, value, propertyName)
        //         .SelectMany(
        //             propNameStorageValueKvp =>
        //             {
        //                 var propName = propNameStorageValueKvp.Key;
        //                 var storageValue = propNameStorageValueKvp.Value;
        //                 return ComputeOverflowValues(propName, storageValue);
        //             })
        //         .ToArray();

        // }

        public override object GetMemberValue(MemberInfo memberInfo,
            IDictionary<string, EntityProperty> values, out bool shouldSkip, Func<object> getDefaultValue = default)
        {
            var propertyName = this.GetTablePropertyName(memberInfo);
            if(!String.Equals(propertyName, "astKvps", StringComparison.Ordinal))
                if(values.TryGetValue("astKvps", out var astKvps))
                    if(astKvps.PropertyType == EdmType.String)
                        if(TryParseKvps(astKvps.StringValue, out var parsedKvps))
                        {
                            shouldSkip = false;
                            return parsedKvps;
                        }

            return base.GetMemberValue(memberInfo, values, out shouldSkip, getDefaultValue:getDefaultValue);

            bool TryParseKvps(string astKvps, out KeyValuePair<string, string>[] rowAndPartitionKeys)
            {
                var parsings = astKvps
                    .Split(',')
                    .Select(
                        kvpString =>
                        {
                            if(!kvpString.StartsWith('{'))
                                return (false, default(KeyValuePair<string, string>));
                            if(!kvpString.EndsWith('}'))
                                return (false, default(KeyValuePair<string, string>));
                            var strippedKvpString = kvpString.Substring(1, kvpString.Length -2);
                            var parts = strippedKvpString.Split(':');
                            if(parts.Length != 2)
                                return (false, default(KeyValuePair<string, string>));
                            return (true, parts[0].PairWithKey(parts[1]));
                        })
                    .ToArray();

                if(parsings.Any(parsing => !parsing.Item1))
                {
                    rowAndPartitionKeys = default;
                    return false;
                }
                rowAndPartitionKeys = parsings
                    .SelectWhere()
                    .ToArray();
                return true;
            }
        }

    }
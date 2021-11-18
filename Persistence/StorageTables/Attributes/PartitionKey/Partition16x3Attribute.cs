using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Azure.Persistence;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Linq.Expressions;
using EastFive.Reflection;


namespace EastFive.Persistence.Azure.StorageTables
{
    public class Partition16x3Attribute : Attribute,
        IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey, IGenerateAzureStorageTablePartitionIndex
    {
        public string GeneratePartitionKey(string rowKey, object value, MemberInfo memberInfo)
        {
            return GetValue(rowKey);
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            // discard since generated from id
            return entity;
        }

        public string ComputePartitionKey(object refKey, MemberInfo memberInfo, string rowKey, 
            params KeyValuePair<MemberInfo, object>[] extraValues)
        {
            return GetValue(rowKey);
        }

        public static string GetValue(string rowKey)
        {
            return rowKey.Substring(0, 3);
        }

        public IEnumerable<string> GeneratePartitionKeys(Type type, int skip, int top)
        {
            return Enumerable
                .Range(skip, top)
                .Select((paritionNum) => paritionNum.ToString("X3").ToLower());
        }

        public string GeneratePartitionIndex(MemberInfo member, string rowKey)
        {
            return GetValue(rowKey);
        }
    }
}

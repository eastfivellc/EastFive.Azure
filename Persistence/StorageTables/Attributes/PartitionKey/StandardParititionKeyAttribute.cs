using BlackBarLabs.Persistence.Azure;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class StandardParititionKeyAttribute : Attribute,
       IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey
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

        public EntityType AssignPartitionKey<EntityType>(EntityType entity, string rowKey, string partitionKey, MemberInfo memberInfo)
        {
            // discard since generated from id
            return entity;
        }

        public static string GetValue(string rowKey)
        {
            return rowKey.GeneratePartitionKey();
        }

        public IEnumerable<string> GeneratePartitionKeys(Type type, int skip, int top)
        {
            return Enumerable
                .Range(
                    -1 * (KeyExtensions.PartitionKeyRemainder - 1),
                    (KeyExtensions.PartitionKeyRemainder * 2) - 1)
                .Select(index => index.ToString());
        }
    }
}

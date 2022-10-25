using EastFive.Linq.Expressions;
using EastFive.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class PartitionByIdAttribute : Attribute,
        IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey, IGenerateAzureStorageTablePartitionIndex
    {
        public string GeneratePartitionKey(string rowKey, object entityValue, MemberInfo memberInfo)
        {
            var memberValue = memberInfo.GetValue(entityValue);
            var strValue = IdLookupAttribute.RowKey(this.GetType(), memberInfo.GetPropertyOrFieldType(), memberValue);
            return strValue;
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            if (Guid.TryParse(value, out Guid valueGuid))
            {
                memberInfo.SetValue(ref entity, valueGuid);
            }
            return entity;
        }

        public string ComputePartitionKey(object refKey, MemberInfo memberInfo, string rowKey,
            params KeyValuePair<MemberInfo, object>[] extraValues)
        {
            var idValue = extraValues.Where(extraValue => extraValue.Key == memberInfo).First().Value;
            var strValue = IdLookupAttribute.RowKey(this.GetType(), memberInfo.GetPropertyOrFieldType(), idValue);
            return strValue;
        }

        public IEnumerable<string> GeneratePartitionKeys(Type type, int skip, int top)
        {
            throw new NotImplementedException();
        }

        public string GeneratePartitionIndex(MemberInfo member, string rowKey)
        {
            return RowKeyAttribute.GenerateRowKeyIndexEx(member, this.GetType());
        }
    }
}

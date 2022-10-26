using EastFive.Extensions;
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
        public bool StoresFullValue
        {
            get; set;
        } = true;

        public string GeneratePartitionKey(string rowKey, object entityValue, MemberInfo memberInfo)
        {
            var memberValue = memberInfo.GetValue(entityValue);
            var strValue = IdLookupAttribute.RowKey(this.GetType(), memberInfo.GetPropertyOrFieldType(), memberValue);
            return strValue;
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            if (!StoresFullValue)
                return entity;

            var objValue = IdLookupAttribute.ParseKey(this.GetType(), memberInfo.GetPropertyOrFieldType(), value);
            memberInfo.SetValue(ref entity, objValue);
            return entity;
        }

        public string ComputePartitionKey(object refKey, MemberInfo memberInfo, string rowKey,
            params KeyValuePair<MemberInfo, object>[] extraValues)
        {
            var strValue = IdLookupAttribute.RowKey(this.GetType(), memberInfo.GetPropertyOrFieldType(), refKey);
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

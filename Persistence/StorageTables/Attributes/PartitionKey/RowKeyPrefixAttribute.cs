using System;
using System.Collections.Generic;
using System.Reflection;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class RowKeyPrefixAttribute : Attribute,
        IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey,
        IGenerateAzureStorageTablePartitionIndex
    {
        private uint? charactersMaybe;
        public uint Characters
        {
            get
            {
                if (!charactersMaybe.HasValue)
                    return 2;
                return charactersMaybe.Value;
            }
            set
            {
                charactersMaybe = value;
            }
        }

        public string GeneratePartitionKey(string rowKey, object value, MemberInfo memberInfo)
        {
            return GetValue(rowKey, this.Characters);
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            // discard since generated from id
            return entity;
        }

        public string ComputePartitionKey(object refKey, MemberInfo memberInfo, string rowKey,
            params KeyValuePair<MemberInfo, object>[] extraValues)
        {
            return GetValue(rowKey, this.Characters);
        }

        public static string GetValue(string rowKey, uint characters)
        {
            if (characters <= 0)
                return "-";
            if (rowKey.IsNullOrWhiteSpace())
                return null;
            return rowKey.Substring(0, (int)characters);
        }

        public string GeneratePartitionIndex(MemberInfo member, string rowKey)
        {
            return GetValue(rowKey, this.Characters);
        }
    }

    public class RowKeyPrefix1Attribute : RowKeyPrefixAttribute
    {
        public RowKeyPrefix1Attribute()
        {
            Characters = 1;
        }
    }

    public class RowKeyPrefix2Attribute : RowKeyPrefixAttribute
    {
        public RowKeyPrefix2Attribute()
        {
            Characters = 2;
        }
    }

    public class RowKeyPrefix3Attribute : RowKeyPrefixAttribute
    {
        public RowKeyPrefix3Attribute()
        {
            Characters = 3;
        }
    }
}

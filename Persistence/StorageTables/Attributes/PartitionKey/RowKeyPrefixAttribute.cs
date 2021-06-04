using EastFive.Azure.Persistence.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class RowKeyPrefixAttribute : Attribute,
        IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey,
        StringKeyGenerator
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

        public IEnumerable<string> GeneratePartitionKeys(Type type, int skip, int top)
        {
            var formatter = $"X{this.Characters}";
            return Enumerable
                .Range(skip, top)
                .Select((paritionNum) => paritionNum.ToString(formatter).ToLower());
        }

        public IEnumerable<StringKey> GetKeys()
        {
            var formatter = $"X{this.Characters}";
            return Enumerable
                .Range(0, (int)Math.Pow(0x16, this.Characters))
                .Select((paritionNum) => new StringKey() { Equal = paritionNum.ToString(formatter).ToLower() });
        }
    }
}

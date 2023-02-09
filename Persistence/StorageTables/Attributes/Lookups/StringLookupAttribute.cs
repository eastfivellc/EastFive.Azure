using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Linq.Expressions;
using EastFive.Serialization;

namespace EastFive.Persistence.Azure.StorageTables
{
    public abstract class StringLookupAttribute : StorageLookupAttribute, IGenerateLookupKeys
    {
        public bool IgnoreNull { get; set; }

        public bool IgnoreNullWhiteSpace { get; set; }

        public override TResult GetLookupKeys<TResult>(MemberInfo decoratedMember,
                IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues,
            Func<IEnumerable<IRefAst>, TResult> onLookupValuesMatch,
            Func<string, TResult> onNoMatch)
        {
            if (lookupValues.Count() != 1)
                return onNoMatch($"{nameof(StringLookupAttribute)} only supports operations on a single member.");

            var lookupValue = lookupValues.Single();
            var rowKeyValue = lookupValue.Value;
            var propertyValueType = lookupValue.Key.GetMemberType();

            var rowKey = GetStringValue(lookupValue.Key, rowKeyValue, this.GetType());

            if (IgnoreNull && rowKey.IsDefaultOrNull())
                return onLookupValuesMatch(Enumerable.Empty<IRefAst>());

            if (IgnoreNullWhiteSpace && rowKey.IsNullOrWhiteSpace())
                return onLookupValuesMatch(Enumerable.Empty<IRefAst>());

            var partitionKey = GetPartitionKey(rowKey);
            return onLookupValuesMatch(new RefAst(rowKey, partitionKey).AsEnumerable());
        }

        internal static string GetStringValue(MemberInfo memberInfo, object memberValue, Type thisAttributeType)
        {
            var propertyValueType = memberInfo.GetMemberType();
            if (typeof(string).IsAssignableFrom(propertyValueType))
            {
                var stringValue = (string)memberValue;
                return stringValue;
            }
            if(propertyValueType.IsEnum)
            {
                var stringValue = Enum.GetName(propertyValueType, memberValue);
                return stringValue;
            }
            if (typeof(int) == propertyValueType)
            {
                var intValue = (int)memberValue;
                var stringValue = intValue.ToString();
                return stringValue;
            }

            var exMsg = $"{thisAttributeType.Name} is not implemented for type `{propertyValueType.FullName}`. " +
                $"Please update GetRowKeys on `{thisAttributeType.FullName}`.";
            throw new NotImplementedException(exMsg);
        }

        public abstract string GetPartitionKey(string rowKey);
    }

    public class StringMD5LookupAttribute : StringLookupAttribute
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

        public override string GetPartitionKey(string rowKey)
        {
            var hash = rowKey.MD5HashHex();
            return RowKeyPrefixAttribute.GetValue(hash, this.Characters);
        }

    }

    public class StringLookupHashXX32Attribute : StringLookupAttribute
    {
        public bool TrimWhitespace { get; set; }

        /// <summary>
        /// Limit 4
        /// </summary>
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

        public override string GetPartitionKey(string rowKey)
        {
            var rowKeyToHash = TrimWhitespace ?
                rowKey.Trim() : rowKey;
            var hash = rowKeyToHash.GetBytes().HashXX32();
            var hashStr = hash.ToString("X");
            return RowKeyPrefixAttribute.GetValue(hashStr, this.Characters);
        }
    }

    public class StringStandardPartitionLookupAttribute : StringLookupAttribute
    {
        public override string GetPartitionKey(string rowKey)
        {
            return StandardParititionKeyAttribute.GetValue(rowKey);
        }

    }

    public class StringConstantPartitionLookupAttribute : StringLookupAttribute
    {
        private string partition;
        public string Partition
        {
            get
            {
                if (partition.HasBlackSpace())
                    return partition;
                return "-";
            }
            set
            {
                partition = value;
            }
        }

        public override string GetPartitionKey(string rowKey)
        {
            return Partition;
        }
    }
}

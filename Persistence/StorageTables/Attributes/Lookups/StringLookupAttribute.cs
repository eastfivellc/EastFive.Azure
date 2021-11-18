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

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            if (lookupValues.Count() != 1)
                throw new ArgumentException("IdLookupAttribute only supports operations on a single member.", "lookupValues");

            var lookupValue = lookupValues.Single();
            var rowKeyValue = lookupValue.Value;
            var propertyValueType = lookupValue.Key.GetMemberType();

            if (typeof(string).IsAssignableFrom(propertyValueType))
            {
                var rowKey = (string)rowKeyValue;
                
                if (IgnoreNull && rowKey.IsDefaultOrNull())
                    return Enumerable.Empty<IRefAst>();

                if(IgnoreNullWhiteSpace && rowKey.IsNullOrWhiteSpace())
                    return Enumerable.Empty<IRefAst>();

                var partitionKey = GetPartitionKey(rowKey);
                return new RefAst(rowKey, partitionKey).AsEnumerable();
            }

            var exMsg = $"{this.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                $"Please override GetRowKeys on `{this.GetType().FullName}`.";
            throw new NotImplementedException(exMsg);
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
            var exMsg = $"{thisAttributeType.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                $"Please override GetRowKeys on `{thisAttributeType.GetType().FullName}`.";
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
            var hash = rowKey.GetBytes().HashXX32();
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

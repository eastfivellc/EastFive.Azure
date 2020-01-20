using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Extensions;
using EastFive.Linq.Expressions;
using EastFive.Serialization;

namespace EastFive.Persistence.Azure.StorageTables
{
    public abstract class StringLookupAttribute : StorageLookupAttribute, IGenerateLookupKeys
    {
        public override IEnumerable<MemberInfo> ProvideLookupMembers(MemberInfo decoratedMember)
        {
            return decoratedMember.AsEnumerable();
        }

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            if (lookupValues.Count() != 1)
                throw new ArgumentException("IdLookupAttribute only supports operations on a single member.", "lookupValues");

            var lookupValue = lookupValues.Single();
            var rowKeyValue = lookupValue.Value;
            var propertyValueType = lookupValue.Key.GetMemberType();
            if (!typeof(string).IsAssignableFrom(propertyValueType))
            {
                var exMsg = $"{this.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                    $"Please override GetRowKeys on `{this.GetType().FullName}`.";
                throw new NotImplementedException(exMsg);
            }
            var rowKey = (string)rowKeyValue;
            var partitionKey = GetPartitionKey(rowKey);
            return new RefAst(rowKey, partitionKey).AsEnumerable();
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

    public class StringStandardPartitionLookupAttribute : StringLookupAttribute
    {
        public override string GetPartitionKey(string rowKey)
        {
            return StandardParititionKeyAttribute.GetValue(rowKey);
        }

    }


}

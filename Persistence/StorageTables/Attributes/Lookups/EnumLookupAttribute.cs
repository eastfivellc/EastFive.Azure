using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Serialization;

namespace EastFive.Persistence.Azure.StorageTables
{
    public abstract class EnumLookupAttribute : StorageLookupAttribute, IGenerateLookupKeys
    {
        public bool IgnoreNull { get; set; }

        public string NullValue { get; set; } = "null";

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            if (lookupValues.Count() != 1)
                throw new ArgumentException("EnumLookupAttribute only supports operations on a single member.", "lookupValues");

            var lookupValue = lookupValues.Single();
            var rowKeyValue = lookupValue.Value;
            var propertyValueType = lookupValue.Key.GetMemberType();

            return GetLookup(propertyValueType, rowKeyValue);

            IEnumerable<IRefAst> GetLookup(Type lookupType, object lookupValue)
            {
                if (lookupType.IsEnum)
                {
                    var rowKey = Enum.GetName(lookupType, lookupValue);
                    var partitionKey = GetPartitionKey(rowKey);
                    return new RefAst(rowKey, partitionKey).AsEnumerable();
                }

                return lookupType.IsNullable(
                    baseType =>
                    {
                        if (lookupValue.NullableHasValue())
                            return GetLookup(baseType, lookupValue.GetNullableValue());

                        if (IgnoreNull)
                            return Enumerable.Empty<IRefAst>();

                        var partitionKey = GetPartitionKey(NullValue);
                        return new RefAst(NullValue, partitionKey).AsEnumerable();
                    },
                    () =>
                    {
                        var exMsg = $"{this.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                            $"Please override GetRowKeys on `{this.GetType().FullName}`.";
                        throw new NotImplementedException(exMsg);
                    });
            }
        }

        public abstract string GetPartitionKey(string rowKey);
    }

    public class EnumLookupHashXX32Attribute : EnumLookupAttribute
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


}

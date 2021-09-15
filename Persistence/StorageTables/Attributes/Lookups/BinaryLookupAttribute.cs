using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Extensions;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Serialization;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class BinaryLookupAttribute : StorageLookupAttribute, IGenerateLookupKeys
    {
        public enum StorageMasks
        {
            True,
            False,
            TrueAndFalse,
        }

        private StorageMasks? maskMaybe;
        public StorageMasks StorageMask
        {
            get
            {
                if (!maskMaybe.HasValue)
                    return StorageMasks.TrueAndFalse;
                return maskMaybe.Value;
            }
            set
            {
                maskMaybe = value;
            }
        }

        /// <summary>
        /// Since this is such a small set of rows, just use one table for all entries.
        /// </summary>
        /// <param name="memberInfo"></param>
        /// <returns></returns>
        protected override string GetLookupTableName(MemberInfo memberInfo)
        {
            return "BinaryLookupAttribute";
        }

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            if (lookupValues.Count() != 1)
                throw new ArgumentException("IdLookupAttribute only supports operations on a single member.", "lookupValues");

            var lookupValue = lookupValues.Single();

            var rowKeyValue = lookupValue.Value;
            var propertyValueType = lookupValue.Key.GetMemberType();
            if (!typeof(bool).IsAssignableFrom(propertyValueType))
            {
                var exMsg = $"{this.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                    $"Please override GetRowKeys on `{this.GetType().FullName}`.";
                throw new NotImplementedException(exMsg);
            }
            var rowKey = (bool)rowKeyValue;
            if (StorageMask == StorageMasks.True)
                if (!rowKey)
                    return Enumerable.Empty<IRefAst>();

            if (StorageMask == StorageMasks.False)
                if (rowKey)
                    return Enumerable.Empty<IRefAst>();

            var partitionKey = base.GetLookupTableName(decoratedMember);
            return new RefAst(rowKey.ToString(), partitionKey).AsEnumerable();
        }
    }

}

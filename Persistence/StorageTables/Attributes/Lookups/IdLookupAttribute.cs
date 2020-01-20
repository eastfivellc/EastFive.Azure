using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Extensions;
using EastFive.Linq.Expressions;

namespace EastFive.Persistence.Azure.StorageTables
{
    public abstract class IdLookupAttribute : StorageLookupAttribute, IGenerateLookupKeys
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

            var rowKey = RowKey(this.GetType(), propertyValueType, rowKeyValue);
            if (rowKey.IsDefaultNullOrEmpty())
                return Enumerable.Empty<IRefAst>();
            var partitionKey = GetPartitionKey(rowKey);
            var astRef = new RefAst(rowKey, partitionKey);
            return astRef.AsEnumerable();

            
        }

        internal static string RowKey(Type thisType, Type propertyValueType, object rowKeyValue)
        {
            if (typeof(Guid).IsAssignableFrom(propertyValueType))
            {
                var guidValue = (Guid)rowKeyValue;
                return guidValue.ToString("N");
            }
            if (typeof(IReferenceable).IsAssignableFrom(propertyValueType))
            {
                var refValue = (IReferenceable)rowKeyValue;
                if (refValue.IsDefaultOrNull())
                    return null;
                return refValue.id.ToString("N");
            }
            if (typeof(IReferenceableOptional).IsAssignableFrom(propertyValueType))
            {
                var referenceableOptional = (IReferenceableOptional)rowKeyValue;
                if (referenceableOptional.IsDefaultOrNull())
                    return null;
                if (!referenceableOptional.HasValue)
                    return null;
                return referenceableOptional.id.Value.ToString("N");
            }
            var exMsg = $"{thisType.Name} is not implemented for type `{propertyValueType.FullName}`. " +
                $"Please override GetRowKeys on `{thisType.FullName}`.";
            throw new NotImplementedException(exMsg);
        }

        public abstract string GetPartitionKey(string rowKey);
    }

    public class IdStandardPartitionLookupAttribute : IdLookupAttribute
    {
        public override string GetPartitionKey(string rowKey)
        {
            return StandardParititionKeyAttribute.GetValue(rowKey);
        }
    }

    public class IdPrefixLookupAttribute : IdLookupAttribute
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
            return RowKeyPrefixAttribute.GetValue(rowKey, this.Characters);
        }
    }


    public abstract class IdsLookupAttribute : StorageLookupAttribute, IGenerateLookupKeys
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
            if (rowKeyValue.IsNull())
                return new IRefAst[] { };
            var propertyValueType = rowKeyValue.GetType();

            return RowKeys()
                .Select(
                    rowKey =>
                    {
                        var partitionKey = GetPartitionKey(rowKey);
                        return new RefAst(rowKey, partitionKey);
                    });

            string[] RowKeys()
            {
                if (typeof(IReferences).IsAssignableFrom(propertyValueType))
                {
                    var references = (IReferences)rowKeyValue;
                    if (references.IsDefaultOrNull())
                        return new string[] { };
                    return references.ids.Select(id => id.ToString("N")).ToArray();
                }
                if (typeof(IReferenceable).IsAssignableFrom(propertyValueType))
                {
                    var reference = (IReferenceable)rowKeyValue;
                    if (reference.IsDefaultOrNull())
                        return new string[] { };
                    return reference.id.ToString("N").AsArray();
                }
                var exMsg = $"{this.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                    $"Please override GetRowKeys on `{this.GetType().FullName}`.";
                throw new NotImplementedException(exMsg);
            }
        }

        protected abstract string GetPartitionKey(string rowKey);
    }

    public class IdsPrefixLookupAttribute : IdsLookupAttribute
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

        protected override string GetPartitionKey(string rowKey)
        {
            return RowKeyPrefixAttribute.GetValue(rowKey, this.Characters);
        }
    }
}

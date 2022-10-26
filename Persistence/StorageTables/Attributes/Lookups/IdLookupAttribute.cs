using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EastFive.Azure.Persistence;
using EastFive.Extensions;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Serialization;

namespace EastFive.Persistence.Azure.StorageTables
{
    public abstract class IdLookupAttribute : StorageLookupAttribute, IGenerateLookupKeys
    {
        public override TResult GetLookupKeys<TResult>(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues,
            Func<IEnumerable<IRefAst>, TResult> onLookupValuesMatch,
            Func<string, TResult> onNoMatch)
        {
            if (lookupValues.Count() != 1)
                return onNoMatch($"{nameof(IdLookupAttribute)} only supports operations on a single member.");

            var lookupValue = lookupValues.Single();
            var rowKeyValue = lookupValue.Value;
            var propertyValueType = lookupValue.Key.GetMemberType();

            var rowKey = RowKey(this.GetType(), propertyValueType, rowKeyValue);
            if (rowKey.IsDefaultNullOrEmpty())
                return onLookupValuesMatch(Enumerable.Empty<IRefAst>());
            var partitionKey = GetPartitionKey(rowKey);
            var astRef = new RefAst(rowKey, partitionKey);
            return onLookupValuesMatch(astRef.AsEnumerable());
        }

        internal static string RowKey(Type thisType, Type propertyValueType, object rowKeyValue)
        {
            if (typeof(Guid).IsAssignableFrom(propertyValueType))
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
            return propertyValueType.IsNullable(
                nullableBase =>
                {
                    if (!rowKeyValue.NullableHasValue())
                        return null;
                    var valueFromNullable = rowKeyValue.GetNullableValue();
                    return RowKey(thisType, nullableBase, valueFromNullable);
                },
                () =>
                {
                    var exMsg = $"{thisType.Name} is not implemented for type `{propertyValueType.FullName}`. " +
                        $"Please override GetRowKeys on `{thisType.FullName}`.";
                    throw new NotImplementedException(exMsg);
                });
        }

        internal static object ParseKey(Type thisType, Type propertyValueType, string keyValue)
        {
            if (typeof(Guid).IsAssignableFrom(propertyValueType))
            {
                var guidValue = Guid.Parse(keyValue);
                return guidValue;
            }

            if (propertyValueType.IsSubClassOfGeneric(typeof(IRef<>)))
            {
                var guidValue = Guid.Parse(keyValue);
                return guidValue.BindToRefType(propertyValueType);
            }

            if (propertyValueType.IsSubClassOfGeneric(typeof(IRefOptional<>)))
            {
                if(Guid.TryParse(keyValue, out Guid guidValue))
                    return guidValue.AsOptional().BindToRefOptionalType(propertyValueType);

                return default(Guid?).BindToRefOptionalType(propertyValueType);
            }

            return propertyValueType.IsNullable(
                nullableBase =>
                {
                    return ParseKey(thisType, nullableBase, keyValue);
                },
                () =>
                {
                    var exMsg = $"{thisType.Name} is not implemented for type `{propertyValueType.FullName}`. " +
                        $"Please override GetRowKeys on `{thisType.FullName}`.";
                    throw new NotImplementedException(exMsg);
                });
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

    public class IdHashXX32LookupAttribute : IdLookupAttribute
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
        public override TResult GetLookupKeys<TResult>(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues,
            Func<IEnumerable<IRefAst>, TResult> onLookupValuesMatch,
            Func<string, TResult> onNoMatch)
        {
            if (lookupValues.Count() != 1)
                return onNoMatch("IdLookupAttribute only supports operations on a single member.");

            var lookupValue = lookupValues.Single();
            var rowKeyValue = lookupValue.Value;
            if (rowKeyValue.IsNull())
                return onLookupValuesMatch(new IRefAst[] { });
            var propertyValueType = rowKeyValue.GetType();

            var astRefs = RowKeys()
                .Select(
                    rowKey =>
                    {
                        var partitionKey = GetPartitionKey(rowKey);
                        return new RefAst(rowKey, partitionKey);
                    })
                .ToArray();
            return onLookupValuesMatch(astRefs);

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

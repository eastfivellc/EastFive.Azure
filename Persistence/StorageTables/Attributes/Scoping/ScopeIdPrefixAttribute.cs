using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EastFive.Extensions;
using EastFive.Linq.Expressions;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ScopePrefixAttribute : Attribute, IScopeKeys
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

        private double? orderMaybe;
        public double Order
        {
            get
            {
                if (!orderMaybe.HasValue)
                    return 0d;
                return orderMaybe.Value;
            }
            set
            {
                orderMaybe = value;
            }
        }

        public string Scope { get; set; }

        public ScopePrefixAttribute(string scope)
        {
            this.Scope = scope;
        }

        public string MutateKey(string currentKey, MemberInfo memberInfo, object memberValue, out bool ignore)
        {
            var idKey = RowKey(memberInfo.GetMemberType(), memberValue, this.GetType());
            if (idKey.IsNullOrWhiteSpace())
            {
                ignore = true;
                return null;
            }

            ignore = false;
            return RowKeyPrefixAttribute.GetValue(idKey, this.Characters);
        }

        internal static string RowKey(Type propertyValueType, object rowKeyValue, Type thisAttributeType)
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
            if (typeof(string).IsAssignableFrom(propertyValueType))
            {
                var rowKey = (string)rowKeyValue;
                return rowKey;
            }

            var exMsg = $"{thisAttributeType.Name} is not implemented for type `{propertyValueType.FullName}`. " +
                $"Please override GetRowKeys on `{thisAttributeType.FullName}`.";
            throw new NotImplementedException(exMsg);
        }
    }
}

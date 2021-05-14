using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using EastFive.Linq.Expressions;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ExternalIntegrationKeyLookupAttribute : ScopedLookupAttribute, IScopeKeys
    {
        internal const string ExternalIntegrationRowScoping = "External__Integration__Scope__Row";
        private const string ExternalIntegrationPartionScoping = "External__Integration__Scope__Parition";

        public ExternalIntegrationKeyLookupAttribute()
            : base(ExternalIntegrationRowScoping, ExternalIntegrationPartionScoping)
        {
        }

        public double Order { get; set; }

        public string Scope => ExternalIntegrationPartionScoping;

        public string MutateKey(string currentKey, MemberInfo memberInfo, object memberValue, out bool ignore)
        {
            var rowKey = ScopePrefixAttribute.RowKey(memberInfo.GetMemberType(), memberValue, this.GetType());
            if (rowKey.IsNullOrWhiteSpace())
            {
                ignore = true;
                return currentKey;
            }
            ignore = false;
            return $"{currentKey}{rowKey}";
        }

    }

    public class ExternalIntegrationIdAttribute : Attribute, IScopeKeys
    {
        public string Scope { get; set; }

        public double Order { get; set; }

        public ExternalIntegrationIdAttribute()
        {
            this.Scope = ExternalIntegrationKeyLookupAttribute.ExternalIntegrationRowScoping;
        }

        protected string GetStringValue(MemberInfo key, object valueOuter, out bool ignore)
        {
            return GetStringFromType(key.GetMemberType(), valueOuter, out ignore);

            string GetStringFromType(Type memberType, object value, out bool ignoreInner)
            {
                ignoreInner = false;
                if (typeof(Guid).IsAssignableFrom(memberType))
                {
                    var guidValue = (Guid)value;
                    return guidValue.ToString("N");
                }
                if (typeof(IReferenceable).IsAssignableFrom(memberType))
                {
                    var referenceableValue = (IReferenceable)value;
                    return referenceableValue.id.ToString("N");
                }
                if (typeof(string).IsAssignableFrom(memberType))
                {
                    var stringValue = (string)value;
                    return stringValue;
                }
                if (typeof(Guid?).IsAssignableFrom(memberType))
                {
                    var guidMaybeValue = (Guid?)value;
                    if (!guidMaybeValue.HasValue)
                    {
                        ignoreInner = true;
                        return string.Empty;
                    }
                    return GetStringFromType(typeof(Guid), guidMaybeValue.Value, out ignoreInner);
                }
                if (typeof(IReferenceableOptional).IsAssignableFrom(memberType))
                {
                    var guidMaybeValue = (IReferenceableOptional)value;
                    if (!guidMaybeValue.HasValue)
                    {
                        ignoreInner = true;
                        return string.Empty;
                    }
                    return GetStringFromType(typeof(Guid), guidMaybeValue.id.Value, out ignoreInner);
                }
                throw new ArgumentException(
                        $"Integration ID of type {memberType.FullName} is not supported.");
            }
        }

        public string MutateKey(string currentKey, MemberInfo key, object value, out bool ignore)
        {
            var valueStr = GetStringValue(key, value, out ignore);
            if (ignore)
                return currentKey;
            return currentKey; // This should be the only partition modifier $"{valueStr}_{currentKey}";
        }
    }
}

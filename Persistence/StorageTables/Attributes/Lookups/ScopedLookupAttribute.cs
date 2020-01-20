using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Expressions;

namespace EastFive.Persistence.Azure.StorageTables
{
    public abstract class ScopedLookupAttribute : StorageLookupAttribute,
        IGenerateLookupKeys
    {
        public string Scope { get; set; }

        public interface IScope
        {
            string Scope { get; }

            IRefAst MutateReference(IRefAst lookupKeyCurrent, MemberInfo key, object value, out bool ignore);
        }

        public class ScopingAttribute : Attribute, IScope
        {
            public string Scope { get; set; }

            public bool Facet { get; set; } = false;

            public ScopingAttribute(string scope)
            {
                this.Scope = scope;
            }

            public IRefAst MutateReference(IRefAst lookupKeyCurrent, MemberInfo key, object value, out bool ignore)
            {
                ignore = false;
                var valueStr = GetStringValue(key, value);
                if (this.Facet)
                    return lookupKeyCurrent.RowKey.AsAstRef(valueStr);
                return $"{lookupKeyCurrent.RowKey}{valueStr}".AsAstRef(lookupKeyCurrent.PartitionKey);
            }

            protected virtual string GetStringValue(MemberInfo key, object value)
            {
                if (typeof(Guid).IsAssignableFrom(key.GetMemberType()))
                {
                    var guidValue = (Guid)value;
                    return guidValue.ToString("N");
                }
                return (string)value;
            }
        }

        public class Scoping2Attribute : ScopingAttribute
        {
            public Scoping2Attribute(string scope) : base(scope)
            {
            }
        }

        #region IGenerateLookupKeys

        public override IEnumerable<MemberInfo> ProvideLookupMembers(MemberInfo decoratedMember)
        {
            return decoratedMember.DeclaringType
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(
                    (member) =>
                    {
                        var scopeMatches = member
                            .GetAttributesInterface<IScope>()
                            .Where(attr => attr.Scope == this.Scope);
                        return scopeMatches.Any();
                    })
                .Append(decoratedMember);
        }

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            var self = lookupValues.Where(kvp => kvp.Key == decoratedMember).First();
            var startingRef = StartingReference(self.Key, self.Value, out bool ignore)
                .PairWithValue(ignore);
            if (ignore)
                return Enumerable.Empty<IRefAst>();
            var finalRefAndIgnoreKvp = lookupValues
                .NullToEmpty()
                .Where(kvp => kvp.Key != decoratedMember)
                .OrderBy(kvp => kvp.Key.Name)
                .Aggregate(
                    startingRef,
                    (lookupKeyCurrentAndIgnoreKvp, memberInfoValueKvp) =>
                    {
                        var lookupKeyCurrent = lookupKeyCurrentAndIgnoreKvp.Key;
                        var ignoreCurrent = lookupKeyCurrentAndIgnoreKvp.Value;
                        if (ignoreCurrent)
                            return lookupKeyCurrentAndIgnoreKvp;
                        var memberInfo = memberInfoValueKvp.Key;
                        var value = memberInfoValueKvp.Value;
                        var scopings = memberInfo
                            .GetAttributesInterface<IScope>()
                            .Where(attr => attr.Scope == this.Scope)
                            .ToArray();

                        if (!scopings.Any())
                            throw new ArgumentException(
                                $"{memberInfo.DeclaringType.FullName}..{memberInfo.Name} is not a scoped parameter" +
                                "and should not have been included in the query.");
                        // TODO: Error for duplicate matches?
                        var scoping = scopings.First();

                        return scoping
                            .MutateReference(lookupKeyCurrent, memberInfo, value, out bool ignoreNext)
                            .PairWithValue(ignoreNext);
                    });
            var finalRef = finalRefAndIgnoreKvp.Key;
            var ignoreInChain = finalRefAndIgnoreKvp.Value;
            if(ignoreInChain)
                return Enumerable.Empty<IRefAst>();
            return finalRef.AsEnumerable();
        }

        #endregion

        public abstract IRefAst StartingReference(MemberInfo key, object value, out bool ignore);
    }

    public class ScopedPrefixAttribute : ScopedLookupAttribute
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

        public override IRefAst StartingReference(MemberInfo memberInfo, object memberValue, out bool ignore)
        {
            var rowKey = RowKey(memberInfo.GetMemberType(), memberValue);
            if(rowKey.IsNullOrWhiteSpace())
            {
                ignore = true;
                return null;
            }
            var partitionKey = RowKeyPrefixAttribute.GetValue(rowKey, this.Characters);
            ignore = false;
            return rowKey.AsAstRef(partitionKey);
        }

        protected string RowKey(Type propertyValueType, object rowKeyValue)
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

            var exMsg = $"{this.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                $"Please override GetRowKeys on `{this.GetType().FullName}`.";
            throw new NotImplementedException(exMsg);
        }
    }

    public class ExternalIntegrationKeyLookupAttribute : ScopedPrefixAttribute
    {
        internal const string ExternalIntegrationScoping = "External__Integration__Scope";

        public ExternalIntegrationKeyLookupAttribute()
        {
            this.Scope = ExternalIntegrationScoping;
        }

        public override IRefAst StartingReference(MemberInfo memberInfo, object memberValue, out bool ignore)
        {
            var rowKey = RowKey(memberInfo.GetMemberType(), memberValue);
            if (rowKey.IsNullOrWhiteSpace())
            {
                ignore = true;
                return null;
            }
            ignore = false;
            var partitionKey = RowKeyPrefixAttribute.GetValue(rowKey, this.Characters);
            return rowKey.AsAstRef(partitionKey);
        }
    }

    public class ExternalIntegrationIdAttribute : Attribute, ScopedLookupAttribute.IScope
    {
        public string Scope { get; set; }

        public ExternalIntegrationIdAttribute()
        {
            this.Scope = ExternalIntegrationKeyLookupAttribute.ExternalIntegrationScoping;
        }

        public IRefAst MutateReference(IRefAst lookupKeyCurrent, MemberInfo key, object value, out bool ignore)
        {
            var valueStr = GetStringValue(key, value, out ignore);
            if (ignore)
                return lookupKeyCurrent;
            return lookupKeyCurrent.RowKey.AsAstRef($"{valueStr}_{lookupKeyCurrent.PartitionKey}");
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
                    if(!guidMaybeValue.HasValue)
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
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Expressions;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ScopedLookupAttribute : StorageLookupAttribute,
        IGenerateLookupKeys
    {
        public string RowScope { get; set; }

        public string PartitionScope { get; set; }

        public ScopedLookupAttribute(string rowScope, string partitionScope)
        {
            this.RowScope = rowScope;
            this.PartitionScope = partitionScope;
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
                            .GetAttributesInterface<IScopeKeys>()
                            .Where(
                                attr =>
                                {
                                    if (attr.Scope == this.RowScope)
                                        return true;
                                    if (attr.Scope == this.PartitionScope)
                                        return true;
                                    return false;
                                });
                        return scopeMatches.Any();
                    })
                .Append(decoratedMember);
        }

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            var lookupRowKey = ScopedPartitionAttribute.ComputeKey(decoratedMember.DeclaringType,
                memberInfo =>
                {
                    return memberInfo
                        .GetAttributesInterface<IScopeKeys>(multiple: true)
                        .Where(attr => attr.Scope == this.RowScope)
                        .First(
                            (attr, next) => attr.PairWithKey(memberInfo),
                            () => default(KeyValuePair<MemberInfo, IScopeKeys>?));
                },
                lookupValues,
                out bool ignore);

            if (ignore)
                return Enumerable.Empty<IRefAst>();

            var lookupPartitionKey = ScopedPartitionAttribute.ComputeKey(decoratedMember.DeclaringType,
                memberInfo =>
                {
                    return memberInfo
                        .GetAttributesInterface<IScopeKeys>(multiple: true)
                        .Where(attr => attr.Scope == this.PartitionScope)
                        .First(
                            (attr, next) => attr.PairWithKey(memberInfo),
                            () => default(KeyValuePair<MemberInfo, IScopeKeys>?));
                },
                lookupValues,
                out ignore);

            if (ignore)
                return Enumerable.Empty<IRefAst>();

            return lookupRowKey.AsAstRef(lookupPartitionKey).AsEnumerable();
        }

        #endregion

    }

    


}

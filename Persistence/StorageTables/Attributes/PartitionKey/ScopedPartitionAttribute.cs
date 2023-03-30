﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using static EastFive.Persistence.Azure.StorageTables.ParititionKeyAttribute;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ScopedPartitionAttribute : Attribute,
        IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey, IProvideTableQuery, IGenerateAzureStorageTablePartitionIndex
    {
        public const string Scoping = "__PartitionKeyScoping__";

        public string GeneratePartitionKey(string rowKey, object value, MemberInfo decoratedMember)
        {
            var lookupValues = decoratedMember.DeclaringType.GetMembers()
                .Where(prop => prop.ContainsAttributeInterface<IScopeKeys>())
                .Select(prop => prop.GetValue(value).PairWithKey(prop))
                .ToArray();
            return ComputePartitionKey(default, decoratedMember, rowKey, lookupValues);
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            // TODO: pass these values into each of the scopings
            return entity;
        }

        public string ComputePartitionKey(object refKey, MemberInfo decoratedMember, string rowKey,
            params KeyValuePair<MemberInfo, object>[] lookupValues)
        {
            return ComputePartitionKey(decoratedMember.DeclaringType, lookupValues, out MemberInfo[] discard);
        }

        public string ProvideTableQuery<TEntity>(MemberInfo decoratedMember,
            Assignment[] assignments,
            out Func<TEntity, bool> postFilter,
            out string[] assignmentsUsed)
        {
            postFilter = (e) => true;
            var lookupValues = assignments
                .Select(assignment => assignment.member.PairWithValue(assignment.value))
                .ToArray();
            var partitionValue = ComputePartitionKey(decoratedMember.DeclaringType, lookupValues,
                out MemberInfo[] membersUsed);
            assignmentsUsed = membersUsed.Select(m => m.Name).ToArray();
            return ExpressionType.Equal.WhereExpression("PartitionKey", partitionValue);
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Expression<Func<TEntity, bool>> filter,
            out Func<TEntity, bool> postFilter)
        {
            Func<TEntity, bool> cacheFilter = (e) => true;
            postFilter = cacheFilter;
            if (filter.Body is BinaryExpression)
            {
                var filterAssignments = filter.Body.GetFilterAssignments<TEntity>().ToArray();
                var partitionValue = ComputePartitionKey(typeof(TEntity), filterAssignments,
                    out MemberInfo[] membersUsed);
                return ExpressionType.Equal.WhereExpression("PartitionKey", partitionValue);
            }
            return filter.ResolveFilter<TEntity>(out postFilter);

            //var result = filter.MemberComparison(
            //    (memberInAssignmentInfo, expressionType, partitionValue) =>
            //    {
            //        // TODO: if(memberInAssignmentInfo != memberInfo)?
            //        if (expressionType == ExpressionType.Equal)
            //            return ExpressionType.Equal.WhereExpression("PartitionKey", partitionValue);

            //        if (expressionType == ExpressionType.IsTrue)
            //            return string.Empty;

            //        throw new ArgumentException();
            //    },
            //    () =>
            //    {
            //        throw new ArgumentException();
            //    });
            //postFilter = cacheFilter;
            //return result;
        }

        private string ComputePartitionKey(Type declaringType,
            KeyValuePair<MemberInfo, object>[] lookupValues,
            out MemberInfo[] membersUsed)
        {
            var r = ComputeKey(declaringType,
                mi => mi.GetAttributesInterface<IScopeKeys>()
                    .Where(scoper => scoper.Scope == Scoping)
                    .First(
                        (scoper, next) => scoper.PairWithKey(mi),
                        () => default(KeyValuePair<MemberInfo, IScopeKeys>?)),
                lookupValues, out bool ignore, out membersUsed);
            if(ignore)
                throw new Exception("Cannot ignore partition key");
            return r;
        }

        internal static string ComputeKey(Type declaringType,
            Func<MemberInfo, KeyValuePair<MemberInfo, IScopeKeys>?> filter,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues,
            out bool ignore, out MemberInfo[] membersUsed)
        {
            bool allIgnored = false;
            var matchingKvps = declaringType
                .GetPropertyOrFieldMembers()
                .Where(mi => mi.ContainsAttributeInterface<IScopeKeys>())
                .Select(filter)
                .SelectWhereHasValue();

            membersUsed = matchingKvps.Select(kvp => kvp.Key).ToArray();

            var r = matchingKvps
                .OrderBy(memberKvp => memberKvp.Value.Order)
                .Aggregate(
                    string.Empty,
                    (keyCurrent, memberKvp) =>
                    {
                        if (allIgnored)
                            return keyCurrent;

                        var member = memberKvp.Key;
                        var scoping = memberKvp.Value;

                        return lookupValues
                            .NullToEmpty()
                            // Filter out row key parts of the query
                            .Where(kvp => kvp.Key == member)
                            .First<KeyValuePair<MemberInfo, object>, string>(
                                (memberInfoValueKvp, next) =>
                                {
                                    var memberInfo = memberInfoValueKvp.Key;
                                    var value = memberInfoValueKvp.Value;
                                    
                                    var partitionNext = scoping.MutateKey(keyCurrent, 
                                        memberInfo, value, out allIgnored);
                                    return partitionNext; // $"{keyCurrent}{partitionNext}";
                                },
                                () => throw new ArgumentException($"{member.DeclaringType}..{member.Name} " +
                                    " has not been included in the query."));
                    });
            ignore = allIgnored;
            return r;
        }

        public IEnumerable<string> GeneratePartitionKeys(Type type, int skip, int top)
        {
            // TODO: 
            throw new NotImplementedException();
        }

        public string GeneratePartitionIndex(MemberInfo member, string rowKey)
        {
            var propertyValueType = member.GetPropertyOrFieldType();
            var message = $"`{this.GetType().FullName}` Cannot generate index of type `{propertyValueType.FullName}` on `{member.DeclaringType.FullName}..{member.Name}`";
            throw new NotImplementedException(message);
        }
    }

}

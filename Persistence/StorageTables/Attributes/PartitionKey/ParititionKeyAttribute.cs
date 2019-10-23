﻿using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence.Azure;
using EastFive.Extensions;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ParititionKeyAttribute : Attribute,
        IModifyAzureStorageTablePartitionKey, 
        IProvideTableQuery,
        ParititionKeyAttribute.IModifyPartitionScope
    {
        public interface IModifyPartitionScope
        {
            string GenerateScopedPartitionKey(MemberInfo memberInfo, object memberValue);
        }

        public class ScopeAttribute : Attribute, IModifyPartitionScope
        {
            public string GenerateScopedPartitionKey(MemberInfo memberInfo, object memberValue)
            {
                if (memberValue.IsDefaultOrNull())
                    return string.Empty;
                if (typeof(string).IsAssignableFrom(memberValue.GetType()))
                    return (string)memberValue;
                if (typeof(Guid).IsAssignableFrom(memberValue.GetType()))
                {
                    var guidValue = (Guid)memberValue;
                    return guidValue.ToString("N");
                }
                if (typeof(IReferenceable).IsAssignableFrom(memberValue.GetType()))
                {
                    var refValue = (IReferenceable)memberValue;
                    return refValue.id.ToString("N");
                }
                return (string)memberValue;
            }
        }

        public string GeneratePartitionKey(string rowKey, object value, MemberInfo memberInfo)
        {
            return value.GetType()
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(member => member.ContainsAttributeInterface<IModifyPartitionScope>())
                .OrderBy(member => member.Name)
                .Aggregate(string.Empty,
                    (current, memberPartitionScoping) =>
                    {
                        var partitionScoping = memberPartitionScoping.GetAttributeInterface<IModifyPartitionScope>();
                        var partitionValue = memberInfo.GetValue(value);
                        var nextPartitionScope = partitionScoping.GenerateScopedPartitionKey(memberPartitionScoping, partitionValue);

                        if (current.IsNullOrWhiteSpace())
                            return nextPartitionScope;

                        return $"{current}___{nextPartitionScope}";
                    });
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            if (memberInfo.GetPropertyOrFieldType().IsAssignableFrom(typeof(string)))
                memberInfo.SetValue(ref entity, value);

            // otherwise, discard ...?
            return entity;
        }

        private IEnumerable<KeyValuePair<MemberInfo, object>> GetFilterAssignments<TEntity>(Expression filter)
        {
            return filter.MemberComparison(
                (memberInfo, expressionType, value) =>
                {
                    if (expressionType == ExpressionType.Equal)
                        return memberInfo.PairWithValue(value).AsArray();

                    // don't error here since it could be non-partition queries
                    // (and if they are partition queries the failure will be noted when the matchup occurs)
                    return new KeyValuePair<MemberInfo, object>[] { };
                },
                () =>
                {
                    if (filter is BinaryExpression)
                    {
                        var binaryExpression = filter as BinaryExpression;

                        // Since the partition key is a hash, we can only AndAlso values
                        if (binaryExpression.NodeType == ExpressionType.AndAlso)
                        {
                            var leftFilter = GetFilterAssignments<TEntity>(binaryExpression.Left);
                            var rightFilter = GetFilterAssignments<TEntity>(binaryExpression.Right);

                            return leftFilter.Concat(rightFilter);
                        }
                    }
                    // don't error here since it could be non-partition queries
                    // (and if they are partition queries the failure will be noted when the matchup occurs)
                    return new KeyValuePair<MemberInfo, object>[] { };
                });
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Expression<Func<TEntity, bool>> filter, 
            out Func<TEntity, bool> postFilter)
        {
            if (filter.Body is BinaryExpression)
            {
                var filterAssignments = GetFilterAssignments<TEntity>(filter.Body);

                var scopedMembers = memberInfo.DeclaringType
                    .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                    .Where(member => member.ContainsAttributeInterface<IModifyPartitionScope>())
                    .ToDictionary(member => member.Name);

                postFilter = (e) => true;
                var partitionValue = filterAssignments
                    .OrderBy(kvp => kvp.Key.Name)
                    .Aggregate(string.Empty,
                        (current, filterAssignment) =>
                        {
                            if (!scopedMembers.ContainsKey(filterAssignment.Key.Name))
                                throw new ArgumentException();

                            var memberPartitionScoping = scopedMembers[filterAssignment.Key.Name];
                            var partitionScoping = memberPartitionScoping.GetAttributeInterface<IModifyPartitionScope>();
                            var nextPartitionScope = partitionScoping
                                .GenerateScopedPartitionKey(memberPartitionScoping, filterAssignment.Value);

                            if (current.IsNullOrWhiteSpace())
                                return nextPartitionScope;

                            return $"{current}___{nextPartitionScope}";
                        });
                return ExpressionType.Equal.WhereExpression("Partition", partitionValue);
            }
            return filter.ResolveFilter<TEntity>(out postFilter);
        }

        public string GenerateScopedPartitionKey(MemberInfo memberInfo, object memberValue)
        {
            if (memberValue.IsDefaultOrNull())
                return string.Empty;
            if(typeof(string).IsAssignableFrom(memberValue.GetType()))
                return (string)memberValue;
            if (typeof(Guid).IsAssignableFrom(memberValue.GetType()))
            {
                var guidValue = (Guid)memberValue;
                return guidValue.ToString("N");
            }
            if (typeof(IReferenceable).IsAssignableFrom(memberValue.GetType()))
            {
                var refValue = (IReferenceable)memberValue;
                return refValue.id.ToString("N");
            }
            return (string)memberValue;
        }
    }

}

using BlackBarLabs.Persistence.Azure;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ScopedPartitionAttribute : Attribute,
        IModifyAzureStorageTablePartitionKey, IComputeAzureStorageTablePartitionKey, IProvideTableQuery
    {
        public interface IScope
        {
            string MutateReference(string currentPartition, MemberInfo key, object value);

            double Order { get; }
        }

        public string GeneratePartitionKey(string rowKey, object value, MemberInfo decoratedMember)
        {
            var lookupValues = decoratedMember.DeclaringType.GetMembers()
                .Where(prop => prop.ContainsAttributeInterface<IScope>())
                .Select(prop => prop.GetValue(value).PairWithKey(prop))
                .ToArray();
            return ComputePartitionKey(default, decoratedMember, rowKey, lookupValues);
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            // discard since generated from id
            return entity;
        }

        public string ComputePartitionKey(object refKey, MemberInfo decoratedMember, string rowKey,
            params KeyValuePair<MemberInfo, object>[] lookupValues)
        {
            return ComputePartitionKey(decoratedMember.DeclaringType, lookupValues);
        }

        public string ComputePartitionKey(Type declaringType,
            params KeyValuePair<MemberInfo, object>[] lookupValues)
        {
            return declaringType.GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<IScope>())
                .OrderBy(member => member
                    .GetAttributesInterface<IScope>()
                    .First().Order)
                .Select(
                    member =>
                    {
                        return lookupValues
                            .NullToEmpty()
                            // Filter out row key parts of the query
                            .Where(kvp => kvp.Key == member)
                            .First<KeyValuePair<MemberInfo, object>, KeyValuePair<MemberInfo, object>>(
                                    (attr, next) => attr,
                                    () => throw new ArgumentException($"{member.DeclaringType}..{member.Name} " +
                                        " has not been included in the query."));
                    })
                .Aggregate(
                    string.Empty,
                    (partitionCurrent, memberInfoValueKvp) =>
                    {
                        var memberInfo = memberInfoValueKvp.Key;
                        var value = memberInfoValueKvp.Value;
                        var scoping = memberInfo.GetAttributesInterface<IScope>().First();

                        return scoping.MutateReference(partitionCurrent, memberInfo, value);
                    });
        }

        public IEnumerable<string> GeneratePartitionKeys(Type type, int skip, int top)
        {
            throw new NotImplementedException();
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo, 
            Expression<Func<TEntity, bool>> filter,
            out Func<TEntity, bool> postFilter)
        {
            Func<TEntity, bool> cacheFilter = (e) => true;
            var result = filter.MemberComparison(
                (memberInAssignmentInfo, expressionType, partitionValue) =>
                {
                    // TODO: if(memberInAssignmentInfo != memberInfo)?
                    if (expressionType == ExpressionType.Equal)
                        return ExpressionType.Equal.WhereExpression("PartitionKey", partitionValue);

                    throw new ArgumentException();
                },
                () =>
                {
                    throw new ArgumentException();
                });
            postFilter = cacheFilter;
            return result;
        }

        public string ProvideTableQuery<TEntity>(MemberInfo decoratedMember, Assignment[] assignments,
            out Func<TEntity, bool> postFilter)
        {
            postFilter = (e) => true;
            var lookupValues = assignments
                .Select(assignment => assignment.member.PairWithValue(assignment.value))
                .ToArray();
            var partitionValue = ComputePartitionKey(decoratedMember.DeclaringType, lookupValues);
            return ExpressionType.Equal.WhereExpression("PartitionKey", partitionValue);
        }
    }

    public class ScopePartitionIdAttribute : Attribute, ScopedPartitionAttribute.IScope
    {
        public double Order { get; set; }

        public string MutateReference(string currentPartition, MemberInfo key, object value)
        {
            var idValue = IdLookupAttribute.RowKey(this.GetType(), key.GetPropertyOrFieldType(), value);
            return $"{currentPartition}{idValue}";
        }
    }

    public class ScopePartitionDateTime : Attribute, ScopedPartitionAttribute.IScope
    {
        public double Order { get; set; }

        public double OffsetHours { get; set; }

        /// <summary>
        /// In Seconds
        /// </summary>
        public double SpanSize { get; set; }

        public string MutateReference(string currentPartition, MemberInfo key, object value)
        {
            var dateTime = (DateTime)value;
            if (!OffsetHours.IsDefault())
                dateTime = dateTime + TimeSpan.FromHours(OffsetHours);
            var spanSize = TimeSpan.FromSeconds(this.SpanSize);
            var dtPartition  = DateTimeLookupAttribute
                .ComputeLookupKey(dateTime, spanSize);
            return $"{currentPartition}{dtPartition}";
        }
    }
}

using BlackBarLabs.Extensions;
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
        ParititionKeyAttribute.IModifyPartitionScope,
        IComputeAzureStorageTablePartitionKey,
        IGenerateAzureStorageTablePartitionIndex
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

        

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Expression<Func<TEntity, bool>> filter, 
            out Func<TEntity, bool> postFilter)
        {
            if (filter.Body is BinaryExpression)
            {
                var filterAssignments = filter.Body.GetFilterAssignments<TEntity>();

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
                return ExpressionType.Equal.WhereExpression("PartitionKey", partitionValue);
            }
            return filter.ResolveFilter<TEntity>(out postFilter);
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Assignment[] assignments,
            out Func<TEntity, bool> postFilter)
        {
            postFilter = (e) => true;

            var filterAssignments = assignments
                .Select(assignment => assignment.member.PairWithValue(assignment.value));

            return TableQuery(memberInfo, filterAssignments);
        }

        private string TableQuery(MemberInfo memberInfo,
            IEnumerable<KeyValuePair<MemberInfo, object>> filterAssignments)
        {
            var scopedMembers = memberInfo.DeclaringType
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(member => member.ContainsAttributeInterface<IModifyPartitionScope>())
                .ToDictionary(member => member.Name);

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
            return ExpressionType.Equal.WhereExpression("PartitionKey", partitionValue);
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

        public string ComputePartitionKey(object memberValue,
            MemberInfo memberInfo, string rowKey,
            params KeyValuePair<MemberInfo, object>[] extraValues)
        {
            if(memberValue is string)
                return memberValue as string;

            throw new Exception(
                $"{nameof(ParititionKeyAttribute)} only works on string members." + 
                $" Issue is on {memberInfo.DeclaringType.FullName}..{memberInfo.Name}.");
        }

        public string GeneratePartitionIndex(MemberInfo member, string rowKey)
        {
            return RowKeyAttribute.GenerateRowKeyIndexEx(member, this.GetType());
        }
    }

}

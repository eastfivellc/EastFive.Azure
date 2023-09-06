using System;
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

namespace EastFive.Persistence.Azure.StorageTables
{
    public class PartitionKeyAttribute : Attribute,
        IModifyAzureStorageTablePartitionKey, 
        IProvideTableQuery,
        PartitionKeyAttribute.IModifyPartitionScope,
        IComputeAzureStorageTablePartitionKey,
        IGenerateAzureStorageTablePartitionIndex
    {
        public double Order { get; set; }

        public interface IModifyPartitionScope
        {
            double Order { get; }
            string GenerateScopedPartitionKey(MemberInfo memberInfo, object memberValue);
        }

        private static string AppendScoping(string current, string nextPartitionScope)
        {
            return $"{current}___{nextPartitionScope}";
        }

        private IEnumerable<MemberInfo> GetScopedMembers(Type type)
        {
            return type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(member => member.ContainsAttributeInterface<IModifyPartitionScope>());
        }

        public string GeneratePartitionKey(string rowKey, object value, MemberInfo memberInfo)
        {
            return GetScopedMembers(value.GetType())
                .OrderBy(member => member.Name)
                .Aggregate(string.Empty,
                    (current, memberPartitionScoping) =>
                    {
                        var partitionScoping = memberPartitionScoping.GetAttributeInterface<IModifyPartitionScope>();
                        var partitionValue = memberInfo.GetValue(value);
                        var nextPartitionScope = partitionScoping.GenerateScopedPartitionKey(memberPartitionScoping, partitionValue);

                        if (current.IsNullOrWhiteSpace())
                            return nextPartitionScope;

                        return AppendScoping(current, nextPartitionScope);
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

                var scopedMembers = GetScopedMembers(memberInfo.DeclaringType)
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

                            return AppendScoping(current, nextPartitionScope);
                        });
                return ExpressionType.Equal.WhereExpression("PartitionKey", partitionValue);
            }
            return filter.ResolveFilter<TEntity>(out postFilter);
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Assignment[] assignments,
            out Func<TEntity, bool> postFilter,
            out string[] assignmentsUsed)
        {
            postFilter = (e) => true;

            var scopedMembers = memberInfo.DeclaringType
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .TryWhere((MemberInfo member, out IModifyPartitionScope partitionModifer) =>
                    member.TryGetAttributeInterface(out partitionModifer))
                .ToArray();

            var filterAssignments = assignments
                .Collate(scopedMembers,
                    (assignment, scopedMember) => (assignment.member, assignment.value, scopeModifier: scopedMember.@out),
                    tpl => tpl.member.Name)
                .ToArray();
                //.Where(assignment => scopedMembers.ContainsKey(assignment.member.Name))
                //.Select(assignment => assignment.member.PairWithValue(assignment.value));

            assignmentsUsed = filterAssignments.Select(tpl => tpl.member.Name).ToArray();
            return TableQuery(filterAssignments);
        }

        private string TableQuery(
            IEnumerable<(MemberInfo member, object value, IModifyPartitionScope modifier)> filterAssignments)
        {
            var partitionValue = filterAssignments
                .OrderBy(tpl => tpl.modifier.Order)
                .Aggregate(string.Empty,
                    (current, filterAssignment) =>
                    {
                        var nextPartitionScope = filterAssignment.modifier
                            .GenerateScopedPartitionKey(filterAssignment.member, filterAssignment.value);

                        if (current.IsNullOrWhiteSpace())
                            return nextPartitionScope;

                        return AppendScoping(current, nextPartitionScope);
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
                $"{nameof(PartitionKeyAttribute)} only works on string members." + 
                $" Issue is on {memberInfo.DeclaringType.FullName}..{memberInfo.Name}.");
        }

        public string GeneratePartitionIndex(MemberInfo member, string rowKey)
        {
            return RowKeyAttribute.GenerateRowKeyIndexEx(member, this.GetType());
        }
    }

}

using EastFive.Reflection;
using EastFive.Linq.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class LastModifiedAttribute : Attribute,
        IModifyAzureStorageTableLastModified, IProvideTableQuery
    {
        public DateTimeOffset GenerateLastModified(object value, MemberInfo memberInfo)
        {
            var lastModifiedValue = memberInfo.GetValue(value);
            var lastModifiedType = memberInfo.GetMemberType();
            if (typeof(DateTimeOffset).IsAssignableFrom(lastModifiedType))
            {
                var dateTimeValue = (DateTimeOffset)lastModifiedValue;
                return dateTimeValue;
            }
            var message = $"`{this.GetType().FullName}` Cannot determine last modified from type `{lastModifiedType.FullName}` on `{memberInfo.DeclaringType.FullName}..{memberInfo.Name}`";
            throw new NotImplementedException(message);
        }

        public EntityType ParseLastModfied<EntityType>(EntityType entity, DateTimeOffset value, MemberInfo memberInfo)
        {
            var memberType = memberInfo.GetMemberType();
            if (memberType.IsAssignableFrom(typeof(DateTimeOffset)))
            {
                memberInfo.SetValue(ref entity, value);
                return entity;
            }
            if (memberType.IsAssignableFrom(typeof(DateTime)))
            {
                var dateTime = value.UtcDateTime;
                memberInfo.SetValue(ref entity, dateTime);
                return entity;
            }
            var message = $"`{this.GetType().FullName}` Cannot determine last modified from type `{memberType.FullName}` on `{memberInfo.DeclaringType.FullName}..{memberInfo.Name}`";
            throw new NotImplementedException(message);
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Expression<Func<TEntity, bool>> filter, out Func<TEntity, bool> postFilter)
        {
            var query = filter.ResolveFilter(out postFilter);
            return query;
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo, Assignment[] assignments, out Func<TEntity, bool> postFilter, out string[] assignmentsUsed)
        {
            postFilter = (e) => true;

            (assignmentsUsed, Assignment assignment) = StorageQueryAttribute.GetAssignment(memberInfo, assignments);
            var assignmentName = "Timestamp";
            var value = assignment.value;
            if (!assignment.member.TryGetAttributeInterface(out IPersistInEntityProperty serializer))
            {
                var assignmentQuery = assignment.type.WhereExpression(assignmentName, value);
                return assignmentQuery;
            }

            return serializer
                .ConvertValue<TEntity>(assignment.member, assignment.value, default)
                .Aggregate("",
                    (queryCurrent, kvp) =>
                    {
                        var itemValue = kvp.Value.PropertyAsObject;
                        var newFilter = assignment.type.WhereExpression(kvp.Key, itemValue);
                        if (queryCurrent.IsNullOrWhiteSpace())
                            return newFilter;

                        return TableQuery.CombineFilters(queryCurrent, TableOperators.And, newFilter);
                    });
        }
    }
}

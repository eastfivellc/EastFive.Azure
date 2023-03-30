using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using EastFive;
using EastFive.Reflection;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Linq;
using EastFive.Extensions;
using Microsoft.Azure.Cosmos.Table;
using DocumentFormat.OpenXml.Office2021.Excel.NamedSheetViews;

namespace EastFive.Persistence
{
    public class StorageQueryAttribute : Attribute, IProvideTableQuery
    {
        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Expression<Func<TEntity, bool>> filter,
            out Func<TEntity, bool> postFilter)
        {
            var query = filter.ResolveFilter(out postFilter);
            return query;
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Assignment[] assignments,
            out Func<TEntity, bool> postFilter,
            out string[] assignmentsUsed)
        {
            postFilter = (e) => true;

            (assignmentsUsed, Assignment assignment) = GetAssignment(memberInfo, assignments);
            var assignmentName = assignment.member.GetTablePropertyName();
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

        public static (string[] assignmentsUsed, Assignment memberValue) GetAssignment(
            MemberInfo memberInfo, Assignment[] assignments)
        {
            return assignments
                .Where(assignment =>
                    assignment.member.DeclaringType == memberInfo.DeclaringType &&
                    assignment.member.Name == memberInfo.Name)
                .First<Assignment, (string[], Assignment)>(
                    (assignment, next) =>
                    {
                        return (assignment.member.Name.AsArray(), assignment);
                    },
                    () =>
                    {
                        var msg = $"{memberInfo.Name} is not in assignments:{assignments.Select(a => a.member.Name).Join(',')}";
                        throw new ArgumentException(msg);
                    });
        }
    }
}


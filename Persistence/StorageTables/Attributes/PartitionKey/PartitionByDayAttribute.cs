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
    public class PartitionByDayAttribute : Attribute,
        IModifyAzureStorageTablePartitionKey,
        IComputeAzureStorageTablePartitionKey,
        IProvideTableQuery
    {
        public string GeneratePartitionKey(string rowKey, object value, MemberInfo memberInfo)
        {
            return ComputePartitionKey(rowKey, value, memberInfo);
        }

        public static string ComputePartitionKey(string rowKey, object value, MemberInfo memberInfo)
        {
            var dateTimeValueObj = memberInfo.GetValue(value);
            return ComputePartitionKey(dateTimeValueObj);
        }

        public EntityType ParsePartitionKey<EntityType>(EntityType entity, string yearDayString, MemberInfo memberInfo)
        {
            if (memberInfo.GetPropertyOrFieldType().IsAssignableFrom(typeof(DateTime)))
            {
                var partitionDate = yearDayString.MatchRegexInvoke("(?<year>[0-9]+)_(?<day>[0-9]+)",
                    (year, day) => new { year, day },
                    (yds) =>
                    {
                        if (!yds.Any())
                            return new DateTime(1, 1, 1);

                        var yd = yds.First();
                        int dayOfYear = int.Parse(yd.day);
                        int year = int.Parse(yd.year);
                        var date = new DateTime(year, 1, 1).AddDays(dayOfYear - 1);
                        return date;
                    });
                memberInfo.SetValue(ref entity, partitionDate);
            }

            // otherwise, discard ...?
            return entity;
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
                    return filter.ResolveFilter<TEntity>(out cacheFilter);
                });
            postFilter = cacheFilter;
            return result;
        }

        public string ProvideTableQuery<TEntity>(MemberInfo memberInfo,
            Assignment[] assignments,
            out Func<TEntity, bool> postFilter)
        {
            postFilter = (e) => true;
            return assignments.Aggregate("",
                (current, assignment) =>
                {
                    if (assignment.type == ExpressionType.Equal)
                        return ExpressionType.Equal.WhereExpression("PartitionKey", assignment.value);

                    throw new ArgumentException();
                });
        }

        public string ComputePartitionKey(object memberValue, MemberInfo memberInfo, string rowKey, params KeyValuePair<MemberInfo, object>[] extraValues)
        {
            return ComputePartitionKey(memberValue);
        }

        public IEnumerable<string> GeneratePartitionKeys(Type type, int skip, int top)
        {
            return new string[] { };
        }

        public static string ComputePartitionKey(object dateTimeValueObj)
        {
            if (dateTimeValueObj.IsDefaultOrNull())
                return "1_1";
            if (dateTimeValueObj.GetType().IsNullable())
            {
                if (!dateTimeValueObj.NullableHasValue())
                    return "1_1";
                dateTimeValueObj = dateTimeValueObj.GetNullableValue();
            }
            var dateTimeValue = (DateTime)dateTimeValueObj;
            return $"{dateTimeValue.Year}_{dateTimeValue.DayOfYear}";
        }
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Linq;
using EastFive.Extensions;

namespace EastFive.Persistence.Azure.StorageTables
{
    public static class TableQueryExtensions
    {
        public static string GetTablePropertyName(this MemberInfo member)
        {
            return member
                .GetAttributesInterface<IPersistInAzureStorageTables>()
                .First(
                    (attr, next) =>
                    {
                        var tablePropertyName = attr.GetTablePropertyName(member);
                        return tablePropertyName;
                    },
                    () =>
                    {
                        return member
                            .GetAttributesInterface<IModifyAzureStorageTablePartitionKey>()
                            .First(
                                (attr, next) =>
                                {
                                    var tablePropertyName = "PartitionKey";
                                    return tablePropertyName;
                                },
                                () =>
                                {
                                    return member.Name;
                                });
                    });
        }

        public static object AsTableQuery<TEntity>(this string whereExpression,
            IList<string> selectColumns = default)
        {
            return GetTableQuery<TEntity>(whereExpression, selectColumns);
        }

        public static object GetTableQuery<TEntity>(string whereExpression = null,
            IList<string> selectColumns = default)
        {
            var query = typeof(TEntity)
                .GetAttributesInterface<IProvideTable>()
                .First(
                    (tableProvider, next) =>
                    {
                        var tableQueryDyanmic = tableProvider.GetTableQuery<TEntity>(
                            whereExpression:whereExpression, selectColumns:selectColumns);
                        return tableQueryDyanmic;
                    },
                    () =>
                    {
                        var tableQuery = new TableQuery<TableEntity<TEntity>>();
                        if (!selectColumns.IsDefaultNullOrEmpty())
                            tableQuery.SelectColumns = selectColumns;
                        if (!whereExpression.HasBlackSpace())
                            return (object)tableQuery;
                        return (object)tableQuery.Where(whereExpression);
                    });
            return query;
        }

        private static MemberInfo ResolveMemberInType(Type entityType, MemberExpression expression, out Func<object, object> getValue)
        {
            var member = expression.Member;
            getValue = (obj) => member.GetValue(obj);
            if (entityType.GetMembers().Contains(member))
            {
                if (member.ContainsAttributeInterface<IPersistInAzureStorageTables>())
                    return member;

                var partitionAttributes = member.GetAttributesInterface<IModifyAzureStorageTablePartitionKey>();
                if (partitionAttributes.Any())
                {
                    var partitionAttribute = partitionAttributes.First();
                    var getRowKey = member.DeclaringType
                        .GetPropertyOrFieldMembers()
                        .Where(m => m.ContainsAttributeInterface<IModifyAzureStorageTableRowKey>())
                        .First<MemberInfo, Func<object, string>>(
                            (rowKeyMember, next) =>
                            {
                                var rowKeyAttr = rowKeyMember.GetAttributesInterface<IModifyAzureStorageTableRowKey>().First();
                                return (obj) => rowKeyAttr.GenerateRowKey(obj, rowKeyMember);
                            },
                            () => (obj) => null);
                    getValue = (obj) => partitionAttribute.GeneratePartitionKey(getRowKey(obj), obj,member);
                    return member;
                }

                throw new ArgumentException($"{member.DeclaringType.FullName}..{member.Name} is not storage property/field.");
            }

            if (expression.Expression is MemberExpression)
                return ResolveMemberInType(entityType, expression.Expression as MemberExpression, out getValue);

            throw new ArgumentException($"{member.DeclaringType.FullName}..{member.Name} is not a property/field of {entityType.FullName}.");
        }

        public static string ExpressionTypeToQueryComparison(this ExpressionType comparision)
        {
            if (ExpressionType.Equal == comparision)
                return QueryComparisons.Equal;
            if (ExpressionType.Assign == comparision) // why not
                return QueryComparisons.Equal;
            if (ExpressionType.GreaterThan == comparision)
                return QueryComparisons.GreaterThan;
            if (ExpressionType.GreaterThanOrEqual == comparision)
                return QueryComparisons.GreaterThanOrEqual;
            if (ExpressionType.LessThan == comparision)
                return QueryComparisons.LessThan;
            if (ExpressionType.LessThanOrEqual == comparision)
                return QueryComparisons.LessThanOrEqual;

            throw new ArgumentException($"{comparision} is not a supported query comparison.");
        }

        public static string WhereExpression(this ExpressionType comparision, string assignmentName, object assignmentValue)
        {
            var queryComparison = comparision.ExpressionTypeToQueryComparison();

            if (typeof(Guid?).IsInstanceOfType(assignmentValue))
                TableQuery.GenerateFilterConditionForGuid(assignmentName, queryComparison, (assignmentValue as Guid?).Value);

            if (typeof(Guid).IsInstanceOfType(assignmentValue))
                return TableQuery.GenerateFilterConditionForGuid(assignmentName, queryComparison, (Guid)assignmentValue);

            if (typeof(IReferenceable).IsInstanceOfType(assignmentValue))
                return TableQuery.GenerateFilterConditionForGuid(assignmentName, queryComparison, ((IReferenceable)assignmentValue).id);

            if (typeof(bool).IsInstanceOfType(assignmentValue))
                return TableQuery.GenerateFilterConditionForBool(assignmentName, queryComparison, (bool)assignmentValue);

            if (typeof(DateTime).IsInstanceOfType(assignmentValue))
                return TableQuery.GenerateFilterConditionForDate(assignmentName, queryComparison, (DateTime)assignmentValue);

            if (typeof(int).IsInstanceOfType(assignmentValue))
                return TableQuery.GenerateFilterConditionForInt(assignmentName, queryComparison, (int)assignmentValue);

            if (typeof(string).IsInstanceOfType(assignmentValue))
                return TableQuery.GenerateFilterCondition(assignmentName, queryComparison, (string)assignmentValue);

            throw new NotImplementedException($"No filter condition created for type {assignmentValue.GetType().FullName}");
        }

        private static object ResolveUnaryExpression<TEntity>(UnaryExpression expression, out Func<TEntity, bool> postFilter)
        {
            // TODO: var expressionModifier = expression.NodeType == ExpressionType.NotEqual;
            var operand = expression.Operand;
            if (!(operand is MemberExpression))
                throw new NotImplementedException($"Unary expression of type {operand.GetType().FullName} is not supported.");

            var memberOperand = operand as MemberExpression;
            var assignmentMember = ResolveMemberInType(typeof(TEntity), memberOperand, out Func<object,object> getValue);
            var assignmentName = assignmentMember.GetTablePropertyName();

            postFilter = (entity) => true;
            var query = GetTableQuery<TEntity>();
            var nullableHasValueProperty = typeof(Nullable<>).GetProperty("HasValue");
            if (memberOperand.Member == nullableHasValueProperty)
            {
                postFilter =
                        (entity) =>
                        {
                            var nullableValue = getValue(entity);
                            var hasValue = nullableHasValueProperty.GetValue(nullableValue);
                            var hasValueBool = (bool)hasValue;
                            return !hasValueBool;
                        };
                return query;

                //if (expression.NodeType == ExpressionType.Not)
                //{
                //    var whereExpr = TableQuery.GenerateFilterCondition(assignmentName, QueryComparisons.Equal, "");
                //    var whereQuery = query.Where(whereExpr);
                //    return whereQuery;
                //}
                //{
                //    var whereExpr = TableQuery.GenerateFilterCondition(assignmentName, QueryComparisons.NotEqual, "");
                //    var whereQuery = query.Where(whereExpr);
                //    return whereQuery;
                //}
            }

            var refOptionalHasValueProperty = typeof(EastFive.IReferenceableOptional).GetProperty("HasValue");
            if (memberOperand.Member == refOptionalHasValueProperty)
            {
                postFilter =
                    (entity) =>
                    {
                        var nullableValue = getValue(entity);
                        var hasValue = refOptionalHasValueProperty.GetValue(nullableValue);
                        var hasValueBool = (bool)hasValue;
                        return !hasValueBool;
                    };
                return query;

                //if (expression.NodeType == ExpressionType.Not)
                //{
                //    var whereExpr = TableQuery.GenerateFilterCondition(assignmentName, QueryComparisons.Equal, null);
                //    var whereQuery = query.Where(whereExpr);
                //    return whereQuery;
                //}
                //{
                //    var whereExpr = TableQuery.GenerateFilterCondition(assignmentName, QueryComparisons.NotEqual, "");
                //    var whereQuery = query.Where(whereExpr);
                //    return whereQuery;
                //}
            }

            throw new NotImplementedException($"Unary expression of type {memberOperand.Member.DeclaringType.FullName}..{memberOperand.Member.Name} is not supported.");
        }

        private static object ResolveConstantExpression<TEntity>(ConstantExpression expression)
        {
            if (!typeof(bool).IsAssignableFrom(expression.Type))
                throw new NotImplementedException($"Constant expression of type {expression.Type.FullName} is not supported.");

            var value = (bool)expression.Value;
            if (!value)
                throw new Exception("Query for nothing?");

            var query = GetTableQuery<TEntity>();
            return query;
        }

        private static string ResolveMemberExpressionFilter<TEntity>(MemberExpression expression)
        {
            var assignmentMember = ResolveMemberInType(typeof(TEntity), expression, out Func<object, object> getValue);
            if (!typeof(bool).IsAssignableFrom(expression.Type))
                throw new NotImplementedException($"Member expression of type {expression.Type.FullName} is not supported.");

            var assignmentName = assignmentMember.GetTablePropertyName();
            var filter = TableQuery.GenerateFilterConditionForBool(assignmentName, QueryComparisons.Equal, true);
            return filter;
        }

        private static object ResolveMemberExpression<TEntity>(MemberExpression expression)
        {
            var filter = ResolveMemberExpressionFilter<TEntity>(expression);
            var whereQuery = GetTableQuery<TEntity>(filter);
            return whereQuery;
        }

        private static string ResolveBinaryExpressionFilter<TEntity>(BinaryExpression binaryExpression, out Func<TEntity, bool> postFilter)
        {
            if(binaryExpression.NodeType == ExpressionType.AndAlso)
            {
                if (!(binaryExpression.Left is Expression<Func<TEntity, bool>>))
                    throw new Exception();
                var leftExpr = binaryExpression.Left as Expression<Func<TEntity, bool>>;
                var leftFilter = ResolveFilter<TEntity>(leftExpr, out Func<TEntity, bool> postFilterLeft);

                if (!(binaryExpression.Right is Expression<Func<TEntity, bool>>))
                    throw new Exception();
                var rightExpr = binaryExpression.Right as Expression<Func<TEntity, bool>>;
                var rightFilter = ResolveFilter<TEntity>(rightExpr, out Func<TEntity, bool> postFilterRight);

                var queryComparison = binaryExpression.NodeType.ExpressionTypeToQueryComparison();

                postFilter = (e) => postFilterLeft(e) && postFilterRight(e);
                var filterAnd = $"{leftFilter} {queryComparison} {rightFilter}";
            }

            if (!(binaryExpression.Left is MemberExpression))
                throw new ArgumentException("TableQuery expression left side must be an MemberExpression");

            var memberBeingAssigned = binaryExpression.Left as MemberExpression;
            var assignmentMember = ResolveMemberInType(typeof(TEntity), memberBeingAssigned, out Func<object, object> getValue);
            var assignmentValue = binaryExpression.Right.ResolveExpression();
            var assignmentName = assignmentMember.GetTablePropertyName();

            var whereFilter = binaryExpression.NodeType.WhereExpression(assignmentName, assignmentValue);
            postFilter = (e) => true;
            return whereFilter;
        }

        private static object ResolveBinaryExpression<TEntity>(BinaryExpression binaryExpression, out Func<TEntity, bool> postFilter)
        {
            var whereFilter = ResolveBinaryExpressionFilter<TEntity>(binaryExpression, out postFilter);
            var whereQuery = GetTableQuery<TEntity>(whereFilter);
            return whereQuery;
        }

        public static object ResolveQuery<TEntity>(this Expression<Func<TEntity, bool>> filter,
            out Func<TEntity, bool> postFilter)
        {
            if (filter.Body is UnaryExpression)
                return ResolveUnaryExpression(filter.Body as UnaryExpression, out postFilter);

            if (filter.Body is BinaryExpression)
                return ResolveBinaryExpression(filter.Body as BinaryExpression, out postFilter);

            postFilter = (entity) => true;
            if (filter.Body is ConstantExpression)
                return ResolveConstantExpression<TEntity>(filter.Body as ConstantExpression);

            if (filter.Body is MemberExpression)
                return ResolveMemberExpression<TEntity>(filter.Body as MemberExpression);

            throw new ArgumentException($"{filter.Body.GetType().FullName} is not a supported TableQuery expression type.");
        }

        public static string ResolveFilter<TEntity>(this Expression<Func<TEntity, bool>> filter, 
            out Func<TEntity, bool> postFilter)
        {
            if (filter.Body is UnaryExpression)
            {
                var discard = ResolveUnaryExpression<TEntity>(filter.Body as UnaryExpression, out postFilter);
                return null;
            }

            if (filter.Body is BinaryExpression)
                return ResolveBinaryExpressionFilter<TEntity>(filter.Body as BinaryExpression, out postFilter);

            postFilter = (entity) => true;
            if (filter.Body is ConstantExpression)
                return default;

            if (filter.Body is MemberExpression)
                return ResolveMemberExpressionFilter<TEntity>(filter.Body as MemberExpression);


            throw new ArgumentException($"{filter.Body.GetType().FullName} is not a supported TableQuery expression type.");
        }

    }
}

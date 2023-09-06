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
    public abstract class ScopedKeyAttribute : Attribute, IProvideTableQuery
    {
        protected abstract string KeyScoping { get; }

        protected abstract string KeyName { get; }

        protected string GenerateKey(object value, MemberInfo decoratedMember)
        {
            var lookupValues = decoratedMember.DeclaringType.GetMembers()
                .Where(prop => prop.ContainsAttributeInterface<IScopeKeys>())
                .Select(prop => prop.GetValue(value).PairWithKey(prop))
                .ToArray();
            return ComputeKey(decoratedMember, lookupValues);
        }

        public EntityType ParseKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            // TODO: pass these values into each of the scopings
            return entity;
        }

        public string ComputeKey(MemberInfo decoratedMember,
            params KeyValuePair<MemberInfo, object>[] lookupValues)
        {
            return ComputeKey(decoratedMember.DeclaringType, lookupValues, out MemberInfo[] discard);
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
            var keyValue = ComputeKey(decoratedMember.DeclaringType,
                lookupValues,
                out MemberInfo[] membersUsed);
            assignmentsUsed = membersUsed.Select(m => m.Name).ToArray();
            return ExpressionType.Equal.WhereExpression(KeyName, keyValue);
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
                var keyValue = ComputeKey(typeof(TEntity),
                    filterAssignments, out MemberInfo[] membersUsed);
                return ExpressionType.Equal.WhereExpression(KeyName, keyValue);
            }
            return filter.ResolveFilter<TEntity>(out postFilter);

            //var result = filter.MemberComparison(
            //    (memberInAssignmentInfo, expressionType, keyValue) =>
            //    {
            //        // TODO: if(memberInAssignmentInfo != memberInfo)?
            //        if (expressionType == ExpressionType.Equal)
            //            return ExpressionType.Equal.WhereExpression(KeyName, keyValue);

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

        private string ComputeKey(Type declaringType,
            KeyValuePair<MemberInfo, object>[] lookupValues,
            out MemberInfo[] membersUsed)
        {
            var scoping = KeyScoping;
            var key = ComputeKey(declaringType,
                mi => mi.GetAttributesInterface<IScopeKeys>()
                    .Where(scoper => scoper.Scope == scoping)
                    .First(
                        (scoper, next) => scoper.PairWithKey(mi),
                        () => default(KeyValuePair<MemberInfo, IScopeKeys>?)),
                lookupValues, out bool ignore, out membersUsed);
            if(ignore)
                throw new Exception($"{this.GetType().FullName}:Cannot ignore a key value");
            var keySanitized = key.AsAzureStorageTablesSafeKey();
            return keySanitized;
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
                                    
                                    var keyNext = scoping.MutateKey(keyCurrent, 
                                        memberInfo, value, out allIgnored);
                                    return keyNext;
                                },
                                () => throw new ArgumentException($"{member.DeclaringType}..{member.Name} " +
                                    " has not been included in the query."));
                    });
            ignore = allIgnored;
            return r;
        }

        public string GenerateIndex(MemberInfo member)
        {
            var propertyValueType = member.GetPropertyOrFieldType();
            var message = $"`{this.GetType().FullName}` Cannot generate index of type `{propertyValueType.FullName}` on `{member.DeclaringType.FullName}..{member.Name}`";
            throw new NotImplementedException(message);
        }
    }

}

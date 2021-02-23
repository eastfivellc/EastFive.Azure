using EastFive.Api;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace EastFive.Persistence.Azure.StorageTables
{
    public static class StorageQueryExtensions
    {
        [MutateIdQuery]
        public static IQueryable<TResource> StorageQueryById<TResource>(this IQueryable<TResource> query, IRef<TResource> resourceRef)
            where TResource : IReferenceable
        {
            if (!typeof(StorageQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(StorageQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var storageQuery = query as StorageQuery<TResource>;

            var condition = Expression.Call(
                typeof(ResourceQueryExtensions), nameof(StorageQueryExtensions.StorageQueryById), new Type[] { typeof(TResource) },
                query.Expression, Expression.Constant(resourceRef.id));

            var requestMessageNewQuery = storageQuery.FromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class MutateIdQueryAttribute : Attribute, IProvideQueryValues, IBuildStorageQueries
        {
            public (MemberInfo, object)[] BindStorageQueryValue(
                MethodInfo method,
                Expression[] arguments)
            {
                var idMember = method.DeclaringType
                    .GetPropertyOrFieldMembers()
                    .Where(field => field.ContainsAttributeInterface<IComputeAzureStorageTableRowKey>())
                    .First();
                var idValue = arguments.First().ResolveExpression();
                var value = (idMember, idValue);
                return value.AsArray();
            }

            public IEnumerable<Assignment> GetStorageValues(MethodInfo methodInfo, Expression[] methodArguments)
            {
                return methodInfo
                    .GetAttributeInterface<IBuildStorageQueries>()
                    .BindStorageQueryValue(methodInfo, methodArguments)
                    .Select(
                        assignment => new Assignment
                        {
                            member = assignment.Item1,
                            type = ExpressionType.Equal,
                            value = assignment.Item2,
                        });
            }
        }


    }
}

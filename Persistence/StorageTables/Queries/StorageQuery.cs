using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Linq.Expressions;
using Microsoft.AspNetCore.Http;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Api;
using EastFive.Extensions;
using EastFive.Reflection;

namespace EastFive.Persistence.Azure.StorageTables
{
    [StorageQueryInvocation]
    public class StorageQuery<TResource>
        : 
            EastFive.Linq.Queryable<
                TResource,
                StorageQuery<TResource>.StorageQueryProvideQuery>,
            IQueryable<TResource>,
            Linq.ISupplyQueryProvider<StorageQuery<TResource>>
    {
        public StorageQuery(AzureTableDriverDynamic storageDriver)
            : base(new StorageQueryProvideQuery(storageDriver))
        {
            this.StorageDriver = storageDriver;
        }

        private StorageQuery(AzureTableDriverDynamic storageDriver, Expression expr)
            : base(new StorageQueryProvideQuery(storageDriver), expr)
        {
            this.StorageDriver = storageDriver;
        }

        public AzureTableDriverDynamic StorageDriver { get; private set; }

        public StorageQuery<TRelatedResource> Related<TRelatedResource>()
        {
            return new StorageQuery<TRelatedResource>(this.StorageDriver);
        }

        public class StorageQueryProvideQuery :
            EastFive.Linq.QueryProvider<
                EastFive.Linq.Queryable<TResource,
                    StorageQuery<TResource>.StorageQueryProvideQuery>>
        {
            public StorageQueryProvideQuery(AzureTableDriverDynamic storageDriver)
                : base(
                    (queryProvider, type) => (queryProvider is StorageQuery<TResource>)?
                        (queryProvider as StorageQuery<TResource>).From()
                        :
                        new StorageQuery<TResource>(storageDriver),
                    (queryProvider, expression, type) => (queryProvider is StorageQuery<TResource>) ?
                        (queryProvider as StorageQuery<TResource>).FromExpression(expression)
                        :
                        new StorageQuery<TResource>(storageDriver, expression))
            {
            }

            public override object Execute(Expression expression)
            {
                return 1;
            }
        }

        internal virtual StorageQuery<TResource> FromExpression(Expression condition)
        {
            return new StorageQuery<TResource>(
                  this.StorageDriver,
                  condition);
        }

        internal virtual StorageQuery<TResource> From()
        {
            return new StorageQuery<TResource>(
                  this.StorageDriver);
        }

        public StorageQuery<TResource> ActivateQueryable(QueryProvider<StorageQuery<TResource>> provider, Type type)
        {
            return From();
        }

        public StorageQuery<TResource> ActivateQueryableWithExpression(QueryProvider<StorageQuery<TResource>> queryProvider,
            Expression expression, Type elementType)
        {
            return FromExpression(expression);
        }
    }

    public class StorageQueryInvocationAttribute : Attribute, IInstigatableGeneric
    {
        public virtual Task<IHttpResponse> InstigatorDelegateGeneric(Type type,
                IApplication httpApp, IHttpRequest routeData, ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            return type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .First()
                .GetParameters()
                .Aggregate<ParameterInfo, Func<object [], Task<IHttpResponse>>>(
                    (invocationParameterValues) =>
                    {
                        var StorageQuery = Activator.CreateInstance(type, invocationParameterValues);
                        return onSuccess(StorageQuery);
                    },
                    (next, invocationParameterInfo) =>
                    {
                        return (previousParams) =>
                        {
                            return httpApp.Instigate(routeData, invocationParameterInfo,
                                (invocationParameterValue) =>
                                {
                                    return next(previousParams.Prepend(invocationParameterValue).ToArray());
                                });
                        };
                    })
                .Invoke(new object[] { });
        }
    }

}

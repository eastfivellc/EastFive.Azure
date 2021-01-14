using EastFive.Api;
using EastFive.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure
{
    [StorageResources]
    public class StorageResources<TResource>
        :
            EastFive.Linq.Queryable<
                TResource,
                StorageResources<TResource>.StorageResourcesProvideQuery>,
            IQueryable<TResource>,
            EastFive.Linq.ISupplyQueryProvider<StorageResources<TResource>>
    {
        public StorageResources(IInvokeApplication invokeApplication)
            : base(new StorageResourcesProvideQuery(invokeApplication))
        {
            this.InvokeApplication = invokeApplication;
        }

        private StorageResources(IInvokeApplication invokeApplication, Expression expr)
            : base(new StorageResourcesProvideQuery(invokeApplication), expr)
        {
            this.InvokeApplication = invokeApplication;
        }

        public IInvokeApplication InvokeApplication { get; private set; }

        public StorageResources<TRelatedResource> Related<TRelatedResource>()
        {
            return new StorageResources<TRelatedResource>(this.InvokeApplication);
        }

        public class StorageResourcesProvideQuery :
            EastFive.Linq.QueryProvider<
                EastFive.Linq.Queryable<TResource,
                    EastFive.Persistence.Azure.StorageResources<TResource>.StorageResourcesProvideQuery>>
        {
            public StorageResourcesProvideQuery(IInvokeApplication invokeApplication)
                : base(
                    (queryProvider, type) => (queryProvider is StorageResources<TResource>) ?
                        (queryProvider as StorageResources<TResource>).From()
                        :
                        new StorageResources<TResource>(invokeApplication),
                    (queryProvider, expression, type) => (queryProvider is StorageResources<TResource>) ?
                        (queryProvider as StorageResources<TResource>).FromExpression(expression)
                        :
                        new StorageResources<TResource>(invokeApplication, expression))
            {
            }

            public override object Execute(Expression expression)
            {
                return 1;
            }
        }

        internal virtual StorageResources<TResource> FromExpression(Expression condition)
        {
            return new StorageResources<TResource>(
                  this.InvokeApplication,
                  condition);
        }

        internal virtual StorageResources<TResource> From()
        {
            return new StorageResources<TResource>(
                  this.InvokeApplication);
        }

        public StorageResources<TResource> ActivateQueryable(QueryProvider<StorageResources<TResource>> provider, Type type)
        {
            return From();
        }

        public StorageResources<TResource> ActivateQueryableWithExpression(QueryProvider<StorageResources<TResource>> queryProvider,
            Expression expression, Type elementType)
        {
            return FromExpression(expression);
        }
    }

    public class StorageResourcesAttribute : Attribute, IInstigatableGeneric
    {
        public virtual Task<IHttpResponse> InstigatorDelegateGeneric(Type type,
                IApplication httpApp, IHttpRequest request,
                ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            return type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .First()
                .GetParameters()
                .Aggregate<ParameterInfo, Func<object[], Task<IHttpResponse>>>(
                    (invocationParameterValues) =>
                    {
                        var requestMessage = Activator.CreateInstance(type, invocationParameterValues);
                        return onSuccess(requestMessage);
                    },
                    (next, invocationParameterInfo) =>
                    {
                        return (previousParams) =>
                        {
                            return httpApp.Instigate(request, invocationParameterInfo,
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

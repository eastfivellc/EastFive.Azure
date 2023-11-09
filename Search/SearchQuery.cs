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
using EastFive.Reflection;
using Microsoft.AspNetCore.Http;
using EastFive.Extensions;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents;

namespace EastFive.Azure.Search
{
    public interface IProvideSearchExpression<TResource>
    {
        IQueryable<TResource> FromExpression(Expression condition);
    }

    public class SearchQuery<TResource>
        :
            EastFive.Linq.Queryable<
                TResource,
                SearchQuery<TResource>.SearchQueryProvideQuery>,
            IQueryable<TResource>,
            Linq.ISupplyQueryProvider<SearchQuery<TResource>>,
            IProvideSearchExpression<TResource>
    {
        public SearchClient searchIndexClient;

        public SearchQuery(SearchClient searchIndexClient)
            : base(new SearchQueryProvideQuery(searchIndexClient))
        {
            this.searchIndexClient = searchIndexClient;
        }

        private SearchQuery(SearchClient searchIndexClient, Expression expr)
            : base(new SearchQueryProvideQuery(searchIndexClient), expr)
        {
            this.searchIndexClient = searchIndexClient;
        }

        public SearchQuery<TRelatedResource> Related<TRelatedResource>()
        {
            return new SearchQuery<TRelatedResource>(this.searchIndexClient);
        }

        public class SearchQueryProvideQuery :
            EastFive.Linq.QueryProvider<
                EastFive.Linq.Queryable<TResource,
                    EastFive.Azure.Search.SearchQuery<TResource>.SearchQueryProvideQuery>>
        {
            public SearchQueryProvideQuery(SearchClient searchIndexClient)
                : base(
                    (queryProvider, type) => (queryProvider is SearchQuery<TResource>) ?
                        (queryProvider as SearchQuery<TResource>).From()
                        :
                        new SearchQuery<TResource>(searchIndexClient),
                    (queryProvider, expression, type) => (queryProvider is SearchQuery<TResource>) ?
                        (queryProvider as SearchQuery<TResource>).FromExpression(expression)
                        :
                        new SearchQuery<TResource>(searchIndexClient, expression))
            {
            }

            public override object Execute(Expression expression)
            {
                return 1;
            }
        }

        internal virtual SearchQuery<TResource> FromExpression(Expression condition)
        {
            return new SearchQuery<TResource>(
                  this.searchIndexClient,
                  condition);
        }

        internal virtual SearchQuery<TResource> From()
        {
            return new SearchQuery<TResource>(
                  this.searchIndexClient);
        }

        public SearchQuery<TResource> ActivateQueryable(QueryProvider<SearchQuery<TResource>> provider, Type type)
        {
            return From();
        }

        public SearchQuery<TResource> ActivateQueryableWithExpression(QueryProvider<SearchQuery<TResource>> queryProvider,
            Expression expression, Type elementType)
        {
            return FromExpression(expression);
        }

        IQueryable<TResource> IProvideSearchExpression<TResource>.FromExpression(Expression condition)
            => FromExpression(condition);
    }



}

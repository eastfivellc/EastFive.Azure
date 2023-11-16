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
using Azure.Search.Documents.Models;

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
        public IList<Action<IDictionary<string, IList<FacetResult>>>> Facets;

        public static SearchQuery<TResource> FromIndex(SearchClient searchIndexClient)
        {
            var facets = new List<Action<IDictionary<string, IList<FacetResult>>>>();
            return new SearchQuery<TResource>(searchIndexClient, facets);
        }

        public SearchQuery(SearchClient searchIndexClient, IList<Action<IDictionary<string, IList<FacetResult>>>> facets)
            : base(new SearchQueryProvideQuery(searchIndexClient, facets))
        {
            this.searchIndexClient = searchIndexClient;
            this.Facets = facets;
        }

        private SearchQuery(SearchClient searchIndexClient, IList<Action<IDictionary<string, IList<FacetResult>>>> facets, Expression expr)
            : base(new SearchQueryProvideQuery(searchIndexClient, facets), expr)
        {
            this.searchIndexClient = searchIndexClient;
            this.Facets = facets;
        }

        public SearchQuery<TRelatedResource> Related<TRelatedResource>()
        {
            return new SearchQuery<TRelatedResource>(this.searchIndexClient, this.Facets);
        }

        public class SearchQueryProvideQuery :
            EastFive.Linq.QueryProvider<
                EastFive.Linq.Queryable<TResource,
                    EastFive.Azure.Search.SearchQuery<TResource>.SearchQueryProvideQuery>>
        {
            public SearchQueryProvideQuery(SearchClient searchIndexClient, IList<Action<IDictionary<string, IList<FacetResult>>>> facets)
                : base(
                    (queryProvider, type) => (queryProvider is SearchQuery<TResource>) ?
                        (queryProvider as SearchQuery<TResource>).From()
                        :
                        new SearchQuery<TResource>(searchIndexClient, facets),
                    (queryProvider, expression, type) => (queryProvider is SearchQuery<TResource>) ?
                        (queryProvider as SearchQuery<TResource>).FromExpression(expression)
                        :
                        new SearchQuery<TResource>(searchIndexClient, facets, expression))
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
                  this.searchIndexClient, this.Facets,
                  condition);
        }

        internal virtual SearchQuery<TResource> From()
        {
            return new SearchQuery<TResource>(
                  this.searchIndexClient, this.Facets);
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

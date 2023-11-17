using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Linq;
using EastFive.Reflection;
using EastFive.Collections;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence;
using EastFive.Serialization.Text;

namespace EastFive.Azure.Search
{
    public static class SearchQueryExtensions
    {

        public static IQueryable<T> GetQuery<T>(this SearchClient searchIndexClient)
        {
            return EastFive.Azure.Search.SearchQuery<T>.FromIndex(searchIndexClient);
        }

        public delegate TProp SearchPropertyDelegate<TItem, TProp>(IQueryable<TItem> items, out Func<TProp[]> getFacets);

        private static SearchOptions AppendFilterOption(this SearchOptions searchOptions, string filterToAppend)
        {
            var currentFilter = searchOptions.Filter;
            
            searchOptions.Filter = GetFilter();
            return searchOptions;
            string GetFilter()
            {
                if (currentFilter.HasBlackSpace())
                    return $"{currentFilter} and {filterToAppend}";
                return filterToAppend;
            }
        }

        public static IEnumerableAsync<(double?, T)> SearchQuery<T>(this IQueryable<T> query, string searchText)
        {
            var searchTextComplete = searchText.HasBlackSpace() ?
                $"{searchText}~"
                :
                "*";

            var searchClient = (query as SearchQuery<T>).searchIndexClient;

            var searchOptionsNaked = new SearchOptions
            {
                QueryType = SearchQueryType.Full,
            };

            var searchOptionsPopulated = query.Compile<SearchOptions, ICompileSearchOptions>(searchOptionsNaked,
                onRecognizedAttribute: (searchOptionsCurrent, queryProvider, methodInfo, expressions) =>
                {
                    var searchOptionsUpdated = queryProvider.GetSearchFilters(searchOptionsCurrent,
                        methodInfo, expressions);
                    return searchOptionsUpdated;
                },
                onUnrecognizedAttribute: (searchOptionsCurrent, unrecognizedMethod, methodArguments) =>
                {
                    if (unrecognizedMethod.Name == nameof(System.Linq.Queryable.Skip))
                    {
                        var skip = (int)methodArguments.First().ResolveExpression();
                        searchOptionsCurrent.Skip = skip;
                        return searchOptionsCurrent;
                    }
                    if (unrecognizedMethod.Name == nameof(System.Linq.Queryable.Take))
                    {
                        var limit = (int)methodArguments.First().ResolveExpression();
                        searchOptionsCurrent.Size = limit;
                        return searchOptionsCurrent;
                    }
                    if (unrecognizedMethod.Name == nameof(System.Linq.Queryable.Where))
                    {
                        return unrecognizedMethod.TryParseMemberAssignment(methodArguments,
                                (memberInfo, expressionType, memberValue) =>
                                {
                                    return searchOptionsCurrent
                                        .AppendFilterOption($"{memberInfo.Name} eq {memberValue}");
                                },
                                () => throw new ArgumentException(
                                    $"Could not parse `{unrecognizedMethod}`({methodArguments})"));
                    }
                    throw new ArgumentException($"Search cannot compile Method `{unrecognizedMethod.DeclaringType.FullName}..{unrecognizedMethod.Name}`");
                });

            return GetResultsAsync().FoldTask();

            async Task<IEnumerableAsync<(double?, T)>> GetResultsAsync()
            {
                if(typeof(T).TryGetAttributeInterface<IProvideSearchSerialization>(
                    out var searchSerializer))
                {
                    var searchResultsType = searchSerializer.BuildSearchResultsType<T>();
                    var method = typeof(SearchClient).GetMethod(
                        nameof(SearchClient.SearchAsync),
                        BindingFlags.Public | BindingFlags.Instance);
                    var methodGeneric = method.MakeGenericMethod(searchResultsType.AsArray());
                    var resultsObj = methodGeneric.Invoke(searchClient,
                        new object [] { searchTextComplete, searchOptionsPopulated, default(System.Threading.CancellationToken) });
                    var resultsTaskObj = resultsObj.CastAsTaskObjectAsync(out var discard);
                    var responseObj = await resultsTaskObj;

                    var queryItemsEnumAsyncObj = typeof(SearchQueryExtensions)
                        .GetMethod(nameof(CastSearchResult), BindingFlags.Public | BindingFlags.Static)
                        .MakeGenericMethod(new[] { searchResultsType, typeof(T) })
                        .Invoke(null, new object[] { responseObj, searchSerializer, query } );

                    var queryItemsEnumAsync = (IEnumerableAsync<(double?, T)>)queryItemsEnumAsyncObj;
                    return queryItemsEnumAsync;
                }

                var result = await searchClient.SearchAsync<T>(searchTextComplete, searchOptionsPopulated);
                var pageResultAsync = result.Value.GetResultsAsync();
                var pageEnumerator = pageResultAsync.GetAsyncEnumerator();
                return EnumerableAsync.Yield<(double?, T)>(
                    async (yieldReturn, yieldBreak) =>
                    {
                        if (!await pageEnumerator.MoveNextAsync())
                            return yieldBreak;

                        var searchResult = pageEnumerator.Current;
                        var doc = searchResult.Document;
                        var score = searchResult.Score;
                        return yieldReturn((score, doc));
                    });
            }
        }

        public static IEnumerableAsync<(double?, TResponse)> CastSearchResult<TIntermediary, TResponse>(
            Response<SearchResults<TIntermediary>> result, IProvideSearchSerialization searchDeserializer, SearchQuery<TResponse> query)
        {
            var searchResponse = SearchResponse.FromResult(result);
            bool[] didPopulateFacets = query.Callbacks
                .Select(
                    facetPopulator =>
                    {
                        facetPopulator(searchResponse);
                        return true;
                    })
                .ToArray();
            var pageResultAsync = result.Value.GetResultsAsync();
            var pageEnumerator = pageResultAsync.GetAsyncEnumerator();
            return EnumerableAsync.Yield<(double?, TResponse)>(
                async (yieldReturn, yieldBreak) =>
                {
                    if (!await pageEnumerator.MoveNextAsync())
                        return yieldBreak;

                    var searchResult = pageEnumerator.Current;
                    var doc = searchResult.Document;
                    var score = searchResult.Score;
                    var result = searchDeserializer.CastResult<TResponse, TIntermediary>(doc);
                    return yieldReturn((score, result));
                });
        }

        [SearchFilterMethod]
        public static IQueryable<TResource> Filter<TProperty, TResource>(this IQueryable<TResource> query,
            TProperty propertyValue, Expression<Func<TResource, TProperty>> propertySelector)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.Filter),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(propertyValue, typeof(TProperty)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty>>)));

            var requestMessageNewQuery = searchQuery.FromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class SearchFilterMethodAttribute : Attribute, ICompileSearchOptions
        {

            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var propertyValue = (IReferenceable)expressions.First().ResolveExpression();

                var propertyExpr = (Expression)expressions[1].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);
                return searchOptions.AppendFilterOption($"{key} eq '{propertyValue.id}'");
            }
        }

        [SearchFilterIfRefSpecified]
        public static IQueryable<TResource> FilterIfRefSpecified<TProperty, TResource>(this IQueryable<TResource> query,
            IRefOptional<TProperty> propertyValue, Expression<Func<TResource, IRef<TProperty>>> propertySelector)
            where TProperty : IReferenceable
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.FilterIfRefSpecified),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(propertyValue, typeof(IRefOptional<TProperty>)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, IRef<TProperty>>>)));

            var requestMessageNewQuery = searchQuery.FromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class SearchFilterIfRefSpecified : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var idValueMaybe = (IReferenceableOptional)expressions.First().ResolveExpression();
                if (idValueMaybe.IsNull())
                    return searchOptions;

                if (!idValueMaybe.HasValue)
                    return searchOptions;

                var propertyExpr = (Expression)expressions[1].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);

                return searchOptions.AppendFilterOption($"{key} eq '{idValueMaybe.id}'");
            }
        }

        [SearchFilterIfSpecified]
        public static IQueryable<TResource> FilterIfSpecified<TProperty, TResource>(this IQueryable<TResource> query,
            Nullable<TProperty> propertyValue, Expression<Func<TResource, TProperty>> propertySelector)
            where TProperty : struct
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.FilterIfSpecified),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(propertyValue, typeof(Nullable<TProperty>)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty>>)));

            var requestMessageNewQuery = searchQuery.FromExpression(condition);
            return requestMessageNewQuery;
        }

        [SearchFilterIfSpecified]
        public static IQueryable<TResource> FilterIfSpecified<TProperty, TResource>(this IQueryable<TResource> query,
            Nullable<TProperty> propertyValue, Expression<Func<TResource, TProperty?>> propertySelector)
            where TProperty : struct
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.FilterIfSpecified),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(propertyValue, typeof(Nullable<TProperty>)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty?>>)));

            var requestMessageNewQuery = searchQuery.FromExpression(condition);
            return requestMessageNewQuery;
        }

        [SearchFilterIfSpecified]
        public static IQueryable<TResource> FilterIfSpecified<TProperty, TResource>(this IQueryable<TResource> query,
            Nullable<TProperty> propertyValue, Expression<Func<TResource, TProperty?>> propertySelector, ComparisonRelationship comparison)
            where TProperty : struct
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.FilterIfSpecified),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(propertyValue, typeof(Nullable<TProperty>)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty?>>)),
                Expression.Constant(comparison, typeof(ComparisonRelationship)));

            var requestMessageNewQuery = searchQuery.FromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class SearchFilterIfSpecified : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var idValueMaybeObj = expressions.First().ResolveExpression();

                if(idValueMaybeObj.IsDefaultOrNull())
                    return searchOptions;

                var v = idValueMaybeObj;

                if(idValueMaybeObj.GetType().IsNullable())
                    if(!idValueMaybeObj.TryGetNullableValue(out v))
                        return searchOptions;


                var propertyExpr = (Expression)expressions[1].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);

                if(v is DateTime)
                {
                    var vDate = (DateTime)v;
                    v = vDate.ToString("O");
                }

                var comparsionStr = GetComparison();
                return searchOptions.AppendFilterOption($"{key} {comparsionStr} '{v}'");

                // https://learn.microsoft.com/en-us/azure/search/search-query-odata-filter
                string GetComparison()
                {
                    if (expressions.Length == 2)
                        return "eq";

                    var comparison = (ComparisonRelationship)expressions[2].ResolveExpression();
                    if (comparison == ComparisonRelationship.equals)
                        return "eq";
                    if (comparison == ComparisonRelationship.greaterThan)
                        return "gt";
                    if (comparison == ComparisonRelationship.greaterThanOrEquals)
                        return "ge";
                    if (comparison == ComparisonRelationship.lessThan)
                        return "lt";
                    if (comparison == ComparisonRelationship.lessThanOrEquals)
                        return "le";
                    if (comparison == ComparisonRelationship.notEquals)
                        return "ne";

                    throw new ArgumentException($"GetComparison in search filter needs case for {comparison}");
                }

            }
        }

        [FacetOption]
        public static IQueryable<TResource> FacetOptions<TProperty, TResource>(this IQueryable<TResource> query,
            Expression<Func<TResource, TProperty>> propertySelector, out Func<FacetResult[]> getFacetResults, int? limit = default)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            propertySelector.TryGetMemberExpression(out var memberInfo);
            if (!memberInfo.TryGetAttributeInterface(
                out IProvideSearchField searchFieldAttr))
                throw new ArgumentException("Cannot use this prop for a FacetOption");
            var key = searchFieldAttr.GetKeyName(memberInfo);

            FacetResult[] facetResultsThis = default;
            getFacetResults = () =>
            {
                return facetResultsThis;
            };

            SearchResponseDelegate callback = (searchResponse) =>
            {
                var allFacetResults = searchResponse.Facets;
                if (!allFacetResults.TryGetValue(key, out var matchingResults))
                    return;
                facetResultsThis = matchingResults.ToArray();
            };

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.FacetOptions),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty>>)),
                Expression.Constant(getFacetResults, typeof(Func<FacetResult[]>)),
                Expression.Constant(limit, typeof(int?)));

            var requestMessageNewQuery = searchQuery.FromExpression(condition);
            requestMessageNewQuery.Callbacks.Add(callback);

            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class FacetOptionAttribute : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var propertyExpr = (Expression)expressions[0].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);

                var limit = (int?)expressions[2].ResolveExpression();
                if(limit.HasValue)
                {
                    var facetWithLimit = $"{key},count:{limit.Value}";
                    searchOptions.Facets.Add(facetWithLimit);
                    return searchOptions;
                }
                
                searchOptions.Facets.Add(key);
                return searchOptions;
            }
        }

        [TopOption]
        public static IQueryable<TResource> Top<TResource>(this IQueryable<TResource> query, int maxResults)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.Top),
                new Type[] { typeof(TResource) },
                query.Expression,
                Expression.Constant(maxResults, typeof(int)));

            var requestMessageNewQuery = searchQuery.FromExpression(condition);

            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class TopOptionAttribute : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var top = (int)expressions[0].ResolveExpression();
                searchOptions.Size = top;
                return searchOptions;
            }
        }

        [IncludeTotalCount]
        public static IQueryable<TResource> IncludeTotalCount<TResource>(this IQueryable<TResource> query, out Func<long> getTotalSearchResults)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            long totalResultsThis = default;
            getTotalSearchResults = () =>
            {
                return totalResultsThis;
            };

            SearchResponseDelegate callback = (searchResponse) =>
            {
                totalResultsThis = searchResponse.TotalCount.Value;
            };

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.IncludeTotalCount),
                new Type[] { typeof(TResource) },
                query.Expression,
                Expression.Constant(getTotalSearchResults, typeof(Func<long>)));

            searchQuery.Callbacks.Add(callback);
            var requestMessageNewQuery = searchQuery.FromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class IncludeTotalCountAttribute : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                searchOptions.IncludeTotalCount = true;
                return searchOptions;
            }
        }
    }
}

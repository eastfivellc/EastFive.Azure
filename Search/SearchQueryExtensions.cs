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

        private static HashSet<string> fullResponseHashes = new HashSet<string>();

        public static IEnumerableAsync<(double?, T)> SearchQuery<T>(this IQueryable<T> query, string searchText = default)
        {
            var searchTextComplete = (!searchText.IsDefault()) ?
                $"{searchText}~"
                :
                "*";

            var searchClient = (query as SearchQuery<T>).carry.searchIndexClient;

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

            if (searchText.HasBlackSpace())
                searchOptionsPopulated.OrderBy.Insert(0, "search.score() desc");

            var allHashesAvailable = ((EastFive.Azure.Search.SearchQuery<T>)query).FullResponseHashes
                .All(
                    fullResponseHash =>
                    {
                        var hashMatch = fullResponseHashes.Contains(fullResponseHash);
                        return hashMatch;
                    });

            Func<IEnumerableAsync<(double?, T)>, IEnumerableAsync<(double?, T)>> afterFilter = (x) => x;

            if (!allHashesAvailable)
            {
                var skipMaybe = searchOptionsPopulated.Skip;
                var topMaybe = searchOptionsPopulated.Size;
                if (skipMaybe.HasValue)
                {
                    if (topMaybe.HasValue)
                    {
                        afterFilter = (x) => x.Skip(skipMaybe.Value).Take(topMaybe.Value);
                    }
                    else
                    {
                        afterFilter = (x) => x.Skip(skipMaybe.Value);
                    }
                }
                else
                {
                    if (topMaybe.HasValue)
                    {
                        afterFilter = (x) => x.Take(topMaybe.Value);
                    }
                }
                searchOptionsPopulated.Skip = default;
                searchOptionsPopulated.Size = default;
            }

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
                    return afterFilter(queryItemsEnumAsync);
                }

                var result = await searchClient.SearchAsync<T>(searchTextComplete, searchOptionsPopulated);
                var pageResultAsync = result.Value.GetResultsAsync();
                var pageEnumerator = pageResultAsync.GetAsyncEnumerator();
                return afterFilter(EnumerableAsync.Yield<(double?, T)>(
                    async (yieldReturn, yieldBreak) =>
                    {
                        if (!await pageEnumerator.MoveNextAsync())
                            return yieldBreak;

                        var searchResult = pageEnumerator.Current;
                        var doc = searchResult.Document;
                        var score = searchResult.Score;
                        return yieldReturn((score, doc));
                    }));
            }
        }

        public static IEnumerableAsync<(double?, TResponse)> CastSearchResult<TIntermediary, TResponse>(
            Response<SearchResults<TIntermediary>> result, IProvideSearchSerialization searchDeserializer, SearchQuery<TResponse> query)
        {
            var searchResponse = SearchResponse.FromResult(result);
            bool[] didPopulateFacets = query.carry.callbacks
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

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
            return requestMessageNewQuery;
        }

        [SearchFilterMethod]
        public static IQueryable<TResource> Filter<TProperty, TResource>(this IQueryable<TResource> query,
            TProperty propertyValue, Expression<Func<TResource, TProperty>> propertySelector, ComparisonRelationship comparison)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.Filter),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(propertyValue, typeof(TProperty)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty>>)),
                Expression.Constant(comparison, typeof(ComparisonRelationship)));

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class SearchFilterMethodAttribute : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var expressionResult = expressions.First().ResolveExpression();
                var searchValue = GetExpressionValue();

                var propertyExpr = (Expression)expressions[1].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);

                var comparsionStr = GetComparison();

                return searchOptions.AppendFilterOption($"{key} {comparsionStr} '{searchValue}'");

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

                string GetExpressionValue()
                {
                    if (expressionResult.IsDefaultOrNull())
                        return null;
                    var expressionResultType = expressionResult.GetType();

                    if (typeof(IReferenceable).IsAssignableFrom(expressionResultType))
                    {
                        var propertyValue = (IReferenceable)expressionResult;
                        return propertyValue.id.ToString();
                    }

                    if (typeof(string).IsAssignableFrom(expressionResultType))
                    {
                        return (string)expressionResult;
                    }
                    return expressionResult.ToString();
                }
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

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
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

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
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

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
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

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
            return requestMessageNewQuery;
        }

        [SearchFilterIfSpecified]
        public static IQueryable<TResource> FilterIfBlackSpace<TResource>(this IQueryable<TResource> query,
            string propertyValue, Expression<Func<TResource, string>> propertySelector, ComparisonRelationship comparison)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.FilterIfBlackSpace),
                new Type[] { typeof(TResource) },
                query.Expression,
                Expression.Constant(propertyValue, typeof(string)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, string>>)),
                Expression.Constant(comparison, typeof(ComparisonRelationship)));

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
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

        [FilterIfAnyRefs]
        public static IQueryable<TResource> FilterIfAnyRefs<TProperty, TResource>(this IQueryable<TResource> query,
            IRefs<TProperty> refsValues, Expression<Func<TResource, IRef<TProperty>>> propertySelector)
            where TProperty : IReferenceable
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.FilterIfAnyRefs),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(refsValues, typeof(IRefs<TProperty>)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, IRef<TProperty>>>)));

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class FilterIfAnyRefsAttribute : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var refValues = (IReferences)expressions[0].ResolveExpression();
                if (refValues.IsDefaultOrNull())
                    return searchOptions;

                if (refValues.ids.IsDefaultNullOrEmpty())
                    return searchOptions;

                var propertyExpr = (Expression)expressions[1].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);

                var filterText = refValues.ids
                    .Select(
                        id =>
                        {
                            return $"{key} eq '{id}'";
                        })
                    .Join(" or ");

                return searchOptions.AppendFilterOption(filterText); // room/wallColour eq 1 or room/wallColour eq 2
            }
        }

        [SearchFilterNullIfSpecified]
        public static IQueryable<TResource> FilterNullIfSpecified<TProperty, TResource>(this IQueryable<TResource> query,
            string propertyValue, Expression<Func<TResource, TProperty?>> propertySelector, ComparisonRelationship comparison)
            where TProperty : struct
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.FilterNullIfSpecified),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(propertyValue, typeof(string)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty?>>)),
                Expression.Constant(comparison, typeof(ComparisonRelationship)));

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class SearchFilterNullIfSpecifiedAttribute : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var nullString = (string)expressions[0].ResolveExpression();

                if (nullString.IsNullOrWhiteSpace())
                    return searchOptions;

                if (!"null".Equals(nullString, StringComparison.OrdinalIgnoreCase))
                    return searchOptions;

                var propertyExpr = (Expression)expressions[1].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);

                return searchOptions.AppendFilterOption($"{key} eq null");
            }
        }
        

        [FacetOption]
        public static IQueryable<TResource> FacetOptions<TProperty, TResource>(this IQueryable<TResource> query,
            Expression<Func<TResource, TProperty>> propertySelector,
            out Func<FacetResult[]> getFacetResults,
            int? limit = default, object[] facetBreakingPoints = default)
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
                Expression.Constant(limit, typeof(int?)),
                Expression.Constant(facetBreakingPoints, typeof(object[])));

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
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
                var facetString = key;
                if (limit.HasValue)
                {
                    facetString = $"{facetString},count:{limit.Value}";
                }

                var facetBreakingPoints = (object[])expressions[3].ResolveExpression();
                if (!facetBreakingPoints.IsDefaultNullOrEmpty())
                {
                    var valuesString = facetBreakingPoints
                        .Select(
                            facetBreak =>
                            {
                                if (facetBreak.IsNull())
                                    return "";
                                var facetBreakType = facetBreak.GetType();
                                if (typeof(DateTime).IsAssignableFrom(facetBreakType))
                                {
                                    var facetBreakDt = (DateTime)facetBreak;
                                    return facetBreakDt.ToString("O");
                                }
                                return facetBreak.ToString();
                            })
                        .Join(" | ");

                    facetString = $"{facetString},interval:year";
                    //facetString = $"{facetString},values:{valuesString}";
                }

                searchOptions.Facets.Add(facetString);
                return searchOptions;
            }
        }

        [SearchOrderByIfSpecified]
        public static IQueryable<TResource> OrderByIfSpecified<TProperty, TResource>(this IQueryable<TResource> query,
            int index, bool shouldBeDescending, Expression<Func<TResource, TProperty>> propertySelector)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.OrderByIfSpecified),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(index, typeof(int)),
                Expression.Constant(shouldBeDescending, typeof(bool)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty>>)));

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
            return requestMessageNewQuery;
        }

        public static IQueryable<TResource> OrderByIfInListSpecified<TProperty, TResource>(this IQueryable<TResource> query,
            string [] sortItems, Expression<Func<TResource, TProperty>> propertySelector)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            propertySelector.TryGetMemberExpression(out var memberInfo);
            if (!memberInfo.TryGetAttributeInterface(
                out IProvideSearchField searchFieldAttr))
                throw new ArgumentException($"Cannot use `{memberInfo.DeclaringType.FullName}..{memberInfo.Name}` prop for OrderBy expression");
            var key = searchFieldAttr.GetKeyName(memberInfo);
            var index = sortItems.NullToEmpty().IndexOf(key,
                (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
                success:index => index,
                notFound:() => -1);

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.OrderByIfSpecified),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(index, typeof(int)),
                Expression.Constant(false, typeof(bool)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty>>)));

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
            return requestMessageNewQuery;
        }

        public static IQueryable<TResource> OrderByDescendingIfInListSpecified<TProperty, TResource>(this IQueryable<TResource> query,
            string[] sortItems, Expression<Func<TResource, TProperty>> propertySelector)
        {
            if (!typeof(SearchQuery<TResource>).IsAssignableFrom(query.GetType()))
                throw new ArgumentException($"query must be of type `{typeof(SearchQuery<TResource>).FullName}` not `{query.GetType().FullName}`", "query");
            var searchQuery = query as SearchQuery<TResource>;

            propertySelector.TryGetMemberExpression(out var memberInfo);
            if (!memberInfo.TryGetAttributeInterface(
                out IProvideSearchField searchFieldAttr))
                throw new ArgumentException($"Cannot use `{memberInfo.DeclaringType.FullName}..{memberInfo.Name}` prop for OrderBy expression");
            var key = searchFieldAttr.GetKeyName(memberInfo);
            var index = sortItems.NullToEmpty().IndexOf(key,
                (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
                success: index => index,
                notFound: () => -1);

            var condition = Expression.Call(
                typeof(SearchQueryExtensions), nameof(SearchQueryExtensions.OrderByIfSpecified),
                new Type[] { typeof(TProperty), typeof(TResource) },
                query.Expression,
                Expression.Constant(index, typeof(int)),
                Expression.Constant(true, typeof(bool)),
                Expression.Constant(propertySelector, typeof(Expression<Func<TResource, TProperty>>)));

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
            return requestMessageNewQuery;
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class SearchOrderByIfSpecifiedAttribute : Attribute, ICompileSearchOptions
        {
            public SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions)
            {
                var index = (int)expressions[0].ResolveExpression();
                if (index < 0)
                    return searchOptions;

                var shouldBeDescending = (bool)expressions[1].ResolveExpression();

                var propertyExpr = (Expression)expressions[2].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);

                while(searchOptions.OrderBy.Count() <= index)
                {
                    searchOptions.OrderBy.Add("");
                }
                searchOptions.OrderBy[index] = shouldBeDescending ? $"{key} desc" : key;
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

            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);

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
            var requestMessageNewQuery = searchQuery.SearchQueryFromExpression(condition);
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

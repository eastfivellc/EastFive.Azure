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
            return new SearchQuery<T>(searchIndexClient);
        }

        public static IEnumerableAsync<(double?, T)> SearchQuery<T>(this IQueryable<T> query, string searchText)
        {
            var searchClient = (query as SearchQuery<T>).searchIndexClient;
            var filterExpressions = query.Compile<string[], IProvideSearchQuery>(new string[] { },
                onRecognizedAttribute: (ss, queryProvider, methodInfo, expressions) =>
                {
                    var searchParam = queryProvider.GetSearchParameter(
                        methodInfo, expressions);
                    return ss.Concat(searchParam).ToArray();
                },
                onUnrecognizedAttribute: (ss, unrecognizedMethod, methodArguments) =>
                {
                    if (unrecognizedMethod.Name == nameof(System.Linq.Queryable.Where))
                    {
                        return unrecognizedMethod.TryParseMemberAssignment(methodArguments,
                                (memberInfo, expressionType, memberValue) =>
                                    ss.Append($"{memberInfo.Name} eq {memberValue}").ToArray(),
                                () => throw new ArgumentException(
                                    $"Could not parse `{unrecognizedMethod}`({methodArguments})"));
                    }
                    throw new ArgumentException($"Search cannot compile Method `{unrecognizedMethod.DeclaringType.FullName}..{unrecognizedMethod.Name}`");
                });
            var filterExpression = filterExpressions
                .Select(expr => $"({expr})")
                .Join(" and ");
            var searchOptions = new SearchOptions
            {
                Filter = filterExpression,
                QueryType = SearchQueryType.Full,
            };
            var searchTextComplete = searchText.HasBlackSpace() ?
                $"{searchText}~"
                :
                "*";

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
                        new object [] { searchTextComplete, searchOptions, default(System.Threading.CancellationToken) });
                    var resultsTaskObj = resultsObj.CastAsTaskObjectAsync(out var discard);
                    var responseObj = await resultsTaskObj;

                    var queryItemsEnumAsyncObj = typeof(SearchQueryExtensions)
                        .GetMethod(nameof(CastSearchResult), BindingFlags.Public | BindingFlags.Static)
                        .MakeGenericMethod(new[] { searchResultsType, typeof(T) })
                        .Invoke(null, new object[] { responseObj, searchSerializer } );

                    var queryItemsEnumAsync = (IEnumerableAsync<(double?, T)>)queryItemsEnumAsyncObj;
                    return queryItemsEnumAsync;
                }

                var result = await searchClient.SearchAsync<T>(searchTextComplete, searchOptions);
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
            Response<SearchResults<TIntermediary>> result, IProvideSearchSerialization searchDeserializer)
        {
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
        public class SearchFilterMethodAttribute : Attribute, IProvideSearchQuery
        {
            public string[] GetSearchParameter(MethodInfo methodInfo, Expression[] expressions)
            {
                var propertyValue = (IReferenceableOptional)expressions.First().ResolveExpression();

                var propertyExpr = (Expression)expressions[1].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);
                return $"{key} eq {propertyValue}".AsArray();
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
        public class SearchFilterIfRefSpecified : Attribute, IProvideSearchQuery
        {
            public string[] GetSearchParameter(MethodInfo methodInfo, Expression[] expressions)
            {
                var idValueMaybe = (IReferenceableOptional)expressions.First().ResolveExpression();
                if (idValueMaybe.IsNull())
                    return new string[] { };

                if (!idValueMaybe.HasValue)
                    return new string[] { };

                var propertyExpr = (Expression)expressions[1].ResolveExpression();
                propertyExpr.TryGetMemberExpression(out var memberInfo);
                if (!memberInfo.TryGetAttributeInterface(
                    out IProvideSearchField searchFieldAttr))
                    throw new ArgumentException("Cannot use this prop for search expression");
                var key = searchFieldAttr.GetKeyName(memberInfo);

                return $"{key} eq '{idValueMaybe.id}'".AsArray();
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
        public class SearchFilterIfSpecified : Attribute, IProvideSearchQuery
        {
            public string[] GetSearchParameter(MethodInfo methodInfo, Expression[] expressions)
            {
                var idValueMaybeObj = expressions.First().ResolveExpression();

                if(idValueMaybeObj.IsDefaultOrNull())
                    return new string[] { };

                var v = idValueMaybeObj;

                if(idValueMaybeObj.GetType().IsNullable())
                    if(!idValueMaybeObj.TryGetNullableValue(out v))
                        return new string[] { };


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
                return $"{key} {comparsionStr} '{v}'".AsArray();

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
    }
}

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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Search
{
    public static class SearchExtensions
    {
        public static SearchIndexClient GetIndexClient()
        {
            return AppSettings.Search.EndPoint.ConfigurationString(
                searchServiceEndPoint =>
                {
                    return AppSettings.Search.AdminApiKey.ConfigurationString(
                        adminApiKey =>
                        {
                            var indexClient = new SearchIndexClient(
                                new Uri(searchServiceEndPoint), new AzureKeyCredential(adminApiKey));
                            return indexClient;
                        });
                });
        }

        public static SearchClient GetClient<T>()
        {
            var indexClient = GetIndexClient();
            var indexName = typeof(T).GetIndexName();
            var searchClient = indexClient.GetSearchClient(indexName);
            return searchClient;
        }

        private static string GetIndexName(this Type type)
        {
            var searchIndexProvider = type.GetAttributeInterface<IProvideSearchIndex>();
            return searchIndexProvider.Index;
        }

        private static (MemberInfo, TInterface)[] GetMemberSupportingInterface<TInterface>(this Type type)
        {
            return type
                .GetMembers()
                .Select(
                    memberInfo =>
                    {
                        return memberInfo.GetAttributesInterface<TInterface>()
                            .Select(contract => (memberInfo, contract));
                    })
                .Where(tpls => tpls.Any())
                .Select(tpls => tpls.First())
                .ToArray();
        }

        public static async Task<SearchIndex> SearchCreateIndex(this Type type)
        {
            var searchIndexClient = GetIndexClient();
            var indexName = type.GetIndexName();
            var fields = type
                .GetMemberSupportingInterface<IProvideSearchField>()
                .Select(fieldProvider => fieldProvider.Item2
                    .GetSearchField(fieldProvider.Item1))
                .ToList();

            var definition = new SearchIndex(indexName)
            {
                Fields = fields,
            };

            var response = await searchIndexClient.CreateOrUpdateIndexAsync(definition);
            return response.Value;
        }

        public static async Task<IndexDocumentsResult> SearchUpdateBatchAsync<T>(this IEnumerableAsync<T> items)
        {
            var searchClient = GetClient<T>();
            var itemsArray = await items
                .Select(item => IndexDocumentsAction.Upload<T>(item))
                .ToArrayAsync();
            var batch = IndexDocumentsBatch.Create(itemsArray);
            
            try
            {
                var result = searchClient.IndexDocuments(batch);
                return result.Value;
            }
            catch (Exception)
            {
                // Sometimes when your Search service is under load, indexing will fail for some of the documents in
                // the batch. Depending on your application, you can take compensating actions like delaying and
                // retrying. For now, just log the failed document keys and continue.
                Console.WriteLine("Failed to index some of the documents: {0}");
                throw;
            }
        }

        public static IEnumerableAsync<T> SearchQuery<T>(IQueryable<T> query, string searchText)
        {
            var searchClient = GetClient<T>();
            var searchExpression = query.Compile<string, IProvideSearchQuery>(searchText,
                onRecognizedAttribute: (ss, queryProvider, methodInfo, expressions) =>
                {
                    var searchParam = queryProvider.GetSearchParameter(
                        methodInfo, expressions);
                    return ss + searchParam;
                },
                onUnrecognizedAttribute: (ss, methodInfo, expressions) =>
                {
                    if (methodInfo.Name == nameof(System.Linq.Queryable.Where))
                    {
                        return ss;
                    }
                    throw new ArgumentException($"Cannot compile Method `{methodInfo.DeclaringType.FullName}..{methodInfo.Name}`");
                });

            return GetResultsAsync().FoldTask();

            async Task<IEnumerableAsync<T>> GetResultsAsync()
            {
                var result = await searchClient.SearchAsync<T>(searchExpression);
                var pageResultAsync = result.Value.GetResultsAsync();
                var pageEnumerator = pageResultAsync.GetAsyncEnumerator();
                return EnumerableAsync.Yield<T>(
                    async (yieldReturn, yieldBreak) =>
                    {
                        if (!await pageEnumerator.MoveNextAsync())
                            return yieldBreak;

                        var searchResult = pageEnumerator.Current;
                        var doc = searchResult.Document;
                        return yieldReturn(doc);
                    });
            }
        }

    }
}

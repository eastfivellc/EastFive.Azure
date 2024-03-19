
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

using EastFive;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Linq;
using EastFive.Reflection;
using EastFive.Collections;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;

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
            return searchIndexProvider.GetIndexName(type);
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

        public static async Task<Response> SearchDeleteIndex(this Type type)
        {
            var searchIndexClient = GetIndexClient();
            var indexName = type.GetIndexName();
            var response = await searchIndexClient.DeleteIndexAsync(indexName);
            return response;
        }

        public static async Task<TResult> SearchUpdateAsync<T, TResult>(this T item,
            Func<IndexDocumentsResult, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var documentSerializer = typeof(T)
                .GetAttributeInterface<IProvideSearchSerialization>();

            var searchClient = GetClient<T>();
            var serializedItem = documentSerializer.GetSerializedObject(item);

            try
            {
                var result = await searchClient.MergeDocumentsAsync(
                    serializedItem.AsArray());
                return onSuccess(result.Value);
            }
            catch (Exception ex)
            {
                // Sometimes when your Search service is under load, indexing will fail for some of the documents in
                // the batch. Depending on your application, you can take compensating actions like delaying and
                // retrying. For now, just log the failed document keys and continue.
                return onFailure(ex.Message);
            }
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
                var result = await searchClient.IndexDocumentsAsync(batch);
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

        public static async Task<IndexDocumentsResult[]> SearchUpdateBatchAsync<T>(this IEnumerable<T> items)
        {
            if (items.None())
                return default;

            var documentSerializer = typeof(T)
                .GetAttributeInterface<IProvideSearchSerialization>();

            var searchClient = GetClient<T>();
            var itemsToProcess = items.Take(32000).ToArray();
            var remainingItems = items.Skip(32000).ToArray();
            var itemsArray = itemsToProcess
                .Select(
                    item =>
                    {
                        var serializedItem = documentSerializer.GetSerializedObject(item);
                        return IndexDocumentsAction.MergeOrUpload(serializedItem);
                    })
                .ToArray();
            var batch = IndexDocumentsBatch.Create(itemsArray);

            try
            {
                var result = await searchClient.IndexDocumentsAsync(batch);
                if(remainingItems.Any())
                {
                    var remainingResults = await SearchUpdateBatchAsync(remainingItems);
                    return remainingResults.Prepend(result).ToArray();
                }
                return result.Value.AsArray();
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

        public static async Task<int> SearchDeleteBatchAsync<T>(this IEnumerableAsync<T> items)
        {
            var searchClient = GetClient<T>();

            var documentSerializer = typeof(T)
                .GetAttributeInterface<IProvideSearchSerialization>();

            return await items
                // .Batch()
                .Segments(5000)
                .Select(
                    async items =>
                    {
                        var itemsArray = items
                            .Select(
                                item =>
                                {
                                    var serializedItem = documentSerializer.GetSerializedObject(item);
                                    return IndexDocumentsAction.Delete(serializedItem);
                                })
                            .ToArray();
                        var batch = IndexDocumentsBatch.Create(itemsArray);
                        try
                        {
                            var result = await searchClient.IndexDocumentsAsync(batch);
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
                    })
                .Await()
                .Select(docs => docs.Results.Count)
                .SumAsync();
        }
    }
}

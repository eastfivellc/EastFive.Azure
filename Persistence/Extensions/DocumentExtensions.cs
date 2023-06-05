using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Linq.Async;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Analytics;
using EastFive.Extensions;

namespace BlackBarLabs.Persistence
{
    public static class DocumentExtensions
    {
        public static async Task<TResult> FindLinkedDocumentAsync<TParentDoc, TLinkedDoc, TResult>(this AzureStorageRepository repo,
            Guid parentDocId,
            Func<TParentDoc, Guid> getLinkedId,
            Func<TParentDoc, TLinkedDoc, TResult> found,
            Func<TResult> parentDocNotFound,
            Func<TParentDoc, TResult> linkedDocNotFound,
            ILogger logger = default)
            where TParentDoc : class, ITableEntity
            where TLinkedDoc : class, ITableEntity
        {
            return await await repo.FindByIdAsync(parentDocId,
                async (TParentDoc document) =>
                {
                    logger.Trace("Found parent doc");
                    var linkedDocId = getLinkedId(document);
                    return await repo.FindByIdAsync(linkedDocId,
                        (TLinkedDoc linkedDoc) =>
                        {
                            logger.Trace("Found linked doc");
                            return found(document, linkedDoc);
                        },
                        () =>
                        {
                            logger.Trace($"Failed to find linked doc {linkedDocId}");
                            return linkedDocNotFound(document);
                        });
                },
                () =>
                {
                    logger.Trace($"Failed to find parent doc {parentDocId}");
                    return parentDocNotFound().AsTask();
                },
                logger: logger);
        }
        
        public static async Task<TResult> FindLinkedDocumentsAsync<TParentDoc, TLinkedDoc, TResult>(this AzureStorageRepository repo,
            Guid parentDocId,
            Func<TParentDoc, Guid[]> getLinkedIds,
            Func<TParentDoc, TLinkedDoc[], TResult> found,
            Func<TResult> parentDocNotFound)
            where TParentDoc : class, ITableEntity
            where TLinkedDoc : class, ITableEntity
        {
            var result = await await repo.FindByIdAsync(parentDocId,
                async (TParentDoc document) =>
                {
                    var linkedDocIds = getLinkedIds(document);
                    var linkedDocsWithNulls = await linkedDocIds
                        .Select(
                            linkedDocId =>
                            {
                                return repo.FindByIdAsync(linkedDocId,
                                    (TLinkedDoc priceSheetDocument) => priceSheetDocument,
                                    () =>
                                    {
                                        // TODO: Log data corruption
                                        return default(TLinkedDoc);
                                    });
                            })
                        .WhenAllAsync();
                    var linkedDocs = linkedDocsWithNulls
                        .Where(doc => default(TLinkedDoc) != doc)
                        .ToArray();
                    return found(document, linkedDocs);
                },
               () => parentDocNotFound().AsTask());

            return result;
        }

        public static async Task<TResult> FindLinkedDocumentsAsync<TParentDoc, TLinkedDoc, TResult>(this AzureStorageRepository repo,
            Guid parentDocId, string partitionKey,
            Func<TParentDoc, Guid[]> getLinkedIds,
            Func<TParentDoc, TLinkedDoc[], TResult> found,
            Func<TResult> parentDocNotFound)
            where TParentDoc : class, ITableEntity
            where TLinkedDoc : class, ITableEntity
        {
            var result = await await repo.FindByIdAsync(parentDocId, partitionKey,
                async (TParentDoc document) =>
                {
                    var linkedDocIds = getLinkedIds(document);
                    var linkedDocsWithNulls = await linkedDocIds
                        .Select(
                            linkedDocId =>
                            {
                                return repo.FindByIdAsync(linkedDocId,
                                    (TLinkedDoc priceSheetDocument) => priceSheetDocument,
                                    () =>
                                    {
                                        // TODO: Log data corruption
                                        return default(TLinkedDoc);
                                    });
                            })
                        .WhenAllAsync();
                    var linkedDocs = linkedDocsWithNulls
                        .Where(doc => default(TLinkedDoc) != doc)
                        .ToArray();
                    return found(document, linkedDocs);
                },
               () => parentDocNotFound().AsTask());

            return result;
        }

        public static async Task<TResult> FindLinkedDocumentsAsync<TParentDoc, TLinkedDoc, TResult>(this AzureStorageRepository repo,
            Guid parentDocId, string partitionKey,
            Func<TParentDoc, Guid[]> getLinkedIds,
            Func<TParentDoc, TLinkedDoc[], Guid [], TResult> found,
            Func<TResult> parentDocNotFound)
            where TParentDoc : class, ITableEntity
            where TLinkedDoc : class, ITableEntity
        {
            var result = await await repo.FindByIdAsync(parentDocId, partitionKey,
                (TParentDoc document) =>
                {
                    var linkedDocIds = getLinkedIds(document);
                    return linkedDocIds
                        .FlatMap(
                            new Guid[] { },
                            async (linkedDocId, missingIds, next, skip) =>
                            {
                                return await await repo.FindByIdAsync(linkedDocId,
                                    (TLinkedDoc priceSheetDocument) => next(priceSheetDocument, missingIds),
                                    () => skip(missingIds.Append(linkedDocId).ToArray()));
                            },
                            (TLinkedDoc[] linkedDocs, Guid[] missingIds) =>
                            {
                                return found(document, linkedDocs, missingIds).AsTask();
                            });
                },
               () => parentDocNotFound().AsTask());

            return result;
        }

        /// <summary>
        /// Starting with <paramref name="startingDocumentId"/>, searches for documents until <paramref name="getLinkedId"/>
        /// returns a Guid? without a value.
        /// </summary>
        /// <typeparam name="TDoc"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="repo"></param>
        /// <param name="startingDocumentId"></param>
        /// <param name="getLinkedId"></param>
        /// <param name="onFound">Passes an array of found documents in reverse order (document with id <paramref name="startingDocumentId"/> is last).</param>
        /// <param name="startDocNotFound"></param>
        /// <returns></returns>
        public static async Task<TResult> FindRecursiveDocumentsAsync<TDoc, TResult>(this AzureStorageRepository repo,
            Guid startingDocumentId,
            Func<TDoc, Guid?> getLinkedId,
            Func<TDoc[], TResult> onFound,
            Func<TResult> startDocNotFound)
            where TDoc : class, ITableEntity
        {
            var result = await await repo.FindByIdAsync(startingDocumentId,
                async (TDoc document) =>
                {
                    var linkedDocId = getLinkedId(document);
                    if (!linkedDocId.HasValue)
                        return onFound(document.AsEnumerable().ToArray());
                    return await repo.FindRecursiveDocumentsAsync(linkedDocId.Value,
                        getLinkedId,
                        (linkedDocuments) => onFound(linkedDocuments.Append(document).ToArray()),
                        () => onFound(new TDoc[] { document })); // TODO: Log data inconsistency
                },
                () => startDocNotFound().AsTask());

            return result;
        }

        public static async Task<TResult> FindRecursiveDocumentsAsync<TDoc, TResult>(this AzureStorageRepository repo,
            Guid startingDocumentId,
            Func<TDoc, Guid[]> getLinkedIds,
            Func<TDoc[], TResult> onFound,
            Func<TResult> startDocNotFound)
            where TDoc : class, ITableEntity
        {
            var result = await await repo.FindByIdAsync(startingDocumentId,
                async (TDoc document) =>
                {
                    var linkedDocIds = getLinkedIds(document);
                    var docs = await linkedDocIds.Select(
                        linkedDocId =>
                            repo.FindRecursiveDocumentsAsync(linkedDocId,
                                getLinkedIds,
                                    (linkedDocuments) => linkedDocuments,
                                    () => (new TDoc[] { })))
                         .WhenAllAsync()
                         .SelectManyAsync()
                         .ToArrayAsync(); // TODO: Log data inconsistency
                    return onFound(docs.Append(document).ToArray());
                },
                () => startDocNotFound().AsTask());

            return result;
        }
    }
}

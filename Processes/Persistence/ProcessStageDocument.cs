using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Azure.Cosmos.Table;

using BlackBarLabs;
using EastFive.Collections.Generic;

using BlackBarLabs.Persistence;
using BlackBarLabs.Extensions;
using EastFive.Serialization;
using BlackBarLabs.Persistence.Azure;
using EastFive.Extensions;
using EastFive;
using EastFive.Linq;
using BlackBarLabs.Persistence.Azure.StorageTables;
using System.Runtime.Serialization;
using System.Net.Http;

namespace EastFive.Azure.Persistence
{
    public class ProcessStageDocument : TableEntity
    {
        #region Properties

        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id => Guid.Parse(this.RowKey);
        
        public Guid Owner { get; set; }

        public Guid ProcessStageType { get; set; }

        public string Title { get; set; }

        #region Confirmables

        public byte[] ConfirmableActorss { get; set; }

        public byte[] ConfirmableNexts { get; set; }

        internal KeyValuePair<Guid[], Guid>[] GetConfirmables()
        {
            return ConfirmableActorss.FromByteArray()
                .Select(actorIdBytes => actorIdBytes.ToGuidsFromByteArray())
                .Zip(ConfirmableNexts.ToGuidsFromByteArray(),
                    (actorIds, next) => actorIds.PairWithValue(next))
                .ToArray();
        }

        internal bool SetConfirmables(KeyValuePair<Guid[], Guid>[] confirmableIds)
        {
            this.ConfirmableActorss = confirmableIds
                .Select(kvp => kvp.Key.ToByteArrayOfGuids())
                .ToByteArray();
            this.ConfirmableNexts = confirmableIds.Select(kvp => kvp.Value).ToByteArrayOfGuids();
            return true;
        }
        
        #endregion
        
        #region Editables

        public byte[] Editables { get; set; }

        internal Guid[] GetEditables()
        {
            return Editables.ToGuidsFromByteArray();
        }

        internal bool SetEditables(Guid[] editables)
        {
            this.Editables = editables.ToByteArrayOfGuids();
            return true;
        }

        internal bool AddEditable(Guid editableId)
        {
            var editables = this.GetEditables();
            if (editables.Contains(editableId))
                return false;
            this.Editables = editables.Append(editableId).ToByteArrayOfGuids();
            return true;
        }

        internal bool RemoveEditable(Guid editableId)
        {
            var editables = this.GetEditables();
            if (!editables.Contains(editableId))
                return false;
            this.Editables = editables.Where(oi => oi != editableId).ToByteArrayOfGuids();
            return true;
        }

        #endregion
        
        #region Completables

        public byte[] Completables { get; set; }

        internal Guid[] GetCompletables()
        {
            return Completables.ToGuidsFromByteArray();
        }

        internal bool SetCompletables(Guid[] completables)
        {
            this.Completables = completables.ToByteArrayOfGuids();
            return true;
        }

        internal bool AddCompletable(Guid completableId)
        {
            var completables = this.GetCompletables();
            if (completables.Contains(completableId))
                return false;
            this.Completables = completables.Append(completableId).ToByteArrayOfGuids();
            return true;
        }

        internal bool RemoveCompletable(Guid completableId)
        {
            var completables = this.GetCompletables();
            if (!completables.Contains(completableId))
                return false;
            this.Completables = completables.Where(oi => oi != completableId).ToByteArrayOfGuids();
            return true;
        }

        #endregion

        #region Viewables

        public byte[] Viewables { get; set; }

        internal Guid[] GetViewables()
        {
            return Viewables.ToGuidsFromByteArray();
        }

        internal bool SetViewables(Guid[] viewables)
        {
            this.Viewables = viewables.ToByteArrayOfGuids();
            return true;
        }

        internal bool AddViewables(Guid viewableId)
        {
            var viewables = this.GetEditables();
            if (viewables.Contains(viewableId))
                return false;
            this.Viewables = viewables.Append(viewableId).ToByteArrayOfGuids();
            return true;
        }

        internal bool RemoveViewable(Guid viewableId)
        {
            var viewables = this.GetEditables();
            if (!viewables.Contains(viewableId))
                return false;
            this.Viewables = viewables.Where(oi => oi != viewableId).ToByteArrayOfGuids();
            return true;
        }

        #endregion

        #endregion

        public static Task<TResult> FindByIdAsync<TResult>(Guid processStageId,
            Func<ProcessStage, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository => azureStorageRepository.FindByIdAsync(processStageId,
                (ProcessStageDocument processStageDocument) =>
                {
                    return onFound(Convert(processStageDocument));
                },
                onNotFound));
        }

        internal static Task<TResult> FindByIdsAsync<TResult>(IEnumerable<Guid> processStageIds,
            Func<ProcessStage [], Guid [], TResult> onFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository => processStageIds
                    .FlatMap(
                        new Guid[] { },
                        async (processStageId, misses, next, skip) =>
                            await await azureStorageRepository.FindByIdAsync(processStageId,
                                (ProcessStageDocument processStageDocument) => next(Convert(processStageDocument), misses),
                                () => skip(misses.Append(processStageId).ToArray())),
                        onFound.AsAsyncFunc()));
        }
        
        public static Task<TResult> FindByOwnerAsync<TResult>(Guid ownerId,
            Func<ProcessStage[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository => azureStorageRepository.FindLinkedDocumentsAsync(ownerId,
                    (Documents.ProcessStageActorLookupDocument actorDoc) => actorDoc.GetLookupDocumentIds(),
                    (Documents.ProcessStageActorLookupDocument actorDoc, ProcessStageDocument[] procStageDocs) =>
                    {
                        return onFound(procStageDocs.Select(Convert).ToArray());
                    },
                    onNotFound));
        }

        public static Task<TResult> FindByResourceAsync<TResult>(Guid resourceId,
            Func<ProcessStage[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository => azureStorageRepository.FindLinkedDocumentsAsync(resourceId,
                    (Documents.ProcessStageActorLookupDocument fulfullmentDoc) => fulfullmentDoc.GetLookupDocumentIds(),
                    (Documents.ProcessStageActorLookupDocument fulfillmentDoc, ProcessStageDocument [] procStageDocs) =>
                        onFound(procStageDocs.Select(Convert).ToArray()),
                    onNotFound));
        }

        internal static Task<TResult> UpdateAsync<TResult>(Guid processStageId,
            Func<ProcessStage, Func<ProcessStage, Task>, Task<TResult>> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository =>
                {
                    return azureStorageRepository.UpdateAsync<ProcessStageDocument, TResult>(processStageId,
                        (doc, saveAsync) =>
                        {
                            return onFound(Convert(doc),
                                async (update) =>
                                {
                                    doc.ProcessStageType = update.processStageTypeId;
                                    doc.Title = update.title;
                                    doc.SetCompletables(update.completableIds);
                                    doc.SetViewables(update.viewableIds);
                                    doc.SetEditables(update.editableIds);
                                    doc.SetConfirmables(update.confirmableIds);

                                    await saveAsync(doc);
                                });
                        },
                        () => onNotFound());
                });
        }

        public static Task<TResult> FindFirstByOwnerAndResourceAsync<TResult>(Guid ownerId,
            Func<ProcessStage[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository => azureStorageRepository.FindLinkedDocumentsAsync(ownerId,
                    (Documents.ProcessStageActorLookupDocument fulfullmentDoc) => fulfullmentDoc.GetLookupDocumentIds(),
                    (Documents.ProcessStageActorLookupDocument fulfillmentDoc, ProcessStageDocument[] procStageDocs) =>
                        onFound(procStageDocs.Select(Convert).ToArray()),
                    onNotFound));
        }

        internal static ProcessStage Convert(ProcessStageDocument processStageDocument)
        {
            return new ProcessStage
            {
                processStageId = processStageDocument.Id,
                ownerId = processStageDocument.Owner,
                processStageTypeId = processStageDocument.ProcessStageType,
                title = processStageDocument.Title,
                confirmableIds = processStageDocument.GetConfirmables(),
                editableIds = processStageDocument.GetEditables(),
                completableIds = processStageDocument.GetCompletables(),
                viewableIds = processStageDocument.GetViewables(),
            };
        }

        internal static Task<TResult> CreateAsync<TResult>(Guid processStageId, Guid actorId,
                Guid processStageTypeId, string title,
                KeyValuePair<Guid[], Guid>[] confirmableActorIds, Guid[] editableActorIds, Guid[] viewableActorIds, 
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository =>
                {
                    var rollback = new RollbackAsync<TResult>();

                    var procStageDoc = new ProcessStageDocument()
                    {
                        Owner = actorId,
                        ProcessStageType = processStageTypeId,
                        Title = title,
                    };
                    procStageDoc.SetConfirmables(confirmableActorIds);
                    procStageDoc.SetEditables(editableActorIds);
                    procStageDoc.SetViewables(viewableActorIds);
                    rollback.AddTaskCreate(processStageId, procStageDoc,
                        onAlreadyExists, azureStorageRepository);

                    rollback.AddTaskCreateOrUpdate<TResult, Documents.ProcessStageActorLookupDocument>(actorId,
                        (created, lookupDoc) => lookupDoc.AddLookupDocumentId(processStageId),
                        lookupDoc => lookupDoc.RemoveLookupDocumentId(processStageId),
                        azureStorageRepository);


                    return rollback.ExecuteAsync(onSuccess);
                });
        }

        private static Task<ProcessStage> UpdateAsync(Guid adapterId,
            string key, string name, KeyValuePair<string, string>[] identifiers,
            string resourceType, Guid integrationId, Guid integrationIdRemote)
        {
            throw new NotImplementedException();
            //return AzureStorageRepository.Connection(
            //    async azureStorageRepository =>
            //    {
            //        var rollback = new RollbackAsync<Connection>();
                    
            //        rollback.AddTaskCreateOrUpdate(integrationId,
            //            (created, integrationAdapterLookupDoc) => integrationAdapterLookupDoc.AddLookupDocumentId(adapterId),
            //            (IntegrationAdapterLookupDocument integrationAdapterLookupDoc) => integrationAdapterLookupDoc.RemoveSynchronizationDocumentId(adapterId),
            //            azureStorageRepository);
                    
            //        var connection = new Connection
            //        {
            //            adapterInternal = new Adapter()
            //            {
            //                adapterId = adapterId,
            //                key = key,
            //                name = name,
            //                identifiers = identifiers,
            //                integrationId = integrationId,
            //                resourceType = resourceType,
            //            },
            //        };

            //        if (!connectorAdapterMaybe.HasValue)
            //        {
            //            rollback.AddTaskUpdate<KeyValuePair<string, KeyValuePair<string, string>[]>, Connection, AdapterDocument>(adapterId,
            //                (adapterDoc, onModified, onNotModified) =>
            //                {
            //                    var rollbackSave = adapterDoc.Name.PairWithValue(adapterDoc.GetIdentifiers());
            //                    adapterDoc.Name = name;
            //                    adapterDoc.SetIdentifiers(identifiers);
            //                    adapterDoc.ResourceType = resourceType;
            //                    return onModified(rollbackSave);
            //                },
            //                (save, adapterDoc) =>
            //                {
            //                    adapterDoc.Name = save.Key;
            //                    adapterDoc.SetIdentifiers(save.Value);
            //                    adapterDoc.ResourceType = resourceType;
            //                    return true;
            //                },
            //                () => connection,
            //                azureStorageRepository);
            //            return await rollback.ExecuteAsync(() => connection);
            //        }

            //        var connector = connectorAdapterMaybe.Value.Key;
            //        var adapterRemote = connectorAdapterMaybe.Value.Value;
            //        adapterRemote.adapterId = GetId(adapterRemote.key, adapterRemote.integrationId, adapterRemote.resourceType);

            //        var connectorDoc = new ConnectorDocument()
            //        {
            //            CreatedBy = connector.createdBy,
            //            LocalAdapter = adapterId,
            //            RemoteAdapter = adapterRemote.adapterId,
            //        };
            //        connectorDoc.SetMethod(connector.synchronizationMethod);
            //        rollback.AddTaskCreate(connector.connectorId, connectorDoc, () => default(Connection), azureStorageRepository);

            //        var adapterRemoteDoc = new AdapterDocument()
            //        {
            //            Key = adapterRemote.key,
            //            IntegrationId = integrationIdRemote,
            //            Name = adapterRemote.name,
            //            ResourceType = resourceType,
            //        };
            //        adapterRemoteDoc.AddConnectorId(connector.connectorId);
            //        adapterRemoteDoc.SetIdentifiers(adapterRemote.identifiers);
            //        rollback.AddTaskCreate(adapterRemote.adapterId, adapterRemoteDoc, () => default(Connection), azureStorageRepository);
                    
            //        rollback.AddTaskCreateOrUpdate(adapterRemote.integrationId,
            //            (created, integrationAdapterLookupDoc) => integrationAdapterLookupDoc.AddLookupDocumentId(adapterRemote.adapterId),
            //            (IntegrationAdapterLookupDocument integrationAdapterLookupDoc) => integrationAdapterLookupDoc.RemoveSynchronizationDocumentId(adapterRemote.adapterId),
            //            azureStorageRepository);
                    
            //        rollback.AddTaskUpdate<KeyValuePair<KeyValuePair<string, Guid[]>, KeyValuePair<string, string>[]>, Connection, AdapterDocument>(adapterId,
            //                (adapterDoc, onModified, onNotModified) =>
            //                {
            //                    var rollbackSave = adapterDoc.Name.PairWithValue(adapterDoc.GetConnectorIds()).PairWithValue(adapterDoc.GetIdentifiers());
            //                    adapterDoc.AddConnectorId(connector.connectorId);
            //                    adapterDoc.Name = name;
            //                    adapterDoc.SetIdentifiers(identifiers);
            //                    adapterDoc.ResourceType = resourceType;
            //                    return onModified(rollbackSave);
            //                },
            //                (save, adapterDoc) =>
            //                {
            //                    adapterDoc.Name = save.Key.Key;
            //                    adapterDoc.SetConnectorIds(save.Key.Value);
            //                    adapterDoc.SetIdentifiers(save.Value);
            //                    adapterDoc.ResourceType = resourceType;
            //                    return true;
            //                },
            //                () => connection,
            //                azureStorageRepository);
            //        return await rollback.ExecuteAsync(() => connection);
            //    });
        }

        internal static Task<TResult> DeleteByIdAsync<TResult>(Guid synchronizationId,
            Func<TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository =>
                {
                    return azureStorageRepository.DeleteIfAsync<ProcessStageDocument, TResult>(synchronizationId,
                        async (syncDoc, deleteSyncDocAsync) =>
                        {
                            throw new NotImplementedException();
                            return onDeleted();
                            //if (syncDoc.LocalIdMaybe.HasValue)
                            //{
                            //    bool successLocal = await azureStorageRepository.DeleteIfAsync<ConnectorDocument, bool>(syncDoc.LocalIdMaybe.Value,
                            //        async (syncLookupDoc, deleteSyncLookupDocAsync) =>
                            //        {
                            //            await deleteSyncLookupDocAsync();
                            //            return true;
                            //        },
                            //        () => false);
                            //}
                            //if (!syncDoc.RemoteId.IsNullOrWhiteSpace())
                            //{
                            //    var externalLookupId = ConnectorDocument.GetId(syncDoc.SystemName, syncDoc.ResourceType, syncDoc.ActorId, syncDoc.RemoteId);
                            //    bool successExternal = await azureStorageRepository.DeleteIfAsync<ConnectorDocument, bool>(externalLookupId,
                            //        async (syncLookupDoc, deleteSyncLookupDocAsync) =>
                            //        {
                            //            await deleteSyncLookupDocAsync();
                            //            return true;
                            //        },
                            //        () => false);
                            //}
                            //bool deletedLookup = await azureStorageRepository.UpdateAsync<SynchronizationActorLookupDocument, bool>(syncDoc.ActorId, syncDoc.SystemName,
                            //    async (syncActorLookupDoc, saveAsync) =>
                            //    {
                            //        if (syncActorLookupDoc.RemoveSynchronizationDocumentId(synchronizationId))
                            //            await saveAsync(syncActorLookupDoc);
                            //        return true;
                            //    },
                            //    () => false);

                            //await deleteSyncDocAsync();
                            //return onDeleted();
                        },
                        onNotFound);
                });
        }
        
    }
}

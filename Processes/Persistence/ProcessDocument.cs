using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Azure.Cosmos.Table;

using BlackBarLabs;
using EastFive.Collections.Generic;

using EastFive.Serialization;
using EastFive.Extensions;
using EastFive;
using EastFive.Linq;
using System.Runtime.Serialization;
using EastFive.Persistence;
using EastFive.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Persistence;

namespace EastFive.Azure.Persistence
{
    public class ProcessDocument : TableEntity
    {
        #region Properties

        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id => Guid.Parse(this.RowKey);

        public Guid ProcessStage { get; set; }

        public Guid Owner { get; set; }

        public DateTime CreatedOn { get; set; }

        public Guid Resource { get; set; }

        public string ResourceType { get; set; }

        public Guid? PreviousStep { get; set; }

        public DateTime? ConfirmedWhen { get; set; }

        public Guid? ConfirmedBy { get; set; }
        
        #region Resources

        public byte[] Resources { get; set; }
        public byte[] ResourceKeys { get; set; }

        internal Process.ProcessStageResource[] GetResources()
        {
            return Resources
                .ToNullablesFromByteArray(bytes => new Guid(bytes))
                .Zip(ResourceKeys.ToStringsFromUTF8ByteArray(),
                    (resourceId, key) =>
                        new Process.ProcessStageResource()
                        {
                            key = key,
                            resourceId = resourceId,
                        })
                .ToArray();
        }

        internal bool SetResources(Process.ProcessStageResource[] resources)
        {
            this.Resources = resources.Select(r => r.resourceId).ToByteArrayOfNullables(guid => guid.ToByteArray());
            this.ResourceKeys = resources.Select(r => r.key).ToUTF8ByteArrayOfStrings();
            return true;
        }
        
        #endregion

        #endregion

        public static Task<TResult> FindByIdAsync<TResult>(Guid processId,
            Func<Process, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository => azureStorageRepository.FindByIdAsync(processId,
                    (ProcessDocument processDocument) =>
                    {
                        return onFound(Convert(processDocument));
                    },
                    onNotFound));
        }

        public static Task<TResult> FindByResourceAsync<TResult>(Guid resourceId, Type resourceType,
            Func<Process[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                async azureStorageRepository => await await azureStorageRepository.FindLinkedDocumentsAsync(
                        resourceId, resourceType.AssemblyQualifiedName,
                        (Documents.LookupDocument fulfullmentDoc) => fulfullmentDoc.GetLookupDocumentIds(),
                    (Documents.LookupDocument fulfillmentDoc, ProcessDocument[] procStageDocs) =>
                    {
                        return onFound(procStageDocs.Select(Convert).ToArray()).AsTask();
                    },
                    onNotFound.AsAsyncFunc()));
        }

        public static Task<TResult> FindAllInFlowByActorAsync<TResult>(Guid actorId, Type resourceType,
            Func<Process[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                async azureStorageRepository => await await azureStorageRepository.FindLinkedDocumentsAsync(
                        actorId, resourceType.AssemblyQualifiedName,
                        (Documents.ProcessStepActorLookupDocument actorLookupDoc) => actorLookupDoc.GetLookupDocumentIds(),
                    async (Documents.ProcessStepActorLookupDocument actorLookupDoc, ProcessDocument [] procStepDocs, Guid [] missingElements) =>
                    {
                        while(true)
                        {
                            var processLookup = procStepDocs.Select(step => step.Id).AsHashSet();
                            var missingSteps = procStepDocs
                                    .Where(proc => proc.PreviousStep.HasValue)
                                    .Where(proc => !processLookup.Contains(proc.PreviousStep.Value))
                                    .ToArray();
                            if(!missingSteps.Any())
                                return onFound(procStepDocs.Select(Convert).ToArray());

                            procStepDocs = await missingSteps
                                .Where(step => step.PreviousStep.HasValue)
                                .Select(step => step.PreviousStep.Value)
                                .Distinct()
                                .FlatMap(
                                    new Guid[] { },
                                    async (stepId, brokenStepIds, next, skip) => await await azureStorageRepository.FindByIdAsync(stepId,
                                        (ProcessDocument prcDoc) => next(prcDoc, brokenStepIds),
                                        () => skip(brokenStepIds.Append(stepId).ToArray())),
                                    async (ProcessDocument[] newDocuments, Guid [] brokenStepIds) =>
                                    {
                                        return await procStepDocs
                                            .Concat(newDocuments)
                                            .SelectPartition(
                                                (procStageDoc, broken, okay) => procStageDoc.PreviousStep.HasValue && brokenStepIds.Contains(procStageDoc.PreviousStep.Value) ?
                                                    broken(procStageDoc)
                                                    :
                                                    okay(procStageDoc),
                                                async (ProcessDocument [] brokens, ProcessDocument[] okays) =>
                                                {
                                                    var corrected = await brokens.Select(
                                                        ps =>
                                                        {
                                                            ps.PreviousStep = default(Guid?);
                                                            return azureStorageRepository.UpdateIfNotModifiedAsync(ps,
                                                                () => ps,
                                                                () => ps);
                                                        })
                                                        .WhenAllAsync();
                                                    return okays
                                                        .Concat(corrected)
                                                        .ToArray();
                                                });
                                    });
                        }
                    },
                    onNotFound.AsAsyncFunc()));
        }
        
        internal static Process Convert(ProcessDocument processDocument)
        {
            return new Process
            {
                processId = processDocument.Id,

                processStageId = processDocument.ProcessStage,
                createdOn = processDocument.CreatedOn,

                resourceId = processDocument.Resource,
                resourceType = Type.GetType(processDocument.ResourceType),

                resources = processDocument.GetResources(),

                confirmedWhen = processDocument.ConfirmedWhen,
                confirmedBy = processDocument.ConfirmedBy,
                previousStep = processDocument.PreviousStep,
            };
        }

        internal static Task<TResult> CreateAsync<TResult>(Guid processId,
                Guid processStageId, 
                Guid actorId, Guid resourceId, Type resourceType, DateTime createdOn,
                Process.ProcessStageResource[] resources,
                Guid? previousStepId, DateTime? confirmedWhen, Guid? confirmedBy,
                Guid [] lookupActorIds,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists)
        {
            throw new NotImplementedException();
            //return AzureStorageRepository.Connection(
            //    azureStorageRepository =>
            //    {
            //        var rollback = new RollbackAsync<TResult>();

            //        var resourceTypeString = resourceType.AssemblyQualifiedName;
            //        var processDocument = new ProcessDocument()
            //        {
            //            ProcessStage = processStageId,

            //            Resource = resourceId,
            //            ResourceType = resourceTypeString,
            //            CreatedOn = createdOn,
            //            Owner = actorId,

            //            ConfirmedBy = confirmedBy,
            //            PreviousStep = previousStepId,
            //            ConfirmedWhen = confirmedWhen,
            //        };
            //        processDocument.SetResources(resources);
            //        rollback.AddTaskCreate(processId, processDocument,
            //            onAlreadyExists, azureStorageRepository);

            //        foreach(var lookupActorId in lookupActorIds.Distinct())
            //            rollback.AddTaskCreateOrUpdate<TResult, Documents.ProcessStepActorLookupDocument>(lookupActorId, resourceTypeString,
            //                (created, lookupDoc) => lookupDoc.AddLookupDocumentId(processId),
            //                actorDoc => actorDoc.RemoveLookupDocumentId(processId),
            //                azureStorageRepository);

            //        rollback.AddTaskCreateOrUpdate<TResult, Documents.ProcessStepResourceLookupDocument>(resourceId, resourceTypeString,
            //            (created, lookupDoc) => lookupDoc.AddLookupDocumentId(processId),
            //            actorDoc =>actorDoc.RemoveLookupDocumentId(processId),
            //            azureStorageRepository);
                    
            //        bool[] updates = resources
            //            .Select(
            //                resource =>
            //                {
            //                    if (!resource.resourceId.HasValue)
            //                        return true;
            //                    rollback.AddTaskCreateOrUpdate<TResult, Documents.ProcessStepResourceKeyLookupDocument>(
            //                            resource.resourceId.Value, resource.type.AssemblyQualifiedName,
            //                        (created, lookupDoc) => lookupDoc.AddLookupDocumentId(processId),
            //                        lookupDoc => lookupDoc.RemoveLookupDocumentId(processId),
            //                        azureStorageRepository);
            //                    return true;
            //                })
            //            .ToArray();

            //        return rollback.ExecuteAsync(onSuccess);
            //    });
        }

        internal static Task<TResult> UpdateAsync<TResult>(Guid processId,
            Func<Process, Func<Process, Task>, Task<TResult>> onFound,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository =>
                {
                    return azureStorageRepository.UpdateAsync<ProcessDocument, TResult>(processId,
                        (doc, saveAsync) =>
                        {
                            return onFound(Convert(doc),
                                async proc =>
                                {
                                    doc.ConfirmedBy = proc.confirmedBy;
                                    doc.ConfirmedWhen = proc.confirmedWhen;
                                    doc.SetResources(proc.resources);
                                    await saveAsync(doc);
                                });
                        },
                        onNotFound);
                });
        }

        internal static Task<TResult> DeleteByIdAsync<TResult>(Guid processStepId,
            Func<TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                azureStorageRepository =>
                {
                    return azureStorageRepository.DeleteIfAsync<ProcessDocument, TResult>(processStepId,
                        async (processStepDocument, deleteProcStepDocAsync) =>
                        {

                            bool updatedActorLookup = await azureStorageRepository.UpdateAsync<Documents.ProcessStepActorLookupDocument, bool>(
                                    processStepDocument.Owner, processStepDocument.ResourceType,
                                async (ownerDoc, saveAsync) =>
                                {
                                    if (ownerDoc.RemoveLookupDocumentId(processStepId))
                                        await saveAsync(ownerDoc);
                                    return true;
                                },
                                () => false);

                            bool deletedResource = await azureStorageRepository.UpdateAsync<Documents.ProcessStepResourceLookupDocument, bool>(
                                    processStepDocument.Resource, processStepDocument.ResourceType,
                                async (resourceLookupDoc, saveAsync) =>
                                {
                                    if (resourceLookupDoc.RemoveLookupDocumentId(processStepId))
                                        await saveAsync(resourceLookupDoc);
                                    return true;
                                },
                                () => false);

                            bool [] deletedResourceKeys = await processStepDocument.GetResources()
                                .Where(resource => resource.resourceId.HasValue)
                                .Select(
                                    resource => azureStorageRepository.UpdateAsync<Documents.ProcessStepResourceKeyLookupDocument, bool>(
                                            resource.resourceId.Value, resource.type.AssemblyQualifiedName,
                                        async (resourceLookupDoc, saveAsync) =>
                                        {
                                            if (resourceLookupDoc.RemoveLookupDocumentId(processStepId))
                                                await saveAsync(resourceLookupDoc);
                                            return true;
                                        },
                                        () => false))
                                .WhenAllAsync();

                            await deleteProcStepDocAsync();
                            return onDeleted();
                        },
                        onNotFound);
                });
        }
        
    }
}

using EastFive.Api.Controllers;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure
{
    public struct ProcessStage
    {
        public Guid processStageId;
        public string title;
        public Guid ownerId;
        public Guid processStageTypeId;
        public KeyValuePair<Guid[], Guid>[] confirmableIds;
        public Guid[] editableIds;
        public Guid[] completableIds;
        public Guid[] viewableIds;
        public string [] resourcesDisplayed;
    }

    public static class ProcessStages
    {
        public static Task<TResult> CreateAsync<TResult>(Guid processStageId, Guid actorId,
                Guid processStageTypeId, string title,
                Guid[] viewableActorIds, Guid[] editableActorIds, Guid [] completableActorIds,
                KeyValuePair<Guid[], Guid>[] confirmableActorIdsNexts,
                EastFive.Api.Security security,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists,
            Func<TResult> onTypeDoesNotExist,
            Func<Guid, TResult> onConfirmationStageDoesNotExist,
            Func<TResult> onActorDoesNotExist,
            Func<string, TResult> onFailure)
        {
            // TODO: Security
            return ProcessStages.CreateAsync(processStageId, actorId,
                    processStageTypeId, title,
                    viewableActorIds, editableActorIds, completableActorIds,
                    confirmableActorIdsNexts,
                onCreated,
                onAlreadyExists,
                onTypeDoesNotExist,
                onConfirmationStageDoesNotExist, 
                onActorDoesNotExist, 
                onFailure);
        }

        public async static Task<TResult> CreateAsync<TResult>(Guid processStageId, Guid actorId,
                Guid processStageTypeId, string title,
                Guid[] viewableActorIds, Guid[] editableActorIds, Guid[] completableActorIds,
                KeyValuePair<Guid[], Guid>[] confirmableActorIdsNexts,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists,
            Func<TResult> onTypeDoesNotExist,
            Func<Guid, TResult> onStageDoesNotExist,
            Func<TResult> onActorDoesNotExist,
            Func<string, TResult> onFailure)
        {
            return await await ProcessStageTypes.FindIdAsync(processStageTypeId,
                async (stageType) =>
                {
                    var computedTitle = title.IsNullOrWhiteSpace() ?
                        stageType.title
                        :
                        title;

                    // TODO: Validate the stage
                    // TODO: Validate the actors
                    return await await Persistence.ProcessStageDocument.FindByIdsAsync(
                        confirmableActorIdsNexts.Select(confirmableActorIdsNext => confirmableActorIdsNext.Value),
                        async (confirmableNextStages, missingNextStageIds) =>
                        {
                            if (missingNextStageIds.Any())
                                return onStageDoesNotExist(missingNextStageIds.First());

                            return await Persistence.ProcessStageDocument.CreateAsync(processStageId, actorId,
                                    processStageTypeId, computedTitle,
                                    confirmableActorIdsNexts, editableActorIds, viewableActorIds,
                                onCreated,
                                onAlreadyExists);
                            
                        });
                },
                () => onTypeDoesNotExist().AsTask());
        }

        public static Task<TResult> FindByIdAsync<TResult>(Guid processStageId, EastFive.Api.Security security,
            Func<ProcessStage, TResult> onFound,
            Func<TResult> onNotFound,
            Func<TResult> onUnauthorized)
        {
            return Persistence.ProcessStageDocument.FindByIdAsync(processStageId,
                (processStage) =>
                {
                    return onFound(processStage);
                },
                onNotFound);
        }

        public static Task<TResult> FindByResourceAsync<TResult>(Guid resourceId, EastFive.Api.Security security,
            Func<ProcessStage[], TResult> onFound,
            Func<TResult> onResourceNotFound,
            Func<TResult> onUnauthorized)
        {
            return Persistence.ProcessStageDocument.FindByResourceAsync(resourceId,
                (processStages) =>
                {
                    return onFound(processStages);
                },
                onResourceNotFound);
        }

        public static async Task<TResult> FindStartByActorAndResourceTypeAsync<TResult>(Guid ownerId, Type resourceType, EastFive.Api.Security security,
            Func<ProcessStage[], TResult> onFound,
            Func<TResult> onResourceNotFound,
            Func<TResult> onUnauthorized)
        {
            return await await Persistence.ProcessStageDocument.FindByOwnerAsync(ownerId,
                async (processStages) =>
                {
                    var typeIds = await processStages
                        .Select(processStage => processStage.processStageTypeId)
                        .Distinct()
                        .FlatMap(
                            async (processStageTypeId, add, skip) => await await Persistence.ProcessStageTypeDocument.FindByIdAsync(processStageTypeId,
                                processStageTypeDoc => processStageTypeDoc.resourceType == resourceType ?
                                    add(processStageTypeId)
                                    :
                                    skip(),
                                () => skip()),
                            (IEnumerable<Guid> processStageTypeIds) => processStageTypeIds.ToLookup(v => v).AsTask());
                    var listedAsNext = processStages.SelectMany(procStage => procStage.confirmableIds.SelectValues()).ToLookup(v => v);
                    return onFound(
                        processStages
                            .Where(procStage => typeIds.Contains(procStage.processStageTypeId))
                            .Where(procStage => !listedAsNext.Contains(procStage.processStageId))
                            .ToArray());
                },
                onResourceNotFound.AsAsyncFunc());
        }
        
        internal static Task<TResult> UpdateAsync<TResult>(Guid processStageId,
                Guid? processStageTypeId, string title,
                Guid[] viewableIds, Guid[] completableIds, Guid[] editableIds,
                KeyValuePair<Guid[], Guid>[] confirmables,
                EastFive.Api.Security security,
            Func<TResult> onUpdated,
            Func<TResult> onNotFound,
            Func<TResult> onUnauthorized,
            Func<string, TResult> onFailure)
        {
            return Persistence.ProcessStageDocument.UpdateAsync(processStageId,
                async (processStage, saveAsync) =>
                {
                    if (!title.IsNullOrWhiteSpace())
                        processStage.title = title;
                    if (processStageTypeId.HasValue)
                        processStage.processStageTypeId = processStageTypeId.Value;
                    if (!viewableIds.IsDefaultOrNull())
                        processStage.viewableIds = viewableIds;
                    if (!completableIds.IsDefaultOrNull())
                        processStage.completableIds = completableIds;
                    if (!editableIds.IsDefaultOrNull())
                        processStage.editableIds = editableIds;
                    if (!confirmables.IsDefaultOrNull())
                        processStage.confirmableIds = confirmables;

                    await saveAsync(processStage);
                    return onUpdated();
                },
                onNotFound);
        }

        public static Task<TResult> DeleteByIdAsync<TResult>(Guid processStageId, EastFive.Api.Security security,
            Func<TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            return Persistence.ProcessStageDocument.DeleteByIdAsync(processStageId,
                () =>
                {
                    return onDeleted();
                },
                onNotFound);
        }
    }
}

using EastFive.Api.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Extensions;

namespace EastFive.Azure
{
    public struct Process
    {
        public Guid processId;

        public Guid processStageId;
        public DateTime createdOn;

        public Guid resourceId;
        public Type resourceType;

        public Guid? previousStep;
        public Guid? confirmedBy;
        public DateTime? confirmedWhen;
        public DateTime? invalidatedWhen;

        public struct ProcessStageResource
        {
            public Guid? resourceId;
            public string key;
            public Type type;
        }
        public ProcessStageResource[] resources;
    }

    public static class Processes
    {
        public static async Task<TResult> CreateAsync<TResult>(Guid processId,
                Guid processStageId, Guid resourceId, DateTime createdOn,
                KeyValuePair<string, Guid>[] resourceIds,
                Guid? previousStepId, DateTime? confirmedWhen, Guid? confirmedBy,
                EastFive.Api.Security security,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists,
            Func<TResult> onStageDoesNotExist,
            Func<string, TResult> onFailure)
        {
            return await await Persistence.ProcessStageDocument.FindByIdAsync(processStageId,
                async stage =>
                {
                    return await await ProcessStageTypes.FindIdAsync(stage.processStageTypeId,
                        async (stageType) =>
                        {
                            var resourceType = stageType.resourceType;

                            return await stageType.resourceKeys
                                .FlatMap(resourceIds.ToDictionary(),
                                    async (kvp, unusedResourceKvps, next, skip, tail) =>
                                    {
                                        if (!unusedResourceKvps.ContainsKey(kvp.Key))
                                        {
                                            if (confirmedWhen.HasValue)
                                                return await tail(onFailure($"Missing resource for `{kvp.Key}`").AsTask());
                                            return await skip(unusedResourceKvps);
                                        }
                                        return await next(
                                            new Process.ProcessStageResource()
                                            {
                                                key = kvp.Key,
                                                resourceId = unusedResourceKvps[kvp.Key],
                                                type = kvp.Value,
                                            },
                                            unusedResourceKvps
                                                .Where(kvpR => kvpR.Key != kvp.Key)
                                                .ToDictionary());
                                    },
                                    async (Process.ProcessStageResource[] procStageResources, Dictionary<string, Guid> unusedResourceIds) =>
                                    {
                                        if (unusedResourceIds.Any())
                                            return onFailure($"`{unusedResourceIds.First().Key}` is not valid resource key for a stage of type `{stageType.processStageTypeId}`");

                                        var applicableActors = stage.completableIds
                                            .Concat(stage.confirmableIds.SelectKeys().SelectMany())
                                            .Concat(stage.editableIds).Concat(stage.viewableIds)
                                            .Distinct()
                                            .ToArray();

                                        if (previousStepId.HasValue)
                                            return await await Persistence.ProcessDocument.FindByIdAsync(previousStepId.Value,
                                                async previousStep =>
                                                {
                                                    if (!(previousStep.confirmedBy.HasValue && previousStep.confirmedWhen.HasValue))
                                                        return onFailure("Previous step has not been confirmed.");

                                                    //TODO: if(previousStep.nextStep.HasValue)

                                                    return await Persistence.ProcessDocument.CreateAsync(processId,
                                                            processStageId, stage.ownerId,
                                                            resourceId, resourceType, createdOn,
                                                            procStageResources,
                                                            previousStepId, confirmedWhen, confirmedBy,
                                                            applicableActors,
                                                        onCreated,
                                                        onAlreadyExists);
                                                },
                                                () => onFailure("Previous step does not exist").AsTask());

                                        // TODO: If confirmed is set, ensure that the security actor posesses a position that is authorized to move the process forward
                                        return await Persistence.ProcessDocument.CreateAsync(processId,
                                                        processStageId, stage.ownerId,
                                                        resourceId, resourceType, createdOn,
                                                        procStageResources,
                                                        previousStepId, confirmedWhen, confirmedBy,
                                                        applicableActors,
                                                    onCreated,
                                                    onAlreadyExists);
                                    });
                        },
                        onStageDoesNotExist.AsAsyncFunc());
                },
                onStageDoesNotExist.AsAsyncFunc());
        }

        public static Task<TResult> FindByIdAsync<TResult>(Guid processStageId, EastFive.Api.Security security,
            Func<Process, TResult> onFound,
            Func<TResult> onNotFound,
            Func<TResult> onUnauthorized)
        {
            return Persistence.ProcessDocument.FindByIdAsync(processStageId,
                (processStage) =>
                {
                    return onFound(processStage);
                },
                onNotFound);
        }

        public static Task<TResult> FindByResourceAsync<TResult>(Guid resourceId, Type resourceType,
                EastFive.Api.Security security,
            Func<Process[], TResult> onFound,
            Func<TResult> onResourceNotFound,
            Func<TResult> onUnauthorized)
        {
            return Persistence.ProcessDocument.FindByResourceAsync(resourceId, resourceType,
                (processStages) =>
                {
                    return onFound(processStages);
                },
                onResourceNotFound);
        }

        internal static Task<TResult> UpdateAsync<TResult>(Guid processId,
            Guid? confirmedById, DateTime? confirmedWhen, 
            KeyValuePair<string, Guid>[] resources, EastFive.Api.Security security,
            Func<TResult> onUpdated, 
            Func<TResult> onNotFound,
            Func<TResult> onUnauthorized,
            Func<string, TResult> onFailure)
        {
            return Persistence.ProcessDocument.UpdateAsync(processId,
                async (process, saveAsync) =>
                {
                    return await await Persistence.ProcessStageDocument.FindByIdAsync(process.processStageId,
                        async processStage => await await Persistence.ProcessStageTypeDocument.FindByIdAsync(processStage.processStageTypeId,
                            processStageType =>
                            {
                                process.confirmedBy = confirmedById;
                                process.confirmedWhen = confirmedWhen;
                                return resources
                                    .FlatMap(
                                        (resourceKvp, nextProcessStageResource, skip, tail) => processStageType.resourceKeys
                                            .First(
                                                (kvp, next) => kvp.Key == resourceKvp.Key ?
                                                    nextProcessStageResource(
                                                        new Process.ProcessStageResource()
                                                        {
                                                            key = resourceKvp.Key,
                                                            resourceId = resourceKvp.Value,
                                                            type = kvp.Value,
                                                        })
                                                    :
                                                    next(),
                                                () => tail(onFailure($"Resource key ${resourceKvp.Key} is not valid for this stage.").AsTask())),
                                        async (IEnumerable<Process.ProcessStageResource> psrs) =>
                                        {
                                            var procStageResources = psrs.ToArray();
                                            if (procStageResources.Length != processStageType.resourceKeys.Length)
                                                if (confirmedWhen.HasValue || process.confirmedWhen.HasValue)
                                                    return onFailure($"Cannot confirm resource without `{processStageType.resourceKeys.SelectKeys().Except(procStageResources.Select(psr => psr.key)).Join(",")}`");
                                            process.resources = procStageResources;
                                            await saveAsync(process);
                                            return onUpdated();
                                        });
                            },
                            () => onFailure($"Process stage is no longer valid `{process.processStageId}` => `{processStage.processStageTypeId}`").AsTask()),
                        () => onFailure($"Process is no longer valid `{processId}` => `{process.processStageId}`").AsTask());
                },
                onNotFound);
            
        }

        public static Task<TResult> DeleteByIdAsync<TResult>(Guid processStageId, EastFive.Api.Security security,
            Func<TResult> onDeleted,
            Func<TResult> onNotFound,
            Func<TResult> onUnauthorized)
        {
            return Persistence.ProcessDocument.DeleteByIdAsync(processStageId,
                () =>
                {
                    return onDeleted();
                },
                onNotFound);
        }
    }
}

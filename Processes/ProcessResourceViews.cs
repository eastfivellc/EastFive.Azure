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
    public struct ProcessResourceView
    {
        public Guid processViewId;
        public Guid actorId;
        public Guid resourceId;
        public Type resourceType;

        public Guid currentProcessStepId;
        public string[] titles;
        public DateTime?[] completions;
        public DateTime?[] invalidations;

        public string[] displayResources;
        public Process.ProcessStageResource[] resourcesProvided;

        public ProcessStage[] nextStages;
        public bool editable;
        public bool completable;
    }

    public static class ProcessResourceViews
    {
        public async static Task<TResult> FindByResourceAsync<TResult>(Guid actorId, Type resourceType,
                EastFive.Api.Security security,
            Func<ProcessResourceView[], TResult> onFound,
            Func<TResult> onResourceNotFound,
            Func<TResult> onUnauthorized)
        {
            return await await Persistence.ProcessDocument.FindAllInFlowByActorAsync(actorId, resourceType,
                (processSteps) =>
                {
                    return processSteps
                        .Select(ps => ps.processStageId)
                        .Distinct()
                        .FlatMap(
                            async (processStageId, next, skip) => await await Persistence.ProcessStageDocument.FindByIdAsync<Task<TResult>>(processStageId,
                                processStage => next(processStageId.PairWithValue(processStage)),
                                () => skip()),
                            (IEnumerable<KeyValuePair<Guid, ProcessStage>> processStages) =>
                            {
                                var processStageLookup = processStages.ToDictionary();
                                return processStages
                                    .SelectValues(stage => stage.processStageTypeId)
                                    .Distinct()
                                    .FlatMap<Guid, Guid[], KeyValuePair<Guid, ProcessStageType>, Task<TResult>>(
                                        new Guid[] { },
                                        async (processStageTypeId, kvpsAggr, next, skip) => await await ProcessStageTypes.FindIdAsync(processStageTypeId,
                                            stageType => next(processStageTypeId.PairWithValue(stageType), kvpsAggr),
                                            () => skip(kvpsAggr.Append(processStageTypeId).ToArray())),
                                        (KeyValuePair<Guid, ProcessStageType>[] stageTypes, Guid[] missingValues) =>
                                        {
                                            var processStageTypeLookup = stageTypes.ToDictionary();
                                            return processSteps
                                                .GroupBy(processStep => processStep.resourceId)
                                                .FlatMap(
                                                    (stepsUnordered, next, skip) =>
                                                    {
                                                        var steps = stepsUnordered
                                                            .OrderWith(default(Guid?),
                                                                (carry, step) => step.previousStep == carry,
                                                                step => step.processId)
                                                            .ToArray();
                                                        if (!steps.Any())
                                                            return skip();
                                                        var viewableSteps = steps
                                                            .Where(step => processStageLookup[step.processStageId].viewableIds.Contains(actorId))
                                                            .ToArray();
                                                        var activeStep = viewableSteps.Last();
                                                        var stage = processStageLookup[activeStep.processStageId];

                                                        var possibleResourceKeys = viewableSteps
                                                            .SelectMany(step => processStageTypeLookup[processStageLookup[step.processStageId].processStageTypeId].resourceKeys.SelectKeys())
                                                            .Distinct()
                                                            .Select(
                                                                key => key.PairWithValue(new Process.ProcessStageResource
                                                                {
                                                                    key = key,
                                                                    type = processStageTypeLookup.SelectValues().First(
                                                                        (v, nextStageType) =>
                                                                        {
                                                                            return v.resourceKeys.First(
                                                                                (v2, nextKey) =>
                                                                                {
                                                                                    if (v2.Key == key)
                                                                                        return v2.Value;
                                                                                    return nextKey();
                                                                                },
                                                                                () => nextStageType());
                                                                        },
                                                                        () => default(Type)),
                                                                }))
                                                            .ToDictionary();

                                                        try
                                                        {
                                                            var view = new ProcessResourceView()
                                                            {
                                                                processViewId = activeStep.processId,
                                                                resourceId = activeStep.resourceId,
                                                                actorId = actorId,
                                                                currentProcessStepId = activeStep.processId,
                                                                titles = viewableSteps
                                                                        .Select(step => processStageLookup[step.processStageId].title)
                                                                        .ToArray(),
                                                                completions = viewableSteps
                                                                        .Select(step => step.confirmedWhen)
                                                                        .ToArray(),
                                                                invalidations = viewableSteps
                                                                        .Select(step => step.invalidatedWhen)
                                                                        .ToArray(),
                                                                resourceType = resourceType,
                                                                resourcesProvided = steps
                                                                        .Where(step => step.confirmedWhen.HasValue)
                                                                        .Aggregate(
                                                                            possibleResourceKeys,
                                                                            (aggr, step) =>
                                                                            {
                                                                                // TODO: This probably does not work
                                                                                foreach (var resource in step.resources)
                                                                                {
                                                                                    // In case it is not a resource that is
                                                                                    // referenced in a viewable step
                                                                                    if (!aggr.ContainsKey(resource.key))
                                                                                        continue;

                                                                                    var aggrv = aggr[resource.key];
                                                                                    if(resource.resourceId.HasValue)
                                                                                        aggrv.resourceId = resource.resourceId;
                                                                                    aggrv.type = resource.type;
                                                                                }
                                                                                return aggr;
                                                                            })
                                                                        .SelectValues()
                                                                        .ToArray(),
                                                                nextStages = stage.confirmableIds
                                                                        .Where(actorKvps => actorKvps.Key.Contains(actorId))
                                                                        .Select(actorKvps => new ProcessStage()
                                                                        {
                                                                        // TODO: Rest of these values
                                                                        processStageId = actorKvps.Value,
                                                                        })
                                                                        .ToArray(),
                                                                displayResources = processStageLookup[activeStep.processStageId]
                                                                    .resourcesDisplayed
                                                                    .NullToEmpty()
                                                                    .Intersect(possibleResourceKeys.SelectKeys())
                                                                    .ToArray(),
                                                                editable = processStageLookup[activeStep.processStageId].editableIds.Contains(actorId),
                                                                completable = processStageLookup[activeStep.processStageId].completableIds.Contains(actorId),
                                                            };
                                                            return next(view);
                                                        } catch (Exception)
                                                        {
                                                            return skip();
                                                        }
                                                    },
                                                    (IEnumerable<ProcessResourceView> views) => onFound(views.ToArray())).AsTask();
                                        });
                            });
                    
                },
                () => onFound(new ProcessResourceView[] { }).AsTask());
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

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
    public struct ProcessStageType
    {
        public Guid processStageTypeId;
        public Guid processStageGroupId;
        public string title;
        public Type resourceType;
        public KeyValuePair<string, Type>[] resourceKeys;
    }

    public static class ProcessStageTypes
    {
        public static Task<TResult> CreateAsync<TResult>(Guid processStageTypeId, Guid actorId,
                Guid processStageGroupId, string title, Type resourceType, KeyValuePair<string, Type>[] resourceKeys,
                EastFive.Api.Security security,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists,
            Func<TResult> onTypeDoesNotExist,
            Func<Guid, TResult> onActorDoesNotExist,
            Func<string, TResult> onFailure)
        {
            // TODO: Security
            return EastFive.Api.Azure.Resources.ProcessStageGroup.stages.First(
                async (stageGroup, nextStageGroup) =>
                {
                    if (stageGroup.Id.UUID != processStageGroupId)
                        return await nextStageGroup();
                    
                    return await Persistence.ProcessStageTypeDocument.CreateAsync(processStageTypeId, actorId,
                            processStageGroupId, title, resourceType, resourceKeys,
                        onCreated,
                        onAlreadyExists);
                },
                () => onTypeDoesNotExist().AsTask());
        }
        
        public static Task<TResult> FindAllAsync<TResult>(EastFive.Api.Security security,
            Func<ProcessStageType[], TResult> onFound,
            Func<TResult> onUnauthorized)
        {
            return Persistence.ProcessStageTypeDocument.FindAllAsync(onFound);
        }

        public static Task<ProcessStageType[]> FindAllAsync()
        {
            return Persistence.ProcessStageTypeDocument.FindAllAsync(x => x);
        }

        public static Task<TResult> FindIdAsync<TResult>(Guid processStageTypeId,
            Func<ProcessStageType, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return Persistence.ProcessStageTypeDocument.FindByIdAsync(processStageTypeId, onFound, onNotFound);
        }
    }
}

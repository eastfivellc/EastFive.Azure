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

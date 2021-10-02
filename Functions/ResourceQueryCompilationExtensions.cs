using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Functions
{
    public static class ResourceQueryCompilationExtensions
    {
        public static async Task<InvocationMessage> FunctionAsync(this IHttpRequest request,
            string serviceBusTriggerNameOverride = default)
        {
            var invocationMessage = await request.CreateInvocationMessageAsync();
            return await invocationMessage.SendToFunctionsAsync(serviceBusTriggerNameOverride);
        }

        public static async Task<InvocationMessage> CreateInvocationMessageAsync(this IHttpRequest request,
            int executionLimit = 1)
        {
            var invocationMessage = await request.InvocationMessageAsync(executionLimit: executionLimit);
            var invocationMessageRef = invocationMessage.invocationRef;
            return await invocationMessage.StorageCreateAsync(
                (created) =>
                {
                    return invocationMessage;
                },
                () => throw new Exception());
        }

        public static async Task<InvocationMessage> SendToFunctionsAsync(this InvocationMessage invocationMessage, 
            string serviceBusTriggerNameOverride = default)
        {
            var invocationMessageRef = invocationMessage.invocationRef;
            var byteContent = invocationMessageRef.id.ToByteArray();

            var serviceBusTriggerName = serviceBusTriggerNameOverride.HasBlackSpace() ?
                serviceBusTriggerNameOverride :
                AppSettings.FunctionProcessorServiceBusTriggerName.ConfigurationString(value => value, (why) => throw new Exception(why));

            await AzureApplication.SendServiceBusMessageStaticAsync(serviceBusTriggerName,
                byteContent.AsEnumerable());
            return invocationMessage;
        }
    }
}

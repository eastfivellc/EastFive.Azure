using BlackBarLabs.Extensions;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Web.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Functions
{
    public static class ResourceQueryCompilationExtensions
    {
        public static async Task<InvocationMessage> FunctionAsync(this HttpRequestMessage request)
        {
            var invocationMessage = await request.CreateInvocationMessageAsync();
            return await invocationMessage.SendToFunctionsAsync();
        }

        public static async Task<InvocationMessage> CreateInvocationMessageAsync(this HttpRequestMessage request)
        {
            var invocationMessage = await request.InvocationMessageAsync();
            var invocationMessageRef = invocationMessage.invocationRef;
            return await invocationMessage.StorageCreateAsync(
                (created) =>
                {
                    return invocationMessage;
                },
                () => throw new Exception());
        }

        public static async Task<InvocationMessage> SendToFunctionsAsync(this InvocationMessage invocationMessage)
        {
            var invocationMessageRef = invocationMessage.invocationRef;
            var byteContent = invocationMessageRef.id.ToByteArray();
            return await AppSettings.FunctionProcessorServiceBusTriggerName.ConfigurationString(
                async (serviceBusTriggerName) =>
                {
                    await AzureApplication.SendServiceBusMessageStaticAsync(serviceBusTriggerName,
                        byteContent.AsEnumerable());
                    return invocationMessage;
                },
                (why) => throw new Exception(why));
        }
    }
}

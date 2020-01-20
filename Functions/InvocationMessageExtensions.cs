using BlackBarLabs.Extensions;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Functions
{
    public static class InvocationMessageExtensions
    {
        public static async Task<InvocationMessage> InvocationMessageAsync(this HttpRequestMessage request)
        {
            var invocationMessageRef = Ref<InvocationMessage>.SecureRef();
            var invocationMessage = new InvocationMessage
            {
                invocationRef = invocationMessageRef,
                headers = request.Headers
                    .Select(hdr => hdr.Key.PairWithValue(hdr.Value.First()))
                    .ToDictionary(),
                requestUri = new Uri(System.Web.HttpUtility.UrlDecode(request.RequestUri.OriginalString)),
                content = request.Content.IsDefaultOrNull() ?
                    default(byte[])
                    :
                    await request.Content.ReadAsByteArrayAsync(),
                method = request.Method.Method,
            };
            return invocationMessage;
        }

        public static async Task<InvocationMessage> InvocationMessageCreateAsync(
            this HttpRequestMessage requestMessage)
        {
            var invocationMessageRef = Ref<InvocationMessage>.SecureRef();
            var invocationMessage = await requestMessage.InvocationMessageAsync();
            return await invocationMessage.StorageCreateAsync(
                (created) =>
                {
                    return invocationMessage;
                });
        }

        public static async Task<InvocationMessage> SendAsync<TResource>(this Task<InvocationMessage> invocationMessageTask,
            AzureApplication application = default)
        {
            var invocationMessage = await invocationMessageTask;
            var byteContent = invocationMessage.invocationRef.id.ToByteArray();
            AzureApplication GetApplication()
            {
                if (!application.IsDefaultOrNull())
                    return application;
                var app = new AzureApplication();
                app.ApplicationStart();
                return app;
            }
            var applicationValid = GetApplication();
            return await EastFive.Web.Configuration.Settings.GetString(
                AppSettings.FunctionProcessorServiceBusTriggerName,
                async (serviceBusTriggerName) =>
                {
                    await applicationValid.SendServiceBusMessageAsync(serviceBusTriggerName, byteContent);
                    return invocationMessage;
                },
                (why) => throw new Exception(why));
        }
    }
}

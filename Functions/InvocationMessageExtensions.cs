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
                referrer = request.Headers.Referrer.IsDefaultOrNull()?
                    default
                    :
                    new Uri(System.Web.HttpUtility.UrlDecode(request.Headers.Referrer.OriginalString)),
                content = request.Content.IsDefaultOrNull() ?
                    default(byte[])
                    :
                    await request.Content.ReadAsByteArrayAsync(),
                invocationMessageSource = GetInvocationMessageSource(),
                method = request.Method.Method,
            };
            return invocationMessage;

            IRefOptional<InvocationMessage> GetInvocationMessageSource()
            {
                if(!request.Headers.TryGetValues(InvocationMessage.InvocationMessageSourceHeaderKey, out IEnumerable<string> invocationMessageSourceStrs))
                    return RefOptional<InvocationMessage>.Empty();

                if(!invocationMessageSourceStrs.Any())
                    return RefOptional<InvocationMessage>.Empty();

                var invocationMessageSourceStr = invocationMessageSourceStrs.First();

                if (!Guid.TryParse(invocationMessageSourceStr, out Guid invocationMessageSource))
                    return RefOptional<InvocationMessage>.Empty();

                return invocationMessageSource.AsRefOptional<InvocationMessage>();
            }
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

        public static async Task<InvocationMessage> SendAsync(this Task<InvocationMessage> invocationMessageTask)
        {
            var invocationMessage = await invocationMessageTask;
            return await invocationMessage.SendAsync();
        }

        public static async Task<InvocationMessage> SendAsync(this InvocationMessage invocationMessage)
        {
            var byteContent = invocationMessage.invocationRef.id.ToByteArray();
            return await EastFive.Web.Configuration.Settings.GetString(
                AppSettings.FunctionProcessorServiceBusTriggerName,
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

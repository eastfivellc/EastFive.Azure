using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Functions
{
    public static class InvocationMessageExtensions
    {
        public static async Task<InvocationMessage> InvocationMessageAsync(this IHttpRequest request,
            int executionLimit = 1)
        {
            var invocationMessageRef = Ref<InvocationMessage>.SecureRef();
            var referrer = request.TryGetReferer(out Uri refererTmp) ?
                    refererTmp
                    :
                    new Uri(System.Web.HttpUtility.UrlDecode(refererTmp.OriginalString));

            var content = await request.ReadContentAsync();
            var invocationMessage = new InvocationMessage
            {
                invocationRef = invocationMessageRef,
                headers = request.Headers
                    .Select(hdr => hdr.Key.PairWithValue(hdr.Value.First()))
                    .ToDictionary(),
                requestUri = new Uri(System.Web.HttpUtility.UrlDecode(request.RequestUri.OriginalString)),
                referrer = referrer,
                content = content,
                invocationMessageSource = GetInvocationMessageSource(),
                method = request.Method.Method,
                executionHistory = new KeyValuePair<DateTime, int>[] { },
                executionLimit = executionLimit,
            };
            return invocationMessage;

            IRefOptional<InvocationMessage> GetInvocationMessageSource()
            {
                if(!request.TryGetHeader(InvocationMessage.InvocationMessageSourceHeaderKey,
                        out string invocationMessageSourceStr))
                    return RefOptional<InvocationMessage>.Empty();

                if (!Guid.TryParse(invocationMessageSourceStr, out Guid invocationMessageSource))
                    return RefOptional<InvocationMessage>.Empty();

                return invocationMessageSource.AsRefOptional<InvocationMessage>();
            }

        }

        public static async Task<InvocationMessage?> InvocationMessageCreateAsync(
            this IHttpRequest requestMessage)
        {
            var invocationMessageRef = Ref<InvocationMessage>.SecureRef();
            var invocationMessage = await requestMessage.InvocationMessageAsync();
            return await invocationMessage.StorageCreateAsync(
                (created) =>
                {
                    return created.Entity;
                },
                () => default(InvocationMessage?));
        }

        public static async Task<InvocationMessage?> SendAsync(this Task<InvocationMessage> invocationMessageTask)
        {
            var invocationMessage = await invocationMessageTask;
            return await invocationMessage.SendAsync();
        }

        public static async Task<InvocationMessage?> SendAsync(this InvocationMessage invocationMessage)
        {
            var byteContent = invocationMessage.invocationRef.id.ToByteArray();
            return await EastFive.Web.Configuration.Settings.GetString(
                AppSettings.FunctionProcessorServiceBusTriggerName,
                async (serviceBusTriggerName) =>
                {
                    try
                    {
                        await AzureApplication.SendServiceBusMessageStaticAsync(serviceBusTriggerName, invocationMessage.id.ToString("N"),
                            byteContent.AsEnumerable());
                        return invocationMessage;
                    } catch(Exception)
                    {
                        return default(InvocationMessage?);
                    }
                },
                (why) => default(InvocationMessage?).AsTask());
        }
    }
}

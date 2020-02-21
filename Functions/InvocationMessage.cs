using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using EastFive.Api.Controllers;
using EastFive.Api.Azure;
using EastFive.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Queue;
using EastFive.Linq.Async;
using EastFive.Linq;
using EastFive.Analytics;
using System.Linq.Expressions;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using System.Net.Http.Headers;

namespace EastFive.Azure.Functions
{
    [FunctionViewController6(
        Route = "InvocationMessage",
        Resource = typeof(InvocationMessage),
        ContentType = "x-application/eastfive.azure.invocation-message",
        ContentTypeVersion = "0.1")]
    [DataContract]
    [StorageTable]
    public struct InvocationMessage : IReferenceable
    {
        public const string InvocationMessageSourceHeaderKey = "X-InvocationMessageSource";

        #region Properties

        [JsonIgnore]
        public Guid id => this.invocationRef.id;

        public const string IdPropertyName = "id";
        [JsonProperty(PropertyName = IdPropertyName)]
        [ApiProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<InvocationMessage> invocationRef;

        public const string LastModifiedPropertyName = "last_modified";
        [LastModified]
        [DateTimeLookup(
            Partition = DateTimeLookupAttribute.hours * DateTimeLookupAttribute.hoursPerDay,
            Row = DateTimeLookupAttribute.hours)]
        [JsonProperty]
        public DateTimeOffset lastModified;

        [JsonProperty]
        [Storage]
        public Uri requestUri;

        [JsonProperty]
        [Storage]
        public string method;

        [JsonProperty]
        [Storage]
        public Uri referrer;

        public const string InvocationMessageSourcePropertyName = "InvocationMessageSource";
        [JsonProperty]
        [Storage]
        [IdPrefixLookup(Characters = 4)]
        public IRefOptional<InvocationMessage> invocationMessageSource;

        [JsonProperty]
        [Storage]
        public IDictionary<string, string> headers;

        [JsonProperty]
        [Storage]
        public byte[] content;

        public const string LastExecutedPropertyName = "last_executed";
        [JsonProperty(PropertyName = LastExecutedPropertyName)]
        [ApiProperty(PropertyName = LastExecutedPropertyName)]
        [Storage]
        [DateTimeLookup(
            Partition = DateTimeLookupAttribute.hours * DateTimeLookupAttribute.hoursPerDay,
            Row = DateTimeLookupAttribute.hours)]
        public DateTime? lastExecuted;

        #endregion

        #region Http Methods

        [Api.HttpGet]
        [RequiredClaim(Microsoft.IdentityModel.Claims.ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static Task<HttpResponseMessage> ListAsync(
            [QueryParameter(Name = "start_time")]DateTime startTime,
            [QueryParameter(Name = "end_time")]DateTime endTime,
            [HeaderLog]EastFive.Analytics.ILogger analyticsLog,
            MultipartResponseAsync<InvocationMessage> onRun)
        {
            Expression<Func<InvocationMessage, bool>> allQuery = (im) => true;

            var messages = allQuery
                .StorageQuery()
                .Where(msg => msg.lastModified >= startTime)
                .Where(msg => msg.lastModified <= endTime);
            return onRun(messages);
        }

        [HttpAction("Invoke")]
        [RequiredClaim(Microsoft.IdentityModel.Claims.ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static async Task<HttpResponseMessage> InvokeAsync(
                [UpdateId]IRefs<InvocationMessage> invocationMessageRefs,
                [HeaderLog]EastFive.Analytics.ILogger analyticsLog,
                InvokeApplicationDirect invokeApplication,
                MultipartResponseAsync onRun)
        {
            var messages = await invocationMessageRefs.refs
                .Select(invocationMessageRef => InvokeAsync(invocationMessageRef, invokeApplication, logging: analyticsLog))
                .AsyncEnumerable()
                .ToArrayAsync();
            return await onRun(messages);
        }

        [HttpAction("Enqueue")]
        [RequiredClaim(Microsoft.IdentityModel.Claims.ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static async Task<HttpResponseMessage> EnqueueAsync(
                [UpdateId]IRef<InvocationMessage> invocationMessageRef,
                AzureApplication application,
            NoContentResponse onNoContent)
        {
            await SendToQueueAsync(invocationMessageRef, application);
            return onNoContent();           
        }

        #endregion

        public static IEnumerableAsync<HttpResponseMessage> InvokeAsync(
                byte [] invocationMessageIdsBytes,
            IInvokeApplication invokeApplication,
            EastFive.Analytics.ILogger analyticsLog = default)
        {
            return invocationMessageIdsBytes
                .Split(index => 16)
                .Select(
                    invocationMessageIdBytes =>
                    {
                        var idBytes = invocationMessageIdBytes.ToArray();
                        var invocationMessageId = new Guid(idBytes);
                        var invocationMessageRef = invocationMessageId.AsRef<InvocationMessage>();
                        return InvokeAsync(invocationMessageRef, invokeApplication, analyticsLog);
                    })
                .Parallel();
        }

        internal static async Task<HttpResponseMessage> CreateAsync(
            HttpRequestMessage httpRequest)
        {
            var invocationMessage = await httpRequest.InvocationMessageAsync();
            return await invocationMessage.StorageCreateAsync(
                (created) =>
                {
                    var invocationSerialized = JsonConvert.SerializeObject(invocationMessage,
                        new EastFive.Api.Serialization.Converter());
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                    {
                        RequestMessage = httpRequest,
                        ReasonPhrase = "Send to background-task message queue",
                        Content = new StringContent(
                            invocationSerialized, Encoding.UTF8,
                            "x-application/eastfive-invocationmessage"),
                    };
                    return response;
                },
                () => throw new Exception());
        }

        public Task SendToQueueAsync(AzureApplication application)
        {
            return InvocationMessage.SendToQueueAsync(this.invocationRef, application);
        }

        public static Task SendToQueueAsync(IRef<InvocationMessage> invocationMessageRef,
            AzureApplication azureApplication)
        {
            var byteContent = invocationMessageRef.id.ToByteArray();
            return EastFive.Web.Configuration.Settings.GetString(
                AppSettings.FunctionProcessorServiceBusTriggerName,
                (serviceBusTriggerName) =>
                {
                    return azureApplication.SendServiceBusMessageAsync(serviceBusTriggerName, byteContent);
                },
                (why) => throw new Exception(why));
        }

        public static Task SendToQueueAsync(IRefs<InvocationMessage> invocationMessageRefs,
            AzureApplication azureApplication)
        {
            var byteContents = invocationMessageRefs.ids.Select(id => id.ToByteArray()).ToArray();
            return EastFive.Web.Configuration.Settings.GetString(
                AppSettings.FunctionProcessorServiceBusTriggerName,
                (serviceBusTriggerName) =>
                {
                    return azureApplication.SendServiceBusMessageAsync(serviceBusTriggerName, byteContents);
                },
                (why) => throw new Exception(why));
        }

        public static Task<HttpResponseMessage> InvokeAsync(IRef<InvocationMessage> invocationMessageRef,
            IInvokeApplication invokeApplication,
            ILogger logging = default)
        {
            var scopedLogger = logging.CreateScope(invocationMessageRef.id.ToString());
            scopedLogger.Trace($"Loading message from storage.");
            return invocationMessageRef.StorageUpdateAsync(
                async (invocationMessage, saveAsync) =>
                {
                    scopedLogger.Trace($"{invocationMessage.method.ToUpper()} {invocationMessage.requestUri}");
                    var httpRequest = new HttpRequestMessage(
                        new HttpMethod(invocationMessage.method),
                        invocationMessage.requestUri);
                    var config = new HttpConfiguration();
                    httpRequest.SetConfiguration(config);

                    logging.Trace($"Message origin:[{invocationMessage.referrer}].");
                    if(invocationMessage.headers.ContainsKey(InvocationMessageSourceHeaderKey))
                    {
                        var sourceInvocationMessageIdStr = invocationMessage.headers[InvocationMessageSourceHeaderKey];
                        if (Guid.TryParse(sourceInvocationMessageIdStr, out Guid sourceInvocationMessageId))
                            logging.Trace($"Function origin:[{sourceInvocationMessageId}].");
                    }
                    foreach (var headerKVP in invocationMessage.headers
                        .Where(headerKvp => headerKvp.Key != InvocationMessageSourceHeaderKey))
                        httpRequest.Headers.Add(headerKVP.Key, headerKVP.Value);
                    httpRequest.Headers.Add(InvocationMessageSourceHeaderKey, 
                        invocationMessageRef.id.ToString());

                    if (!invocationMessage.content.IsDefaultOrNull())
                    {
                        var contentJson = System.Text.Encoding.UTF8.GetString(invocationMessage.content);
                        scopedLogger.Trace(contentJson);
                        httpRequest.Content = new ByteArrayContent(invocationMessage.content);
                        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    }

                    invocationMessage.lastExecuted = DateTime.UtcNow;
                    logging.Trace($"{httpRequest.Method.Method}'ing to `{httpRequest.RequestUri.OriginalString}`.");
                    var result = await invokeApplication.SendAsync(httpRequest);
                    await saveAsync(invocationMessage);
                    return result;
                },
                ResourceNotFoundException.StorageGetAsync<HttpResponseMessage>);
        }
    }
}

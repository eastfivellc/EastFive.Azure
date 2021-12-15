using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using System.Security.Claims;
using System.Linq.Expressions;
using System.Web.Http;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Linq.Async;
using EastFive.Linq;
using EastFive.Analytics;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.StorageTables;

namespace EastFive.Azure.Functions
{
    [FunctionViewController(
        Route = "InvocationMessage",
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
        //[DateTimeLookup(
        //    Partition = TimeSpanUnits.days,
        //    Row = TimeSpanUnits.hours)]
        [JsonProperty]
        public DateTimeOffset lastModified;

        [JsonProperty]
        [StorageOverflow]
        //[UrlMD5Lookup(Characters = 3, Components = UriComponents.PathAndQuery, ShouldHashRowKey = true)]
        public Uri requestUri;

        [JsonProperty]
        [Storage]
        public string method;

        [JsonProperty]
        [Storage]
        //[UrlMD5Lookup(Characters = 3, Components = UriComponents.AbsoluteUri)]
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
        //[DateTimeLookup(
        //    Partition = TimeSpanUnits.days,
        //    Row = TimeSpanUnits.hours)]
        public DateTime? lastExecuted;

        public const string ExecutionHistoryPropertyName = "execution_history";
        [JsonProperty(PropertyName = ExecutionHistoryPropertyName)]
        [ApiProperty(PropertyName = ExecutionHistoryPropertyName)]
        [Storage]
        public KeyValuePair<DateTime, int>[] executionHistory;

        public const string ExecutionLimitPropertyName = "execution_limit";
        [JsonProperty(PropertyName = ExecutionLimitPropertyName)]
        [ApiProperty(PropertyName = ExecutionLimitPropertyName)]
        [Storage]
        public long? executionLimit;

        #endregion

        #region Http Methods

        #region GET

        [Api.HttpGet]
        [SuperAdminClaim]
        public static Task<IHttpResponse> GetByIdAsync(
            [QueryId]IRef<InvocationMessage> invocationMessageRef,
            ContentTypeResponse<InvocationMessage> onFound,
            NotFoundResponse onNotFound)
        {
            return invocationMessageRef.StorageGetAsync(
                (InvocationMessage ent) => onFound(ent),
                () => onNotFound());
        }

        [Api.HttpGet]
        [SuperAdminClaim]
        public static IHttpResponse ListAsync(
            [QueryParameter(Name = "start_time")]DateTime startTime,
            [QueryParameter(Name = "end_time")]DateTime endTime,
            [HeaderLog]EastFive.Analytics.ILogger analyticsLog,
            MultipartAsyncResponse<InvocationMessage> onRun)
        {
            Expression<Func<InvocationMessage, bool>> allQuery = (im) => true;

            var messages = allQuery
                .StorageQuery()
                .Where(msg => msg.lastModified >= startTime)
                .Where(msg => msg.lastModified <= endTime);
            return onRun(messages);
        }

        [Api.HttpGet]
        [SuperAdminClaim]
        public static IHttpResponse ListByRequestUrlAsync(
            [QueryParameter(Name = "request_uri")]Uri requestUri,
            MultipartAsyncResponse<InvocationMessage> onRun)
        {
            var messages = requestUri.StorageGetBy(
                (InvocationMessage ent) => ent.requestUri);
            return onRun(messages);
        }

        #endregion

        #region Actions

        [HttpAction("Invoke")]
        [SuperAdminClaim]
        public static IHttpResponse InvokeAsync(
                [UpdateId]IRefs<InvocationMessage> invocationMessageRefs,
                [HeaderLog]ILogger analyticsLog,
                InvokeApplicationDirect invokeApplication,
                CancellationToken cancellationToken,
                MultipartAsyncResponse<IHttpResponse> onRun)
        {
            var messages = invocationMessageRefs.refs
                .Select(
                    invocationMessageRef => InvokeAsync(invocationMessageRef, invokeApplication,
                        logging:new EventLogger(analyticsLog),
                        cancellationToken: cancellationToken))
                .AsyncEnumerable();
            return onRun(messages);
        }

        [HttpAction("Enqueue")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> EnqueueAsync(
                [UpdateId]IRef<InvocationMessage> invocationMessageRef,
                IAzureApplication application,
            NoContentResponse onNoContent)
        {
            await SendToQueueAsync(invocationMessageRef, application);
            return onNoContent();           
        }

        #endregion

        #region Update

        [Api.HttpPatch]
        [SuperAdminClaim]
        public static Task<IHttpResponse> UpdateByIdAsync(
                [UpdateId]IRef<InvocationMessage> invocationMessageRef,
                [Property(Name = ExecutionLimitPropertyName)]int executionLimit,
                // [Resource]InvocationMessage invocationMessage,
            ContentTypeResponse<InvocationMessage> onFound,
            NotFoundResponse onNotFound)
        {
            return invocationMessageRef.StorageUpdateAsync(
                async (invocationMessage, saveAsync) =>
                {
                    invocationMessage.executionLimit = executionLimit;
                    await saveAsync(invocationMessage);
                    return onFound(invocationMessage);
                },
                () => onNotFound());
        }

        #endregion

        #endregion

        public static IEnumerableAsync<IHttpResponse> InvokeAsync(
                byte [] invocationMessageIdsBytes,
            IInvokeApplication invokeApplication,
            EastFive.Analytics.ILoggerWithEvents analyticsLog = default,
            CancellationToken cancellationToken = default)
        {
            return invocationMessageIdsBytes
                .Split(index => 16)
                .Select(
                    invocationMessageIdBytes =>
                    {
                        var idBytes = invocationMessageIdBytes.ToArray();
                        var invocationMessageId = new Guid(idBytes);
                        var invocationMessageRef = invocationMessageId.AsRef<InvocationMessage>();
                        return InvokeAsync(invocationMessageRef, invokeApplication, analyticsLog, cancellationToken);
                    })
                .Parallel();
        }

        internal static async Task<IHttpResponse> CreateAsync(
            IHttpRequest httpRequest, int executionLimit = 1)
        {
            var invocationMessage = await httpRequest.InvocationMessageAsync(executionLimit: executionLimit);
            return await invocationMessage.StorageCreateAsync(
                (created) =>
                {
                    var invocationSerialized = JsonConvert.SerializeObject(invocationMessage,
                        new Api.Serialization.Converter(httpRequest));
                    var response = new StringHttpResponse(httpRequest, System.Net.HttpStatusCode.Accepted,
                        default, "x-application/eastfive-invocationmessage", default, 
                        invocationSerialized, new UTF8Encoding(false));
                    return response;
                },
                () => throw new Exception());
        }

        public Task SendToQueueAsync(IAzureApplication application)
        {
            return InvocationMessage.SendToQueueAsync(this.invocationRef, application);
        }

        public static Task SendToQueueAsync(IRef<InvocationMessage> invocationMessageRef,
            IAzureApplication azureApplication)
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
            IAzureApplication azureApplication)
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

        public static async Task<IHttpResponse> InvokeAsync(IRef<InvocationMessage> invocationMessageRef,
            IInvokeApplication invokeApplication,
            ILoggerWithEvents logging = default,
            CancellationToken cancellationToken = default)
        {
            var scopedLogger = logging.CreateScope(invocationMessageRef.id.ToString());
            var executionResultRef = Ref<ExecutionResult>.SecureRef();

            var traceId = Security.SecureGuid.Generate();
            using (var messageWriter = new MessageWriter(logging, traceId, cancellationToken))
            {
                scopedLogger.Trace($"Loading message from storage.");
                return await await invocationMessageRef.StorageGetAsync(
                    async (invocationMessage) =>
                    {
                        var lastExecuted = DateTime.UtcNow;

                        scopedLogger.Trace($"{invocationMessage.method.ToUpper()} {invocationMessage.requestUri}");
                        var httpRequest = new InvocationHttpRequest(invocationMessage.requestUri, cancellationToken)
                        {
                            Method = new HttpMethod(invocationMessage.method),
                        };

                        if (ShortCircuit(out DateTime? latestExecutionSS))
                        {
                            logging.Trace($"The message {invocationMessage.id} was already executed on {latestExecutionSS}.");
                            var responseShortCurcuit = new HttpResponse(httpRequest, System.Net.HttpStatusCode.ExpectationFailed);
                            return await invocationMessageRef.StorageUpdateAsync(
                                async (invocationMessageShortCircuit, saveAsync) =>
                                {
                                    invocationMessageShortCircuit.lastExecuted = lastExecuted;
                                    invocationMessageShortCircuit.executionHistory = invocationMessageShortCircuit
                                        .executionHistory
                                        .Append(lastExecuted.PairWithValue((int)responseShortCurcuit.StatusCode))
                                        .ToArray();
                                    await saveAsync(invocationMessageShortCircuit);
                                    return responseShortCurcuit;
                                },
                                () => responseShortCurcuit);
                        }

                        logging.Trace($"Message origin:[{invocationMessage.referrer}].");
                        if (invocationMessage.headers.ContainsKey(InvocationMessageSourceHeaderKey))
                        {
                            var sourceInvocationMessageIdStr = invocationMessage.headers[InvocationMessageSourceHeaderKey];
                            if (Guid.TryParse(sourceInvocationMessageIdStr, out Guid sourceInvocationMessageId))
                                logging.Trace($"Function origin:[{sourceInvocationMessageId}].");
                        }
                        foreach (var headerKVP in invocationMessage.headers
                            .Where(headerKvp => headerKvp.Key != InvocationMessageSourceHeaderKey))
                            httpRequest.Headers.Add(headerKVP.Key, headerKVP.Value.AsArray());
                        httpRequest.Headers.Add(InvocationMessageSourceHeaderKey,
                            invocationMessageRef.id.ToString().AsArray());

                        if (!invocationMessage.content.IsDefaultOrNull())
                        {
                            var contentJson = System.Text.Encoding.UTF8.GetString(invocationMessage.content);
                            scopedLogger.Trace(contentJson);
                            httpRequest.Content = invocationMessage.content;
                            httpRequest.SetMediaType("application/json");
                        }

                        var executionResult = new ExecutionResult
                        {
                            executionResultRef = executionResultRef,
                            invocationMessage = invocationMessageRef,
                            started = lastExecuted,
                            eventMessageId = traceId,
                        };
                        return await await executionResult.StorageCreateAsync(
                            async (discard) =>
                            {
                                logging.Trace($"{httpRequest.Method.Method}'ing to `{httpRequest.RequestUri.OriginalString}`.");

                                var result = await invokeApplication.SendAsync(httpRequest);
                                bool saved = await invocationMessageRef.StorageUpdateAsync(
                                    async (invocationMessageToSave, saveInvocationMessage) =>
                                    {
                                        invocationMessageToSave.lastExecuted = lastExecuted;
                                        invocationMessageToSave.executionHistory = invocationMessageToSave.executionHistory
                                            .Select(
                                                exHi =>
                                                {
                                                    if ((exHi.Key - lastExecuted).TotalSeconds < 1.0)
                                                    {
                                                        return lastExecuted.PairWithValue((int)result.StatusCode);
                                                    }
                                                    return exHi;
                                                })
                                            .ToArray();
                                        await saveInvocationMessage(invocationMessageToSave);
                                        return true;
                                    });
                                return await await GetContents(
                                    (contentBlobId, whenCompleted) =>
                                    {
                                        return executionResult.executionResultRef.StorageUpdateAsync(
                                            async (executionResultToUpdate, saveAsync) =>
                                            {
                                                executionResultToUpdate.ended = whenCompleted;
                                                executionResultToUpdate.contentBlobId = contentBlobId;
                                                await saveAsync(executionResultToUpdate);
                                                return result;
                                            },
                                            () => result);
                                    });

                                async Task<TResult> GetContents<TResult>(
                                    Func<Guid?, DateTime, TResult> onContents)
                                {
                                    using (var stream = new MemoryStream())
                                    {
                                        await result.WriteResponseAsync(stream);
                                        var contents = stream.ToArray();

                                        if (!contents.Any())
                                            return onContents(default, DateTime.UtcNow);

                                        var whenCompleted = DateTime.UtcNow;
                                        if(result.TryGetContentType(out string contentType))
                                            return await contents.BlobCreateAsync("innvocationmessageexecutionresultcontents",
                                                contentBlobId => onContents(contentBlobId, whenCompleted),
                                                contentType: contentType);

                                        return await contents.BlobCreateAsync("innvocationmessageexecutionresultcontents",
                                                contentBlobId => onContents(contentBlobId, whenCompleted));
                                    }
                                }
                            });


                        bool ShortCircuit(out DateTime? latestExecution)
                        {
                            latestExecution = invocationMessage.executionHistory
                                .NullToEmpty()
                                .Aggregate(default(DateTime?),
                                    (dt, exec) =>
                                    {
                                        if (!dt.HasValue)
                                            return exec.Key;
                                        return dt.Value > exec.Key ? dt.Value : exec.Key;
                                    });

                            var hasExecutionHistory = latestExecution.HasValue;
                            if (!hasExecutionHistory)
                                return false;

                            if (!invocationMessage.executionLimit.HasValue)
                                return true;

                            var executionLimit = invocationMessage.executionLimit.Value;
                            if (executionLimit <= invocationMessage.executionHistory.Length)
                                return true;

                            return false;
                        }
                    },
                    ResourceNotFoundException.StorageGetAsync<Task<IHttpResponse>>);
            }
        }
    }
}

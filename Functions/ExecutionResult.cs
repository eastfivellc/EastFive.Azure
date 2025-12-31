using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Functions
{
    [FunctionViewController(
        Route = "ExecutionResult",
        ContentType = "x-application/eastfive.azure.execution-result",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    public struct ExecutionResult : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => this.executionResultRef.id;

        public const string IdPropertyName = "id";
        [JsonProperty(PropertyName = IdPropertyName)]
        [ApiProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<ExecutionResult> executionResultRef;

        public const string InvocationMessagePropertyName = "invocation_message";
        [JsonProperty(PropertyName = InvocationMessagePropertyName)]
        [ApiProperty(PropertyName = InvocationMessagePropertyName)]
        [Storage]
        [IdPrefixLookup(Characters = 4)]
        public IRef<InvocationMessage> invocationMessage;

        public const string LastModifiedPropertyName = "last_modified";
        [LastModified]
        [JsonProperty]
        public DateTimeOffset lastModified;

        public const string StartedPropertyName = "started";
        [JsonProperty(PropertyName = StartedPropertyName)]
        [ApiProperty(PropertyName = StartedPropertyName)]
        [Storage]
        //[DateTimeLookup(
        //    Partition = TimeSpanUnits.days,
        //    Row = TimeSpanUnits.hours)]
        public DateTime started;

        public const string EndedPropertyName = "ended";
        [JsonProperty(PropertyName = EndedPropertyName)]
        [ApiProperty(PropertyName = EndedPropertyName)]
        [Storage]
        public DateTime? ended;

        [JsonProperty]
        [Storage]
        public int statusCode;

        [JsonProperty]
        [Storage]
        public string reasonPhrase;

        [Storage]
        public Guid eventMessageId;

        [Storage]
        public Guid? contentBlobId;

        #endregion

        [Api.HttpGet]
        [SuperAdminClaim]
        public static IHttpResponse GetByInvocationMessageAsync(
            [QueryParameter(Name = InvocationMessagePropertyName)]IRef<InvocationMessage> message,
            MultipartAsyncResponse<ExecutionResult> onResults)
        {
            var messages = message.StorageGetBy(
                (ExecutionResult ent) => ent.invocationMessage);
            return onResults(messages);
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
    }
}

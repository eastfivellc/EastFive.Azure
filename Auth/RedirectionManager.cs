using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BlackBarLabs.Api;
using BlackBarLabs.Extensions;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Controllers;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Serialization;
using EastFive.Web.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [FunctionViewController(
        Route = "RedirectionManager",
        Resource = typeof(RedirectionManager),
        ContentType = "x-application/auth-redirection-manager",
        ContentTypeVersion = "0.1")]
    public struct RedirectionManager : IReferenceable
    {
        [JsonIgnore]
        public Guid id => redirectionManagerRef.id;

        public const string RedirectionManagerId = "id";
        [ApiProperty(PropertyName = RedirectionManagerId)]
        [JsonProperty(PropertyName = RedirectionManagerId)]
        [RowKey]
        [StandardParititionKey]
        public IRef<RedirectionManager> redirectionManagerRef;

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        [Storage]
        public IRefOptional<Authorization> authorization { get; set; }

        public const string Message = "message";
        [ApiProperty(PropertyName = Message)]
        [JsonProperty(PropertyName = Message)]
        [Storage]
        public string message { get; set; }

        public const string When = "when";
        [ApiProperty(PropertyName = When)]
        [JsonProperty(PropertyName = When)]
        [Storage]
        public DateTime when { get; set; }

        public const string Redirection = "redirection";
        [ApiProperty(PropertyName = Redirection)]
        [JsonProperty(PropertyName = Redirection)]
        [Storage]
        public Uri redirection { get; set; }

        [Api.HttpGet]
        public async static Task<IHttpResponse> GetAllSecureAsync(
                [QueryParameter(Name = "ApiKeySecurity")]ApiSecurity apiSecurity,
                [QueryParameter(Name = "method")]IRef<Method> methodRef,
                [OptionalQueryParameter(Name = "successOnly")]bool successOnly,
                AzureApplication application,
                IHttpRequest request,
            ContentTypeResponse<RedirectionManager[]> onContent,
            UnauthorizedResponse onUnauthorized,
            ConfigurationFailureResponse onConfigFailure,
            BadRequestResponse onBadRequest)
        {
            // this is faster than the version commented out below
            var methodMaybe = await Method.ById(methodRef, application, (m) => m, () => default(Method?));
            if (methodMaybe.IsDefault())
                return onBadRequest().AddReason("Method no longer supported");
            var method = methodMaybe.Value;

            var twoMonthsAgo = DateTime.UtcNow.AddMonths(-2);
            var query = new TableQuery<GenericTableEntity>().Where(
            TableQuery.CombineFilters(
                TableQuery.GenerateFilterConditionForGuid("method", QueryComparisons.Equal, method.id),
                TableOperators.And,
                TableQuery.CombineFilters(
                    TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.GreaterThanOrEqual, twoMonthsAgo),
                    TableOperators.And,
                    TableQuery.GenerateFilterConditionForBool("authorized", QueryComparisons.Equal, true)
                    )
                )
            );
            var table = EastFive.Azure.Persistence.AppSettings.Storage.ConfigurationString(
                (conn) =>
                {
                    var account = CloudStorageAccount.Parse(conn);
                    var client = account.CreateCloudTableClient();
                    return client.GetTableReference("authorization");
                },
                (why) => throw new Exception(why));

            var segment = default(TableQuerySegment<GenericTableEntity>);
            var token = default(TableContinuationToken);
            var redirections = new RedirectionManager[] { };
            do
            {
                segment = await table.ExecuteQuerySegmentedAsync(query, token);
                token = segment.ContinuationToken;
                redirections = await segment.Results
                    .Aggregate(
                        redirections.AsTask(),
                        async (aggr, entity) =>
                        {
                            var res = await aggr;
                            var id = Guid.Parse(entity.RowKey);
                            var when = entity.Timestamp.DateTime;
                            var props = entity.WriteEntity(default);
                            var paramKeys = props["parameters__keys"].BinaryValue.ToStringsFromUTF8ByteArray();
                            var paramValues = props["parameters__values"].BinaryValue.ToStringsFromUTF8ByteArray();
                            var parameters = Enumerable.Range(0, paramKeys.Length).ToDictionary(i => paramKeys[i].Substring(1), i => paramValues[i].Substring(1));

                            RedirectionManager? Failure(string why)
                            {
                                if (successOnly)
                                    return default(RedirectionManager?);

                                return new RedirectionManager
                                {
                                    authorization = id.AsRef<Authorization>().Optional(),
                                    message = why,
                                    when = when,
                                };
                            }

                            var item = await method.ParseTokenAsync(parameters, application,
                                (externalId, loginProvider) =>
                                {
                                    return new RedirectionManager
                                    {
                                        when = when,
                                        message = $"Ready:{externalId}",
                                        authorization = id.AsRef<Authorization>().Optional(),
                                        redirection = new Uri(
                                            request.RequestUri,
                                            $"/api/RedirectionManager?ApiKeySecurity={apiSecurity.key}&authorization={id}"),
                                    };
                                },
                                (why) => Failure(why));
                            if (item.IsDefault())
                                return res;

                            return res.Append(item.Value).ToArray();
                        });
            } while (token != null);
            return onContent(redirections.OrderByDescending(x => x.when).ToArray());

            //Expression<Func<Authorization, bool>> allQuery =
            //    (authorization) => authorization.authorized == true;
            //var results = await allQuery
            //    .StorageQuery()
            //    .Where(authorization => !authorization.Method.IsDefaultOrNull())
            //    .Where(authorization => authorization.Method.id == methodRef.id)
            //    .Where(authorization => authorization.lastModified > twoMonthsAgo)
            //    .Select<Authorization, Task<RedirectionManager?>>(
            //        async authorization =>
            //        {
            //            RedirectionManager? Failure(string why)
            //            {
            //                if (successOnly)
            //                    return default(RedirectionManager?);

            //                return new RedirectionManager
            //                {
            //                    authorization = authorization.authorizationRef.Optional(),
            //                    message = why,
            //                    when = authorization.lastModified
            //                };
            //            }

            //            return await method.ParseTokenAsync(authorization.parameters, application,
            //                (externalId, loginProvider) =>
            //                {
            //                    return new RedirectionManager
            //                    {
            //                        when = authorization.lastModified,
            //                        message = $"Ready:{externalId}",
            //                        authorization = authorization.authorizationRef.Optional(),
            //                        redirection = new Uri(
            //                            request.RequestUri,
            //                            $"/api/RedirectionManager?ApiKeySecurity={apiSecurity.key}&authorization={authorization.id}"),
            //                    };
            //                },
            //                (why) => Failure(why));
            //        })
            //    .Throttle(desiredRunCount: 4)
            //    .SelectWhereHasValue()
            //    .OrderByDescendingAsync(item => item.when);
            //return onContent(results.ToArray());
        }

        [Api.HttpGet]
        public static Task<IHttpResponse> GetRedirection(
                [QueryParameter(Name = "ApiKeySecurity")]ApiSecurity apiSecurity,
                [QueryParameter(Name = "authorization")]IRef<Authorization> authRef,
                AzureApplication application,
                HttpRequestMessage request,
            RedirectResponse onRedirection,
            GeneralFailureResponse onFailure,
            UnauthorizedResponse onUnauthorized,
            ConfigurationFailureResponse onConfigFailure)
        {
            return authRef.StorageUpdateAsync(
                async (authorization, saveAsync) =>
                {
                    var url = await await Method.ById(authorization.Method, application,
                        async method =>
                        {
                            return await await method.ParseTokenAsync(authorization.parameters, application,
                                async (externalId, loginProvider) =>
                                {
                                    var tag = "OpioidTool";
                                    return await EastFive.Web.Configuration.Settings.GetString($"AffirmHealth.PDMS.PingRedirect.{tag}.PingAuthName",
                                        async pingAuthName =>
                                        {
                                            return await EastFive.Web.Configuration.Settings.GetGuid($"AffirmHealth.PDMS.PingRedirect.{tag}.PingReportSetId",
                                                reportSetId =>
                                                {
                                                    var requestParams = authorization.parameters
                                                        .AppendIf("PingAuthName".PairWithValue(pingAuthName), !authorization.parameters.ContainsKey("PingAuthName"))
                                                        .AppendIf("ReportSetId".PairWithValue(reportSetId.ToString()), !authorization.parameters.ContainsKey("ReportSetId"))
                                                        .ToDictionary();

                                                    return Auth.Redirection.ProcessAsync(authorization, 
                                                            updatedAuth => 1.AsTask(),
                                                            method, externalId, requestParams,
                                                            request.RequestUri, application, loginProvider,
                                                        (uri) =>
                                                        {
                                                            return uri;
                                                        },
                                                        (why) => default(Uri),
                                                        application.Telemetry);
                                                },
                                                why =>
                                                {
                                                    return default(Uri).AsTask();
                                                });
                                        },
                                        why =>
                                        {
                                            return default(Uri).AsTask();
                                        });
                                },
                                (why) => default(Uri).AsTask());
                        },
                        () => default(Uri).AsTask());
                    if (url.IsDefaultOrNull())
                        return onFailure("Failed to determine correct redirect URL");

                    authorization.expired = false;
                    await saveAsync(authorization);
                    return onRedirection(url);
                });
        }

        [Api.HttpGet]
        public static async Task<IHttpResponse> GetAllSecureAsync(
                [QueryParameter(Name = "ApiKeySecurity")]ApiSecurity apiSecurity,
                [QueryParameter(Name = "authorization")]IRef<Authorization> authorizationRef,
                AzureApplication application,
                IHttpRequest request,
            MultipartResponseAsync<Authorization> onContent,
            RedirectResponse onSuccess,
            NotFoundResponse onNotFound,
            ForbiddenResponse onFailure)
        {
            return await await authorizationRef.StorageGetAsync(
                async authorization =>
                {
                    return await await Method.ById(authorization.Method, application,
                        async method =>
                        {
                            return await await method.ParseTokenAsync(authorization.parameters, application,
                                (externalId, loginProvider) =>
                                {
                                    return Auth.Redirection.ProcessAsync(authorization,
                                            async updatedAuth =>
                                            {

                                            }, method, externalId, authorization.parameters,
                                            request.RequestUri, application, loginProvider,
                                        (uri) => onSuccess(uri),
                                        (why) => onFailure().AddReason(why),
                                        application.Telemetry);
                                },
                                why => onFailure().AddReason(why).AsTask());
                        },
                        () => onFailure().AddReason("Method no longer supported").AsTask());
                },
                () => onNotFound().AsTask());
        }
    }
}

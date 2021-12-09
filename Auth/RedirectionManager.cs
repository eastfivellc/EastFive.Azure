using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Api.Azure;
using EastFive.Azure.Meta;
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
using Microsoft.Azure.Cosmos.Table;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [FunctionViewController(
        Route = "RedirectionManager",
        ContentType = "x-application/auth-redirection-manager",
        ContentTypeVersion = "0.1")]
    public struct RedirectionManager : IReferenceable
    {
        #region Properties

        #region Base

        [JsonIgnore]
        public Guid id => redirectionManagerRef.id;

        public const string RedirectionManagerId = "id";
        [ApiProperty(PropertyName = RedirectionManagerId)]
        [JsonProperty(PropertyName = RedirectionManagerId)]
        [RowKey]
        [StandardParititionKey]
        public IRef<RedirectionManager> redirectionManagerRef;

        #endregion

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

        #endregion

        #region HTTP Methods

        #region GET

        [Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.HijackLoginFlow.FlowName,
            StepName = "Find Authorization To Hijack",
            Step = 2.0)]
        [Api.HttpGet]
        [ApiKeyClaim(System.Security.Claims.ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public async static Task<IHttpResponse> GetAllSecureAsync(

                [Api.Meta.Flows.WorkflowParameter(Value = "{{AuthenticationMethod}}")]
                [QueryParameter(Name = "method")]
                IRef<Method> methodRef,

                [OptionalQueryParameter(Name = "successOnly")]bool successOnly,

                [Api.Meta.Flows.WorkflowParameter(Description = "ACP,Dash,TCM,HUDDLE", Value = "")]
                [OptionalQueryParameter(Name = "search")]
                string search,

                [OptionalQueryParameter(Name = "months")]int? monthsMaybe,
                AzureApplication application,
                EastFive.Api.Security security,
                IHttpRequest request,

            [Api.Meta.Flows.WorkflowVariable(AuthorizationPropertyName, "authorization{0}")]
            ContentTypeResponse<RedirectionManager[]> onContent,
            UnauthorizedResponse onUnauthorized,
            ConfigurationFailureResponse onConfigFailure,
            BadRequestResponse onBadRequest)
        {
            // this query is faster than the version commented out below
            var methodMaybe = await Method.ById(methodRef, application, (m) => m, () => default(Method?));
            if (methodMaybe.IsDefault())
                return onBadRequest().AddReason("Method no longer supported");
            var method = methodMaybe.Value;

            var months = monthsMaybe.HasValue ? Math.Abs(monthsMaybe.Value) : 1;
            var since = DateTime.UtcNow.Date.AddMonths(-months);
            var query = new TableQuery<GenericTableEntity>().Where(
            TableQuery.CombineFilters(
                TableQuery.GenerateFilterConditionForGuid("method", QueryComparisons.Equal, method.id),
                TableOperators.And,
                TableQuery.CombineFilters(
                    TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.GreaterThanOrEqual, since),
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
                var result = await segment.Results
                    .Select(
                        async (entity) =>
                        {
                            var id = Guid.Parse(entity.RowKey);
                            var when = entity.Timestamp.DateTime;
                            var props = entity.WriteEntity(default);
                            var paramKeys = props["parameters__keys"].BinaryValue.ToStringsFromUTF8ByteArray();
                            var paramValues = props["parameters__values"].BinaryValue.ToStringsFromUTF8ByteArray();
                            var parameters = Enumerable.Range(0, paramKeys.Length).ToDictionary(i => paramKeys[i].Substring(1), i => paramValues[i].Substring(1));

                            if (search.HasBlackSpace())
                            {
                                var searchSet = search.Contains(',') ?
                                    search.Split(',')
                                    :
                                    search.AsArray();
                                var match = parameters.Values
                                    .SelectWhereNotNull()
                                    .Any(val => searchSet.Any(searchItem => val.IndexOf(searchItem, StringComparison.OrdinalIgnoreCase) != -1));
                                if (!match)
                                {
                                    if(parameters.Count() < 10)
                                        return default(RedirectionManager?);
                                    return default(RedirectionManager?);
                                }
                            }

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

                            return await method.ParseTokenAsync(parameters, application,
                                (externalId, loginProvider) =>
                                {
                                    return new RedirectionManager
                                    {
                                        when = when,
                                        message = $"Ready:{externalId}",
                                        authorization = id.AsRef<Authorization>().Optional(),
                                        redirection = new Uri(
                                            request.RequestUri,
                                            $"/api/RedirectionManager?authorization={id}"),
                                    };
                                },
                                (why) => Failure(why));
                        })
                    .AsyncEnumerable(readAhead: 10)
                    .SelectWhereHasValue()
                    .ToArrayAsync();
                redirections = redirections.Concat(result).ToArray();

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


        [Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.HijackLoginFlow.FlowName,
            StepName = "Launch UI",
            Step = 3.0)]
        [Api.HttpGet]
        [ApiKeyClaim(System.Security.Claims.ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static Task<IHttpResponse> GetRedirection(
                [Api.Meta.Flows.WorkflowParameter(Value = "{{Authorization}}")]
                [QueryParameter(Name = "authorization")]IRef<Authorization> authRef,
                AzureApplication application,
                IInvokeApplication endpoints,
                EastFive.Api.Security security,
                IHttpRequest request,
            RedirectResponse onRedirection,
            NotFoundResponse onNotFound,
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
                                                            application, request, endpoints, loginProvider, request.RequestUri,
                                                        (uri, accountIdMaybe, modifier) =>
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
                },
                () => onNotFound());
        }

        [Api.HttpGet]
        [RequiredClaim(System.Security.Claims.ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static async Task<IHttpResponse> GetAllSecureAsync(
                [QueryParameter(Name = "authorization")]IRef<Authorization> authorizationRef,
                AzureApplication application,
                IInvokeApplication endpoints,
                IHttpRequest request,
            MultipartAsyncResponse<Authorization> onContent,
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
                                            application, request, endpoints, loginProvider, request.RequestUri,
                                        (uri, accountIdMaybe, modifier) => onSuccess(uri),
                                        (why) => onFailure().AddReason(why),
                                        application.Telemetry);
                                },
                                why => onFailure().AddReason(why).AsTask());
                        },
                        () => onFailure().AddReason("Method no longer supported").AsTask());
                },
                () => onNotFound().AsTask());
        }

        #endregion

        #endregion
    }
}

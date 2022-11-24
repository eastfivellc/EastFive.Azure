using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Meta.Flows;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
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
        [JsonIgnore]
        public IRef<RedirectionManager> redirectionManagerRef;

        #endregion

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        public IRef<Authorization> authorizationRef { get; set; }

        [JsonProperty]
        public string info { get; set; }

        [JsonProperty]
        public int fields { get; set; }

        [JsonProperty]
        public DateTime when { get; set; }

        [JsonProperty(PropertyName = "COPY_link")]
        public Uri link { get; set; }

        #endregion

        #region HTTP Methods

        #region GET

        [Api.HttpGet]
        [Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.HijackLoginFlow.FlowName,
            StepName = Workflows.HijackLoginFlow.Steps.ListLogins,
            Step = Workflows.HijackLoginFlow.Ordinals.ListLogins)]
        [PIIAdminClaim]
        public async static Task<IHttpResponse> QueryAsync(
                [WorkflowParameterFromVariable(
                    Value = Workflows.HijackLoginFlow.Variables.Method.Get.Value,
                    Description = Workflows.HijackLoginFlow.Variables.Method.Get.Description)]
                [QueryParameter(Name = "method")] IRef<Method> methodRef,

                [OptionalQueryParameter(Name = "successOnly")] bool? successOnly,

                [WorkflowParameter(
                    Value = Workflows.HijackLoginFlow.Variables.Search.Set.Value,
                    Description = Workflows.HijackLoginFlow.Variables.Search.Set.Description)]
                [OptionalQueryParameter(Name = "search")] string search,

                [WorkflowParameter(
                    Value = Workflows.HijackLoginFlow.Variables.History.Set.Value,
                    Description = Workflows.HijackLoginFlow.Variables.History.Set.Description)]
                [OptionalQueryParameter(Name = "days")] int? daysMaybe,

                AzureApplication application,
                EastFive.Api.Security security,
                IHttpRequest request,

            ContentTypeResponse<RedirectionManager[]> onContent,
            BadRequestResponse onBadRequest)
        {
            var methodMaybe = await Method.ById(methodRef, application, (m) => m, () => default(Method?));
            if (methodMaybe.IsDefault())
                return onBadRequest().AddReason("Method no longer supported");

            if (!successOnly.HasValue)
                successOnly = true;

            request.Headers.TryGetValue(
                EastFive.Azure.Auth.ApiKeyAccessAttribute.ParameterName,
                out string[] apiVouchers);
            var apiVoucher = apiVouchers
                .NullToEmpty()
                .First(
                    (x,next) => $"&{EastFive.Azure.Auth.ApiKeyAccessAttribute.ParameterName}={x}", 
                    () => string.Empty);

            var method = methodMaybe.Value;
            var days = daysMaybe.HasValue ? Math.Abs(daysMaybe.Value) : 31;
            var since = DateTime.UtcNow.Date.AddDays(-days);
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
                                if (successOnly.GetValueOrDefault())
                                    return default(RedirectionManager?);

                                return new RedirectionManager
                                {
                                    authorizationRef = id.AsRef<Authorization>(),
                                    info = why,
                                    fields = parameters.Count,
                                    when = when,
                                };
                            }

                            return await method.ParseTokenAsync(parameters, application,
                                (externalId, loginProvider) =>
                                {
                                    return new RedirectionManager
                                    {
                                        authorizationRef = id.AsRef<Authorization>(),
                                        info = $"{externalId}",
                                        fields = parameters.Count,
                                        when = when,
                                        link = new Uri(
                                            request.RequestUri,
                                            $"/api/RedirectionManager?authorization={id}{apiVoucher}"),
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
        }

        // commented this out since Postman doesn't behave like a regular browser
        //[Api.Meta.Flows.WorkflowStep(
        //    FlowName = Workflows.HijackLoginFlow.FlowName,
        //    StepName = Workflows.HijackLoginFlow.Steps.LaunchLogin,
        //    Step = Workflows.HijackLoginFlow.Ordinals.LaunchLogin)]
        [Api.HttpGet]
        [PIIAdminClaim]
        public static Task<IHttpResponse> GetRedirection(
                //[WorkflowParameterFromVariable(
                //    Value = Workflows.HijackLoginFlow.Variables.Authorization.Get.Value,
                //    Description = Workflows.HijackLoginFlow.Variables.Authorization.Get.Description)]
                [QueryParameter(Name = AuthorizationPropertyName)] IRef<Authorization> authRef,

                AzureApplication application,
                IInvokeApplication endpoints,
                EastFive.Api.Security security,
                IHttpRequest request,
            RedirectResponse onRedirection,
            NotFoundResponse onNotFound,
            GeneralFailureResponse onFailure)
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
                                                        (uri, accountIdMaybe, modifier) => uri,
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

        public struct RedirectionMinimal
        {
            [JsonProperty(PropertyName = AuthorizationPropertyName)]
            public IRef<Authorization> authorizationRef;

            [JsonProperty]
            public string error;

            [JsonProperty]
            public Guid? accountIdMaybe;

            [JsonProperty(PropertyName = "COPY_redirect")]
            public Uri link;

            [JsonProperty]
            public IDictionary<string, string> parameters;
        }

        [Api.HttpAction("Choose")]
        [Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.HijackLoginFlow.FlowName,
            StepName = Workflows.HijackLoginFlow.Steps.ChooseLogin,
            Step = Workflows.HijackLoginFlow.Ordinals.ChooseLogin)]
        [PIIAdminClaim]
        public static async Task<IHttpResponse> GetAllSecureAsync(
                [WorkflowParameter(
                    Value = Workflows.HijackLoginFlow.Variables.Authorization.Set.Value,
                    Description = Workflows.HijackLoginFlow.Variables.Authorization.Set.Description)]
                [QueryParameter(Name = AuthorizationPropertyName)] IRef<Authorization> authorizationRef,

                AzureApplication application,
                IInvokeApplication endpoints,
                IHttpRequest request,
                EastFive.Api.Security security,

            [WorkflowVariable(
                Workflows.HijackLoginFlow.Variables.Authorization.Get.Value,
                AuthorizationPropertyName)]
            ContentTypeResponse<RedirectionMinimal> onSuccess,
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
                                            updatedAuth => 1.AsTask(),
                                            method, externalId, authorization.parameters,
                                            application, request, endpoints, loginProvider, request.RequestUri,
                                        (uri, accountIdMaybe, modifier) => onSuccess(
                                            new RedirectionMinimal
                                            {
                                                authorizationRef = authorizationRef,
                                                accountIdMaybe = accountIdMaybe,
                                                link = uri,
                                                parameters = authorization.parameters,
                                            }),
                                        (why) => onSuccess(
                                            new RedirectionMinimal
                                            {
                                                authorizationRef = authorizationRef,
                                                error = why,
                                                parameters = authorization.parameters,
                                            }),
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Meta.Flows;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
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
        public bool authorized { get; set; }

        [JsonProperty]
        public bool valid_token { get; set; }

        [JsonProperty]
        public Guid? actor_id { get; set; }

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
            Version = Workflows.HijackLoginFlow.Version,
            StepName = Workflows.HijackLoginFlow.Steps.ListLogins,
            Step = Workflows.HijackLoginFlow.Ordinals.ListLogins)]
        [PIIAdminClaim(AllowLocalHost = true)]
        public async static Task<IHttpResponse> QueryAsync(
                [WorkflowParameterFromVariable(
                    Value = Workflows.HijackLoginFlow.Variables.Method.Get.Value,
                    Description = Workflows.HijackLoginFlow.Variables.Method.Get.Description)]
                [QueryParameter(Name = "method")] IRef<Method> methodRef,

                [WorkflowParameter(
                    Value = Workflows.HijackLoginFlow.Variables.Search.Set.Value,
                    Description = Workflows.HijackLoginFlow.Variables.Search.Set.Description)]
                [OptionalQueryParameter(Name = "search")] string[] search,

                [WorkflowParameter(
                    Value = Workflows.HijackLoginFlow.Variables.History.Set.Value,
                    Description = Workflows.HijackLoginFlow.Variables.History.Set.Description)]
                [OptionalQueryParameter(Name = "days")] int? daysMaybe,

                [WorkflowParameter(
                    Value = Workflows.HijackLoginFlow.Variables.ValidTokens.Set.Value,
                    Description = Workflows.HijackLoginFlow.Variables.ValidTokens.Set.Description)]
                [OptionalQueryParameter(Name = "valid_tokens_only")] bool? validTokensOnly,

                AzureApplication application,
                EastFive.Api.Security security,
                IHttpRequest request,

            ContentTypeResponse<RedirectionManager[]> onContent,
            BadRequestResponse onBadRequest)
        {
            var methodMaybe = await Method.ById(methodRef, application, (m) => m, () => default(Method?));
            if (methodMaybe.IsDefault())
                return onBadRequest().AddReason("Method no longer supported");

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
            var hasSearch = search.Length > 0 && search[0].HasBlackSpace();

            var authorizations = Authorization.GetMatchingAuthorizations(method.authenticationId, days, false);
            var redirections = await authorizations
                .Where(
                    authorizationTpl =>
                    {
                        if (!hasSearch)
                            return true;

                        var (id, when, parameters, accountIdMaybe, authorized) = authorizationTpl;
                        var match = parameters.Values
                                .SelectWhereNotNull()
                                .Any(val => search.Any(searchItem => val.IndexOf(searchItem, StringComparison.OrdinalIgnoreCase) != -1));

                        return match;
                    })
                .Select(
                    async authorizationTpl =>
                    {
                        var (id, when, parameters, accountIdMaybe, authorized) = authorizationTpl;
                        return await method.ParseTokenAsync(parameters, application,
                            (externalId, loginProvider) =>
                            {
                                return new RedirectionManager
                                {
                                    authorizationRef = id.AsRef<Authorization>(),
                                    info = $"{externalId}",
                                    authorized = authorized,
                                    valid_token = true,
                                    actor_id = accountIdMaybe,
                                    fields = parameters.Count,
                                    when = when,
                                    link = new Uri(
                                        request.RequestUri,
                                        $"/api/RedirectionManager?authorization={id}{apiVoucher}"),
                                };
                            },
                            (why) => Failure(why));

                        RedirectionManager? Failure(string why)
                        {
                            if (validTokensOnly.GetValueOrDefault())
                                return default(RedirectionManager?);

                            return new RedirectionManager
                                {
                                    authorizationRef = id.AsRef<Authorization>(),
                                    info = why,
                                    authorized = authorized,
                                    actor_id = accountIdMaybe,
                                    fields = parameters.Count,
                                    when = when,
                                };
                        }
                    })
                .Await(readAhead: 10)
                .SelectWhereHasValue()
                .ToArrayAsync();
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
            ContentTypeResponse<string> onFailure)
        {
            return authRef.StorageUpdateAsync(
                async (authorization, saveAsync) =>
                {
                    return await await Method.ById(authorization.Method, application,
                        async method =>
                        {
                            return await await method.ParseTokenAsync(authorization.parameters, application,
                                async (externalId, loginProvider) =>
                                {
                                    var tag = "ACPTool";
                                    return await EastFive.Web.Configuration.Settings.GetString($"AffirmHealth.PDMS.PingRedirect.{tag}.PingAuthName",
                                        async pingAuthName =>
                                        {
                                            return await EastFive.Web.Configuration.Settings.GetGuid($"AffirmHealth.PDMS.PingRedirect.{tag}.PingReportSetId",
                                                async (reportSetId) =>
                                                {
                                                    var requestParams = authorization.parameters
                                                        .AppendIf("PingAuthName".PairWithValue(pingAuthName), !authorization.parameters.ContainsKey("PingAuthName"))
                                                        .AppendIf("ReportSetId".PairWithValue(reportSetId.ToString()), !authorization.parameters.ContainsKey("ReportSetId"))
                                                        .ToDictionary();

                                                    return await await Auth.Redirection.ProcessAsync(authorization, 
                                                            updatedAuth => 1.AsTask(),
                                                            method, externalId, requestParams,
                                                            application, request, endpoints, loginProvider, request.RequestUri,
                                                        async (uri, accountIdMaybe, modifier) =>
                                                        {
                                                            authorization.expired = false;
                                                            await saveAsync(authorization);
                                                            return onRedirection(uri);
                                                        },
                                                        (why) => onFailure(why).AsTask(),
                                                        application.Telemetry);
                                                },
                                                why => onFailure(why).AsTask());
                                        },
                                        why => onFailure(why).AsTask());
                                },
                                (why) => onFailure(why).AsTask());
                        },
                        () => onFailure("This login provider is not available").AsTask());
                },
                () => onFailure("The authorization was not found"));
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
            Version = Workflows.HijackLoginFlow.Version,
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

        [Api.HttpAction("CreateRedirection")]
        [SuperAdminClaim(AllowLocalHost =true)]
        public static async Task<IHttpResponse> CreateRedirectionAsync(
                [QueryParameter(Name = "account")]Guid account,
                AzureApplication application,
                IInvokeApplication endpoints,
                IHttpRequest request,
            RedirectResponse onSuccess,
            NotFoundResponse onNotFound,
            GeneralConflictResponse onFailure,
            ForbiddenResponse onDisabled)
        {
            return await await application.CreateHijackableAuthorizationAsync(account,
                onCreated: async (Authorization authorization) =>
                {
                    return await await Method.ById(authorization.Method, application,
                        async method =>
                        {
                            return await method.GetLoginProvider(application,
                                (loginProvider) =>
                                {
                                    var authorizationRef = authorization.authorizationRef;
                                    return Auth.Redirection.CreateLoginResponseAsync(account,
                                            authorization.parameters, method, authorization,
                                            application, request, endpoints,
                                            request.RequestUri, loginProvider,
                                            onRedirect:(uri, modifier) =>
                                            {
                                                var response = onSuccess(uri);
                                                var modifiedResponse = modifier(response);
                                                return modifiedResponse;
                                            },
                                            onBadResponse:(why) => onFailure(why),
                                            application.Telemetry);
                                },
                                why => onFailure(why).AsTask());
                        },
                        () => onFailure("Method no longer supported").AsTask());
                },
                onDisabled: () =>
                {
                    return onDisabled().AddReason("Disabled.").AsTask();
                },
                onNotFound: () =>
                {
                    return onNotFound().AsTask();
                },
                onFailure: (why) =>
                {
                    return onFailure(why).AsTask();
                });
        }

        #endregion

        #endregion
    }
}

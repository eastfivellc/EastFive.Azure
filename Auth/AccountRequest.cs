using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Azure.Persistence;
using EastFive.Api.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Azure.Auth
{
    [FunctionViewController(
        Route = "AccountRequest",
        Namespace = "api",
        ContentType = "x-application/e5-account-request",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    public class AccountRequest : IReferenceable
    {
        #region Properties

        #region Base

        [JsonIgnore]
        public Guid id => this.accountRequestRef.id;

        public const string IdPropertyName = "id";
        [JsonProperty(PropertyName = IdPropertyName)]
        [ApiProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 2)]
        public IRef<AccountRequest> accountRequestRef;

        [JsonIgnore]
        [ETag]
        public string eTag;

        #endregion

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        [Storage(Name = AuthorizationPropertyName)]
        public IRef<Authorization> authorization { get; set; }

        public const string WhenPropertyName = "when";
        [ApiProperty(PropertyName = WhenPropertyName)]
        [JsonProperty(PropertyName = WhenPropertyName)]
        [Storage(Name = WhenPropertyName)]
        public DateTime? when { get; set; }

        public const string ApprovedWhenPropertyName = "approved_when";
        [ApiProperty(PropertyName = ApprovedWhenPropertyName)]
        [JsonProperty(PropertyName = ApprovedWhenPropertyName)]
        [Storage(Name = ApprovedWhenPropertyName)]
        public DateTime? approvedWhen { get; set; }

        public const string ApprovedByPropertyName = "approved_by";
        [ApiProperty(PropertyName = ApprovedByPropertyName)]
        [JsonProperty(PropertyName = ApprovedByPropertyName)]
        [Storage(Name = ApprovedByPropertyName)]
        public Guid? approvedBy { get; set; }

        public const string AccountPropertyName = "account";
        [ApiProperty(PropertyName = AccountPropertyName)]
        [JsonProperty(PropertyName = AccountPropertyName)]
        [Storage(Name = AccountPropertyName)]
        public Guid? account { get; set; }

        #endregion

        /// <summary>
        /// Flattened projection of an <see cref="AccountRequest"/> and its
        /// <see cref="Authorization"/> for administrative review.
        /// </summary>
        public struct AccountRequestDetail
        {
            [JsonProperty(PropertyName = IdPropertyName)]
            public Guid id;

            [JsonProperty(PropertyName = WhenPropertyName)]
            public DateTime? when;

            [JsonProperty(PropertyName = "method")]
            public string method;

            [JsonProperty(PropertyName = "parameters")]
            public IDictionary<string, string> parameters;

            [JsonProperty(PropertyName = ApprovedWhenPropertyName)]
            public DateTime? approvedWhen;

            [JsonProperty(PropertyName = ApprovedByPropertyName)]
            public Guid? approvedBy;

            [JsonProperty(PropertyName = AccountPropertyName)]
            public Guid? account;
        }

        #region HTTP Methods

        #region Actions

        public const string LaunchAction = "Launch";
        [Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Step = 1.9)]
        [Unsecured("OAuth launch endpoint - initiates authentication flow, no bearer token available before authentication")]
        [HttpAction(LaunchAction)]
        public static async Task<IHttpResponse> LaunchAsync(

                [Api.Meta.Flows.WorkflowParameter(Value = "{{AuthenticationMethod}}")]
                [QueryParameter(Name = "method")]IRef<Method> methodRef,

                RequestMessage<AccountRequest> api,
                IHttpRequest request,
                IAzureApplication application,
                IProvideUrl urlHelper,
            [Api.Meta.Flows.WorkflowVariableRedirectUrl(
                VariableName = Workflows.AuthorizationFlow.Variables.RedirectUrl)]
            RedirectResponse onLaunched,
            BadRequestResponse onInvalidMethod)
        {
            return await await Method.ById(methodRef, application,
                async method =>
                {
                    var authRef = Ref<Authorization>.SecureRef();
                    var authorization = new Authorization
                    {
                        authorizationRef = authRef,
                        LocationAuthenticationReturn = api
                            // .Where(query => query.authorization == authRef)
                            .HttpAction(ResponseAction)
                            .CompileRequest(request)
                            .RequestUri,
                        Method = methodRef,
                    };

                    return await await authorization.StorageCreateAsync(
                        async (discard) =>
                        {
                            var redir = await method.GetLoginUrlAsync(
                                application, urlHelper, authRef.id);
                            return onLaunched(redir);
                        });
                },
                () => onInvalidMethod().AsTask());
        }

        #endregion

        #region Actions

        public const string ResponseAction = "Response";
        [Unsecured("Account request endpoint - allows users to request account without existing authentication")]
        [HttpAction(ResponseAction)]
        public static Task<IHttpResponse> ResponseAsync(
                [QueryParameter(Name = EastFive.Api.Azure.AzureApplication.QueryRequestIdentfier)]
                    IRef<Authorization> authorizationRef,
            TextResponse onCompleted)
        {
            var accountRequest = new AccountRequest()
            {
                accountRequestRef = Ref<AccountRequest>.NewRef(),
                authorization = authorizationRef,
                when = DateTime.UtcNow,
            };
            return accountRequest.StorageCreateAsync(
                discard =>
                {
                    return onCompleted("Your account has been requested. Thank you.");
                });
        }

        public const string ListAction = "List";
        [HttpAction(ListAction)]
        [SuperAdminClaim]
        public static IHttpResponse List(
                RequestMessage<AccountRequest> api,
            MultipartAsyncResponse<(Authorization, IDictionary<string, string>)> onListed)
        {
            return api
                .StorageGet()
                .Select(
                    request => request.authorization.StorageGetAsync(
                        auth => (auth, auth.parameters),
                        () => (default((Authorization, IDictionary<string, string>)?))))
                .Await()
                .SelectWhereHasValue()
                .HttpResponse(onListed);
        }

        public const string ListDetailAction = "ListDetail";
        [HttpAction(ListDetailAction)]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> ListDetailAsync(
                RequestMessage<AccountRequest> api,
                IApplication application,
            MultipartAcceptArrayResponse<AccountRequestDetail> onListed)
        {
            var details = await api
                .StorageGet()
                .Select(
                    request => request.authorization.StorageGetAsync(
                        async authorization =>
                        {
                            var detail = await CreateDetailAsync(request, authorization, application);
                            return (AccountRequestDetail?)detail;
                        },
                        () => default(AccountRequestDetail?).AsTask()))
                .Await()
                .Await()
                .SelectWhereHasValue()
                .ToArrayAsync();
            return onListed(details);
        }

        private static async Task<AccountRequestDetail> CreateDetailAsync(AccountRequest accountRequest,
            Authorization authorization, IApplication application)
        {
            var methodName = await Method.ById(authorization.Method, application,
                method => method.name,
                () => string.Empty);
            return new AccountRequestDetail
            {
                id = accountRequest.id,
                when = accountRequest.when.HasValue ?
                    accountRequest.when
                    :
                    authorization.lastModified,
                method = methodName,
                parameters = authorization.parameters,
                approvedWhen = accountRequest.approvedWhen,
                approvedBy = accountRequest.approvedBy,
                account = accountRequest.account,
            };
        }

        public const string ApproveAction = "Approve";
        [HttpAction("POST", ApproveAction)]
        [SuperAdminClaim]
        public static Task<IHttpResponse> ApproveAsync(
                [QueryParameter(Name = IdPropertyName)] IRef<AccountRequest> accountRequestRef,
                IAzureApplication application,
                IHttpRequest request,
                SessionToken security,
            ContentTypeResponse<AccountRequestDetail> onApproved,
            NotFoundResponse onNotFound,
            GeneralConflictResponse onFailure)
        {
            return accountRequestRef.StorageUpdateAsync(
                async (AccountRequest accountRequest, Func<AccountRequest, Task<IUpdateTableResult>> saveAsync) =>
                {
                    if (accountRequest.account.HasValue)
                        return onFailure("This account request has already been approved.");

                    return await await accountRequest.authorization.StorageGetAsync(
                        async authorization =>
                        {
                            return await await Method.ById(authorization.Method, application,
                                async method =>
                                {
                                    return await await method.ParseTokenAsync(authorization.parameters,
                                            application,
                                        onParsed: async (externalAccountKey, loginProvider) =>
                                        {
                                            return await await Redirection.IdentifyAccountAsync(
                                                    authorization,
                                                    method, externalAccountKey, authorization.parameters,
                                                    application, loginProvider, request,
                                                onLocated: (accountId, claims) =>
                                                    OnAccountReadyAsync(accountId),
                                                onInterupted: (interceptionUrl, accountId, claims) =>
                                                    OnAccountReadyAsync(accountId),
                                                onGeneralFailure: why => onFailure(why).AsTask(),
                                                    telemetry: application.Telemetry);

                                            async Task<IHttpResponse> OnAccountReadyAsync(Guid accountId)
                                            {
                                                await authorization.authorizationRef.StorageUpdateAsync(
                                                    async (Authorization auth, Func<Authorization, Task<IUpdateTableResult>> saveAuthAsync) =>
                                                    {
                                                        auth.accountIdMaybe = accountId;
                                                        auth.authorized = true;
                                                        await saveAuthAsync(auth);
                                                        return true;
                                                    },
                                                    () => false);

                                                accountRequest.account = accountId;
                                                accountRequest.approvedWhen = DateTime.UtcNow;
                                                accountRequest.approvedBy = security.accountIdMaybe;
                                                await saveAsync(accountRequest);

                                                var detail = await CreateDetailAsync(
                                                    accountRequest, authorization, application);
                                                return onApproved(detail);
                                            }
                                        },
                                        onFailure: why => onFailure(why).AsTask());
                                },
                                () => onFailure("The authentication method for this request is no longer enabled.").AsTask());
                        },
                        () => onFailure("The authorization for this account request no longer exists.").AsTask());
                },
                onNotFound: () => onNotFound());
        }

        #endregion

        #endregion
    }
}


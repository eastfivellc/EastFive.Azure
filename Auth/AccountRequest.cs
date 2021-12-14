using System;
using System.Linq;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;

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

        #endregion

        #region HTTP Methods

        #region Actions

        public const string LaunchAction = "Launch";
        [Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Step = 1.9)]
        [HttpAction(LaunchAction)]
        public static async Task<IHttpResponse> LaunchAsync(

                [Api.Meta.Flows.WorkflowParameter(Value = "{{AuthenticationMethod}}")]
                [QueryParameter(Name = "method")]IRef<Method> methodRef,

                RequestMessage<AccountRequest> api,
                IHttpRequest request,
                IAzureApplication application,
                IProvideUrl urlHelper,
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
        [HttpAction(ResponseAction)]
        [SecurityRoleRequired(RolesAllowed = new string[] { ClaimValues.Roles.SuperAdmin })]
        public static Task<IHttpResponse> ResponseAsync(
                [QueryParameter(Name = EastFive.Api.Azure.AzureApplication.QueryRequestIdentfier)]
                    IRef<Authorization> authorizationRef,
            TextResponse onCompleted)
        {
            var accountRequest = new AccountRequest()
            {
                accountRequestRef = Ref<AccountRequest>.NewRef(),
                authorization = authorizationRef,
            };
            return accountRequest.StorageCreateAsync(
                discard =>
                {
                    return onCompleted("Your account has been requested. Thank you.");
                });
        }

        #endregion

        #endregion
    }
}


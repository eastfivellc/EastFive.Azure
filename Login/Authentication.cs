using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Security;
using EastFive.Serialization;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Net.Http.Headers;
using EastFive.Api.Auth;
using EastFive.Azure.Auth;
using EastFive.Api.Meta.Flows;

namespace EastFive.Azure.Login
{
    [FunctionViewController(
        Route = "Authentication",
        ContentType = "x-application/login-authentication",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    [Html(Title = "Login", Preference = -10.0)]
    public struct Authentication : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => authenticationRef.id;

        public const string AuthenticationPropertyName = "id";
        [ApiProperty(PropertyName = AuthenticationPropertyName)]
        [JsonProperty(PropertyName = AuthenticationPropertyName)]
        [RowKey]
        [StandardParititionKey]
        [HtmlInputHidden]
        public IRef<Authentication> authenticationRef;

        public const string UserIdentificationPropertyName = "user_identification";
        [ApiProperty(PropertyName = UserIdentificationPropertyName)]
        [JsonProperty(PropertyName = UserIdentificationPropertyName)]
        [HtmlInput(Label = "Username or email")]
        [Storage]
        public string userIdentification;

        public const string PasswordPropertyName = "password";
        [ApiProperty(PropertyName = PasswordPropertyName)]
        [JsonProperty(PropertyName = PasswordPropertyName)]
        [HtmlInput(Label = "Password")]
        public string password;

        #region Validation of authorization paramters

        public const string AuthenticatedPropertyName = "authenticated";
        [ApiProperty(PropertyName = AuthenticatedPropertyName)]
        [JsonProperty(PropertyName = AuthenticatedPropertyName)]
        [Storage]
        public DateTime? authenticated;

        public const string StatePropertyName = "state";
        [Storage]
        [JsonIgnore]
        public string state;

        public const string ClientPropertyName = "client";
        [ApiProperty(PropertyName = ClientPropertyName)]
        [JsonProperty(PropertyName = ClientPropertyName)]
        [Storage]
        public IRef<Client> client;

        public const string ValidationPropertyName = "validation";
        [ApiProperty(PropertyName = ValidationPropertyName)]
        [JsonProperty(PropertyName = ValidationPropertyName)]
        [Storage] // for auditing...?
        public string validation;

        #endregion

        //public const string AccountPropertyName = "account";
        //[HtmlLink(Label = "Create new account")]
        //[JsonIgnore]
        //public IRefOptional<Account> account;

        #endregion

        #region HTTP Methods

        [Api.HttpGet]
        public static async Task<IHttpResponse> GetAsync(
                [QueryParameter(Name = AuthenticationPropertyName)]IRef<Authentication> authenticationRef,
                [Accepts(Media = "text/html")]MediaTypeWithQualityHeaderValue accept,
            ContentTypeResponse<Authentication> onFound,
            HtmlResponse onHtmlWanted,
            NotFoundResponse onNotFound)
        {
            if (!accept.IsDefaultOrNull())
                return onHtmlWanted(Properties.Resources.loginHtml);
            return await authenticationRef.StorageGetAsync(
                (authentication) =>
                {
                    return onFound(authentication);
                },
                () => onNotFound());
        }

        [WorkflowStep(
            FlowName = Workflows.PasswordLoginCreateAccount.FlowName,
            Step = 2.0,
            StepName = "Start Authentication Process")]
        [Api.HttpGet]
        public static async Task<IHttpResponse> GetAsync(
                [WorkflowNewId]
                [WorkflowVariable(
                    Workflows.PasswordLoginCreateAccount.Variables.State,
                    StatePropertyName)]
                [QueryParameter(Name = StatePropertyName)]
                string state,

                [WorkflowParameter(
                    Value = "d989b604-1e25-4d77-b79e-fe1c7d36f833",
                    Description = "Unique and static to each client (i.e. iOS or Web)")]
                [QueryParameter(Name = ClientPropertyName)]
                IRef<Client> clientRef,

                [WorkflowNewId(Description = "No idea what this does.")]
                [QueryParameter(Name = ValidationPropertyName)]
                string validation,

                IAuthApplication application, IProvideUrl urlHelper,
            //ContentTypeResponse<Authentication> onFound,

            [WorkflowVariable(
                Workflows.PasswordLoginCreateAccount.Variables.Authorization,
                Authentication.AuthenticationPropertyName)]
            [WorkflowVariableRedirectUrl(
                VariableName = Workflows.PasswordLoginCreateAccount.Variables.AuthorizationRedirect)]
            RedirectResponse onFound,

            ReferencedDocumentNotFoundResponse<Client> onInvalidClient)
        {
            return await await clientRef.StorageGetAsync(
                (client) =>
                {
                    var authentication = new Authentication
                    {
                        authenticationRef = SecureGuid.Generate().AsRef<Authentication>(),
                        state = state,
                        client = clientRef,
                    };
                    return authentication.StorageCreateAsync(
                        (entity) =>
                        {
                            var location = urlHelper.GetLocation<Authentication>(
                                auth => auth.authenticationRef.AssignQueryValue(authentication.authenticationRef),
                                application);
                            return onFound(location);
                        });
                },
                () => onInvalidClient().AsTask());
        }

        public struct AuthorizationParameters
        {
            public IRef<Authentication> state;
            public string token;
        }

        [WorkflowStep(
            FlowName = Workflows.PasswordLoginCreateAccount.FlowName,
            Step = 3.0,
            StepName = "Login")]
        [Api.HttpPatch]
        [HtmlAction(Label = "Login")]
        public static async Task<IHttpResponse> UpdateAsync(
                [WorkflowParameterFromVariable(
                    Value = Workflows.PasswordLoginCreateAccount.Variables.AuthenticationId)]
                [UpdateId(Name = AuthenticationPropertyName)]
                IRef<Authentication> authenticationRef,

                [WorkflowParameter(Value = "true")]
                [OptionalQueryParameter(Name = "hold")]
                bool? hold,

                [WorkflowParameterFromVariable(
                    Value = Workflows.PasswordLoginCreateAccount.Variables.UserId)]
                [Property(Name = UserIdentificationPropertyName)]
                string userIdentification,

                [WorkflowParameterFromVariable(
                    Value = Workflows.PasswordLoginCreateAccount.Variables.Password)]
                [Property(Name = PasswordPropertyName)]
                string password,

                [WorkflowHeaderRequired("Accept", "application/json")]
                Microsoft.Net.Http.Headers.MediaTypeHeaderValue[] acceptsTypes,

                IHttpRequest httpRequest,
            RedirectResponse onUpdated,

            [WorkflowVariable(Workflows.PasswordLoginCreateAccount.Variables.State, StatePropertyName)]
            [WorkflowVariable2(Workflows.PasswordLoginCreateAccount.Variables.Token, "token")]
            ContentTypeResponse<AuthorizationParameters> onJsonPreferred,

            ContentTypeResponse<string> onHeldup,
            NotFoundResponse onNotFound,
            GeneralConflictResponse onInvalidPassword)
        {
            return await await authenticationRef.StorageUpdateAsync(
                (authentication, saveAsync) =>
                {
                    return userIdentification
                        .MD5HashGuid()
                        .AsRef<Account>()
                        .StorageGetAsync(
                            async account =>
                            {
                                var passwordHash = Account.GeneratePasswordHash(userIdentification, password);
                                if (passwordHash != account.password)
                                    return onInvalidPassword("Incorrect username or password.");
                                authentication.userIdentification = userIdentification;
                                authentication.authenticated = DateTime.UtcNow;
                                await saveAsync(authentication);
                                var authorizationUrl = new Uri(httpRequest.RequestUri, $"/api/LoginRedirection?state={authentication.authenticationRef.id}&token={authentication.state}");

                                if (hold.HasValue && hold.Value)
                                {
                                    if (acceptsTypes.Any(hdr => "application/json".Equals(hdr.MediaType.ToString(), StringComparison.OrdinalIgnoreCase)))
                                    {
                                        var authorizationParameters = new AuthorizationParameters
                                        {
                                            state = authentication.authenticationRef,
                                            token = authentication.state,
                                        };
                                        return onJsonPreferred(authorizationParameters);
                                    }
                                    return onHeldup(authorizationUrl.AbsoluteUri);
                                }
                                return onUpdated(authorizationUrl);
                            },
                            () => onInvalidPassword("Incorrect username or password.").AsTask());
                },
                () => onNotFound().AsTask());
        }

        #endregion

    }
}

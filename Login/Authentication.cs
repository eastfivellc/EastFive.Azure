using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Security;
using EastFive.Serialization;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Net.Http.Headers;

namespace EastFive.Azure.Login
{
    [FunctionViewController5(
        Route = "Authentication",
        Resource = typeof(Authentication),
        ContentType = "x-application/login-authentication",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    [Html(Title = "Login")]
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
        [ApiProperty(PropertyName = StatePropertyName)]
        [JsonProperty(PropertyName = StatePropertyName)]
        [Storage]
        [JsonIgnore]
        public string state;

        public const string ClientPropertyName = "client";
        [ApiProperty(PropertyName = ClientPropertyName)]
        [JsonProperty(PropertyName = ClientPropertyName)]
        [Storage]
        public Guid client;

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
        public static async Task<HttpResponseMessage> GetAsync(
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

        [Api.HttpGet]
        public static async Task<HttpResponseMessage> GetAsync(
                [QueryParameter(Name = StatePropertyName)]string state,
                [QueryParameter(Name = ClientPropertyName)]IRef<Client> clientRef,
                [QueryParameter(Name = ValidationPropertyName)]string validation,
                IAuthApplication application, UrlHelper urlHelper,
            //ContentTypeResponse<Authentication> onFound,
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
                    };
                    return authentication.StorageCreateAsync(
                        (entity) =>
                        {
                            var location = urlHelper.GetLocation<Authentication>(
                                auth => auth.authenticationRef.AssignQueryValue(authentication.authenticationRef),
                                application);
                            return onFound(location);
                        },
                        () => throw new Exception("Secure guid not unique"));
                },
                () => onInvalidClient().AsTask());
        }

        [Api.HttpPatch]
        [HtmlAction(Label = "Login")]
        public static async Task<HttpResponseMessage> UpdateAsync(
                [UpdateId(Name = AuthenticationPropertyName)]IRef<Authentication> authenticationRef,
                [OptionalQueryParameter(Name = "hold")]bool? hold,
                [Property(Name = UserIdentificationPropertyName)]string userIdentification,
                [Property(Name = PasswordPropertyName)]string password,
                HttpRequestMessage httpRequest,
            RedirectResponse onUpdated,
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
                                //var related = request
                                //    .Related<Login.Redirection>();
                                //var whered = related
                                //    .Where(redir => redir.state == authentication.state);
                                //var authorizationUrl = whered
                                //    // .ById(authentication.st)
                                //    .RenderLocation();
                                var authorizationUrl = new Uri(httpRequest.RequestUri, $"/api/LoginRedirection?state={authentication.authenticationRef.id}&token={authentication.state}");

                                if (hold.HasValue && hold.Value)
                                    return onHeldup(authorizationUrl.AbsoluteUri);
                                return onUpdated(authorizationUrl);
                            },
                            () => onInvalidPassword("Incorrect username or password.").AsTask());
                },
                () => onNotFound().AsTask());
        }

        #endregion
    }
}

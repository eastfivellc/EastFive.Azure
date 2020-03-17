using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Routing;
using BlackBarLabs.Api;
using BlackBarLabs.Api.Resources;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Controllers;
using EastFive.Azure.Auth;
using EastFive.Extensions;
using Newtonsoft.Json;
using EastFive.Azure;

namespace EastFive.Security.SessionServer.Api.Resources
{
    [DataContract]
    [FunctionViewController(Route = "PasswordCredential")]
    public class PasswordCredential : BlackBarLabs.Api.ResourceBase
    {
        #region Properties
        
        public const string ActorPropertyName = "actor";
        [DataMember]
        [JsonProperty(PropertyName = ActorPropertyName)]
        public WebId Actor { get; set; }

        public const string DisplayNamePropertyName = "display_name";
        [DataMember]
        [JsonProperty(PropertyName = DisplayNamePropertyName)]
        public string DisplayName { get; set; }

        public const string UserIdPropertyName = "user_id";
        [DataMember]
        [JsonProperty(PropertyName = UserIdPropertyName)]
        public string UserId { get; set; }

        public const string TokenPropertyName = "token";
        [DataMember]
        [JsonProperty(PropertyName = TokenPropertyName)]
        public string Token { get; set; }

        public const string IsEmailPropertyName = "is_email";
        [DataMember]
        [JsonProperty(PropertyName = IsEmailPropertyName)]
        public bool IsEmail { get; set; }

        public const string ForceChangePropertyName = "force_change";
        [DataMember]
        [JsonProperty(PropertyName = ForceChangePropertyName)]
        public bool ForceChange { get; set; }

        public const string LastEmailSentPropertyName = "last_email_sent";
        [DataMember]
        [JsonProperty(PropertyName = LastEmailSentPropertyName)]
        public DateTime? LastEmailSent { get; set; }

        #endregion

        #region HTTP Methods
        
        [HttpPost(MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> CreatePasswordCredentialAsync(
                [Property(Name = IdPropertyName)]Guid credentialId,
                [Property(Name = ActorPropertyName)]Guid actorId,
                [PropertyOptional(Name = DisplayNamePropertyName)]string displayName,
                [PropertyOptional(Name = UserIdPropertyName)]string userId,
                [PropertyOptional(Name = TokenPropertyName)]string token,
                [PropertyOptional(Name = IsEmailPropertyName)]bool isEmail,
                [PropertyOptional(Name = ForceChangePropertyName)]bool forceChange,
                [PropertyOptional(Name = LastEmailSentPropertyName)]DateTime? lastEmailSent,
                Context context, IAuthApplication application,
                UrlHelper url,
                EastFive.Api.SessionToken security,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists,
            GeneralConflictResponse onUsernameAlreadyInUse,
            GeneralConflictResponse onInsufficentPassword,
            GeneralConflictResponse onUsernameAlreadyMappedToActor,
            UnauthorizedResponse onUnauthorized,
            ServiceUnavailableResponse onServiceUnavailable,
            GeneralConflictResponse onFailure)
        {
            var callbackUrl = url.GetLocation<OpenIdResponse>();

            return context.PasswordCredentials.CreatePasswordCredentialsAsync(
                    credentialId, actorId,
                    displayName, userId, isEmail, token, forceChange,
                    lastEmailSent, callbackUrl,
                    security, application,
                () => onCreated(),
                () => onAlreadyExists(),
                (actorUsingId) => onUsernameAlreadyInUse($"Username already in use with Actor:{actorUsingId}"),
                () => onInsufficentPassword($"Password is insufficient."),
                () => onUsernameAlreadyMappedToActor($"Relationship already exists"),
                () => onUsernameAlreadyMappedToActor($"Login is already in use"),
                () => onUnauthorized(),
                () => onServiceUnavailable(),
                (why) => onFailure(why));
        }

        [HttpPut(MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> PutAsync(
                [Property(Name = IdPropertyName)]Guid credentialId,
                [PropertyOptional(Name = TokenPropertyName)]string token,
                [PropertyOptional(Name = ForceChangePropertyName)]bool forceChange,
                [PropertyOptional(Name = LastEmailSentPropertyName)]DateTime? lastEmailSent,
                Context context, AzureApplication application,
                EastFive.Api.SessionToken security,
            NoContentResponse onUpdated,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized,
            ServiceUnavailableResponse onServiceUnavailable,
            GeneralConflictResponse onFailure)
        {
            return context.PasswordCredentials.UpdatePasswordCredentialAsync(credentialId,
                    token, forceChange, lastEmailSent,
                    security, application,
                () => onUpdated(),
                () => onNotFound(),
                () => onUnauthorized(),
                () => onServiceUnavailable(),
                (why) => onFailure($"Update failed:{why}"));
        }

        #region GET
        
        [HttpGet]
        public static async Task<HttpResponseMessage> QueryByIdAsync(
                [QueryParameter(Name = IdPropertyName)]Guid credentialId,
                Context context, AzureApplication application,
                EastFive.Api.SessionToken security,
                UrlHelper urlHelper,
            ContentResponse onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized,
            ServiceUnavailableResponse onServiceUnavailable)
        {
            return await context.PasswordCredentials.GetPasswordCredentialAsync(credentialId,
                    security, application,
                (passwordCredential) =>
                {
                    var passwordCred = Convert(passwordCredential, urlHelper);
                    var response = onFound(passwordCred);
                    return response;
                },
                () => onNotFound(),
                () => onUnauthorized(),
                (why) => onServiceUnavailable().AddReason(why));
        }

        [HttpGet]
        public async static Task<HttpResponseMessage> QueryByActorId(
                [QueryParameter(Name = ActorPropertyName)]Guid actorId,
                Context context, AzureApplication application,
                EastFive.Api.SessionToken security,
                UrlHelper urlHelper,
            ContentResponse onFound,
            MultipartResponseAsync onFounds,
            ReferencedDocumentNotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized,
            ServiceUnavailableResponse onServiceUnavailable)
        {
            return await await context.PasswordCredentials.GetPasswordCredentialByActorAsync(
                    actorId,
                    security, application, 
                (credentials) =>
                {
                    var credentialResources = credentials
                        .Select(
                            passwordCredential =>
                            {
                                var resource = Convert(passwordCredential, urlHelper);
                                var response = onFound(resource);
                                return response;
                            })
                        .ToArray();
                    return onFounds(credentialResources);
                },
                () => onNotFound().AsTask(),
                () => onUnauthorized().AsTask(),
                (why) => onServiceUnavailable().AsTask());
        }

        private static Resources.PasswordCredential Convert(EastFive.Api.Azure.Credentials.PasswordCredential passwordCredential, UrlHelper urlHelper)
        {
            return new Resources.PasswordCredential
            {
                Id = urlHelper.GetWebId<PasswordCredential>(passwordCredential.id),
                Actor = passwordCredential.actorId,
                UserId = passwordCredential.userId,
                IsEmail = passwordCredential.isEmail,
                ForceChange = passwordCredential.forceChangePassword,
                Token = String.Empty,
                LastEmailSent = passwordCredential.lastSent,
                DisplayName = passwordCredential.displayName
            };
        }
        
        [HttpDelete]
        public static async Task<HttpResponseMessage> DeleteByIdAsync(
                [Property(Name = IdPropertyName)]Guid passwordCredentialId,
                Context context, AzureApplication application,
                EastFive.Api.SessionToken security,
                UrlHelper urlHelper,
            NoContentResponse onDeleted,
            UnauthorizedResponse onUnauthorized,
            NotFoundResponse onNotFound,
            ServiceUnavailableResponse onServiceUnavailable)
        {
            return await context.PasswordCredentials.DeletePasswordCredentialAsync(passwordCredentialId,
                    security, application,
                () =>
                {
                    var response = onDeleted();
                    return response;
                },
                () => onUnauthorized(),
                () => onNotFound(),
                (why) => onServiceUnavailable().AddReason(why));
        }

        [HttpOptions]
        public static HttpResponseMessage CredentialOptionsAsync(ContentResponse onContent)
        {
            var credentialProviders = new Resources.PasswordCredential[]
            {
                new Resources.PasswordCredential
                {
                    UserId = "0123456789",
                    Token = "ABC.123.MXC",
                },
                new Resources.PasswordCredential
                {
                    //Method = CredentialValidationMethodTypes.OpenIdConnect,
                    //Provider = new Uri("urn:auth.gibbits.nc2media.com/AuthOpenIdConnect/"),
                    UserId = Guid.NewGuid().ToString("N"),
                    Token = "EDF.123.A3EF",
                },
                new Resources.PasswordCredential
                {
                    //Method = CredentialValidationMethodTypes.Implicit,
                    //Provider = new Uri("http://www.example.com/ImplicitAuth"),
                    UserId = Guid.NewGuid().ToString("N"),
                    Token = Guid.NewGuid().ToString("N"),
                }
            };
            var response = new BlackBarLabs.Api.Resources.Options()
            {
                Get = credentialProviders,
            };

            var responseMessage = onContent(response);
            return responseMessage;
        }

        #endregion

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Security.Claims;

using Microsoft.AspNetCore.Mvc.Routing;

using Newtonsoft.Json;

using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Api.Controllers;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Web.Configuration;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController6(
        Route = "Claim",
        Resource = typeof(Claim),
        ContentType = "x-application/auth-claim",
        ContentTypeVersion = "0.1")]
    public struct Claim : IReferenceable
    {
        #region Properties

        public Guid id => claimRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<Claim> claimRef;

        public const string ActorPropertyName = "actor";
        [ApiProperty(PropertyName = ActorPropertyName)]
        [JsonProperty(PropertyName = ActorPropertyName)]
        [Storage]
        [IdPrefixLookup(Characters = 3)]
        public Guid actorId;

        public const string NamePropertyName = "name";
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        [Storage]
        public string name;

        public const string TypePropertyName = "type";
        [ApiProperty(PropertyName = TypePropertyName)]
        [JsonProperty(PropertyName = TypePropertyName)]
        [Storage]
        public Uri type;

        public const string ValuePropertyName = "value";
        [ApiProperty(PropertyName = ValuePropertyName)]
        [JsonProperty(PropertyName = ValuePropertyName)]
        [Storage]
        public string value;

        public const string DescriptionPropertyName = "description";
        [ApiProperty(PropertyName = DescriptionPropertyName)]
        [JsonProperty(PropertyName = DescriptionPropertyName)]
        [Storage]
        public string description;

        #endregion

        [Api.HttpOptions]
        public static IHttpResponse OptionsAsync(
                IApplication application,
            ContentTypeResponse<Claim[]> onFound)
        {
            var claims = application.GetType()
                .GetAttributesInterface<IDeclareClaim>(true, true)
                .Select(
                    attr => new Claim
                    {
                        name = attr.ClaimName,
                        type = attr.ClaimType,
                        value = string.Empty,
                    })
                .ToArray();
            return onFound(claims);
        }

        [Api.HttpGet]
        public static async Task<IHttpResponse> GetAsync(
                RequestMessage<Claim> claims,
                Authorization auth,
            MultipartResponseAsync<Claim> onFound,
            UnauthorizedResponse onUnauthorized)
        {
            if (!auth.accountIdMaybe.HasValue)
                return onUnauthorized();
            Expression<Func<Claim, bool>> allQuery = (claim) => true;
            var claimsForUser = claims
                .Where(claim => claim.actorId == auth.accountIdMaybe.Value)
                .StorageGet();
            return await onFound(claimsForUser);
        }

        [HttpPost]
        [RequiredClaim(ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static Task<IHttpResponse> CreateAsync(
                [Property(Name = IdPropertyName)]IRef<Claim> claimRef,
                [Property(Name = ActorPropertyName)]Guid actorId,
                [Property(Name = TypePropertyName)]string type,
                [Property(Name = NamePropertyName)]string value,
                [Resource]Claim claim,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists)
        {
            return claim.StorageCreateAsync(
                (discard) => onCreated(),
                onAlreadyExists: () => onAlreadyExists());
        }
    }

    public static class ClaimValues
    {
        public static class Roles
        {
            public const string SuperAdmin = "superadmin";
        }
    }

    public class ClaimEnableRolesAttribute : Attribute, IDeclareClaim
    {
        public const string Type = ClaimTypes.Role;

        public Uri ClaimType => new Uri(Type);

        public string ClaimDescription => "Permisison roles allowed";

        public string ClaimName => "Role";
    }

    public class ClaimEnableAuthenticationAttribute : Attribute, IDeclareClaim
    {
        public const string Type = ClaimTypes.Authentication;

        public Uri ClaimType => new Uri(Type);

        public string ClaimDescription => "The authentication provider used to create the token (AuthorizationMethod in EastFive Auth).";

        public string ClaimName => "Authentication";
    }

    public class ClaimEnableAuthenticationMethodAttribute : Attribute, IDeclareClaim
    {
        public const string Type = ClaimTypes.AuthenticationMethod;

        public Uri ClaimType => new Uri(Type);

        public string ClaimDescription => "The method used by the authentication provider to integrate the client for identity " +
            "i.e. Password, X509Certificate, RSASecureID, Biometric, KnowledgeBasedAuth, None.";

        public string ClaimName => "Authentication Method";
    }

    public class ClaimEnableAuthenticationInstantAttribute : Attribute, IDeclareClaim
    {
        public const string Type = ClaimTypes.AuthenticationInstant;

        public Uri ClaimType => new Uri(Type);

        public string ClaimDescription => "The instance of Authentication provided (Authorization in EastFive Auth).";

        public string ClaimName => "Authentication Instant";
    }
}
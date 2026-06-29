using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Routing;

using EastFive.Api;
using EastFive.Api.Controllers;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using EastFive.Api.Meta.Flows;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController(
        Route = "Whoami",
        ContentType = "x-application/auth-whoami",
        ContentTypeVersion = "0.1")]
    [DisplayEntryPoint]
    public struct Whoami
    {
        public const string SessionPropertyName = "session";
        [ApiProperty(PropertyName = SessionPropertyName)]
        [JsonProperty(PropertyName = SessionPropertyName)]
        public IRef<Session> session;

        public const string NamePropertyName = "name";
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        public string name { get; set; }

        public const string AccountPropertyName = "account";
        [JsonProperty(PropertyName = AccountPropertyName)]
        [ApiProperty(PropertyName = AccountPropertyName)]
        public Guid? account { get; set; }

        public const string TokenPropertyName = "token";
        [JsonProperty(PropertyName = TokenPropertyName)]
        [ApiProperty(PropertyName = TokenPropertyName)]
        public System.IdentityModel.Tokens.Jwt.JwtSecurityToken securityToken;

        public const string SessionExpiresPropertyName = "session_expires";
        [JsonProperty(PropertyName = SessionExpiresPropertyName)]
        [ApiProperty(PropertyName = SessionExpiresPropertyName)]
        public DateTime? sessionExpires;

        public const string RolesPropertyName = "roles";
        [JsonProperty(PropertyName = RolesPropertyName)]
        [ApiProperty(PropertyName = RolesPropertyName)]
        public string[] roles { get; set; }

        [WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Step = 4.0)]
        [Api.HttpGet] //(MatchAllBodyParameters = false)]
        public static async Task<IHttpResponse> GetAsync(
                EastFive.Azure.Auth.SessionToken security,
                IHttpRequest request,
                IApplication application,

            [WorkflowVariable("Session", SessionPropertyName)]
            [WorkflowVariable2("Account", AccountPropertyName)]
            ContentTypeResponse<Whoami> onFound,
            NotFoundResponse onNotFound)
        {
            async Task<string> GetName()
            {
                if (!security.accountIdMaybe.HasValue)
                    return string.Empty;
                return await application.GetActorNameDetailsAsync(security.accountIdMaybe.Value,
                    (first, last, email) =>
                    {
                        return $"{first} {last} [{email}]";
                    },
                    () => string.Empty);
            }
            var name = await GetName();
            request.TryParseJwt(out System.IdentityModel.Tokens.Jwt.JwtSecurityToken securityToken);
            var roles = ExtractRoles(securityToken);
            var sessionRef = security.sessionId.AsRef<Session>();
            return await sessionRef.StorageGetAsync(
                session =>
                {
                    var whoami = new Whoami()
                    {
                        session = sessionRef,
                        account = security.accountIdMaybe,
                        name = name,
                        securityToken = securityToken,
                        sessionExpires = session.expires,
                        roles = roles,
                    };
                    return onFound(whoami);
                },
                onDoesNotExists:() => onNotFound());
        }

        /// <summary>
        /// Pulls the role claim values out of the parsed session JWT, matching the same
        /// <see cref="System.Security.Claims.ClaimTypes.Role"/> claim type the authorization
        /// attributes check. Comma-joined values are split so callers receive one role per entry.
        /// Returned values are the raw claim values (e.g. <c>"superadmin"</c>) so a UI can gate
        /// sections without re-parsing the token client-side.
        /// </summary>
        private static string[] ExtractRoles(System.IdentityModel.Tokens.Jwt.JwtSecurityToken securityToken)
        {
            if (securityToken == null)
                return Array.Empty<string>();
            return securityToken.Claims
                .Where(claim => string.Equals(claim.Type, System.Security.Claims.ClaimTypes.Role, StringComparison.Ordinal))
                .SelectMany(claim => claim.Value.Split(','))
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct()
                .ToArray();
        }
    }
}
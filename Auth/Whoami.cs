﻿using System;
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

        [WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Step = 4.0)]
        [Api.HttpGet] //(MatchAllBodyParameters = false)]
        public static async Task<IHttpResponse> GetAsync(
                EastFive.Api.SessionToken security,
                IHttpRequest request,
                IAuthApplication application,

            [WorkflowVariable("Session", SessionPropertyName)]
            [WorkflowVariable2("Account", AccountPropertyName)]
            ContentTypeResponse<Whoami> onFound)
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
            request.TryParseJwt(out System.IdentityModel.Tokens.Jwt.JwtSecurityToken securityToken);
            var whoami = new Whoami()
            {
                session = security.sessionId.AsRef<Session>(),
                account = security.accountIdMaybe,
                name = await GetName(),
                securityToken = securityToken,
            };
            return onFound(whoami);
        }
    }
}
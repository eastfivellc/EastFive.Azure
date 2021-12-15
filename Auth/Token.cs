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
using EastFive.Web.Configuration;
using Newtonsoft.Json;
using EastFive.Api.Auth;
using System.Security.Claims;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController(
        Route = "Token",
        ContentType = "x-application/auth-token",
        ContentTypeVersion = "0.1")]
    public struct Token
    {
        public const string JwtPropertyName = "jwt";
        [ApiProperty(PropertyName = JwtPropertyName)]
        [JsonProperty(PropertyName = JwtPropertyName)]
        public string jwt;

        public const string IssuerPropertyName = "issuer";
        [ApiProperty(PropertyName = IssuerPropertyName)]
        [JsonProperty(PropertyName = IssuerPropertyName)]
        public string issuer;

        public const string ScopePropertyName = "scope";
        [JsonProperty(PropertyName = ScopePropertyName)]
        [ApiProperty(PropertyName = ScopePropertyName)]
        public string scope { get; set; }

        public const string ExpirationPropertyName = "expiration";
        [ApiProperty(PropertyName = ExpirationPropertyName)]
        [JsonProperty(PropertyName = ExpirationPropertyName)]
        public DateTime expiration;

        public const string SecretePropertyName = "secret";
        [ApiProperty(PropertyName = SecretePropertyName)]
        [JsonProperty(PropertyName = SecretePropertyName)]
        public string secret;

        [Api.HttpGet]
        public static IHttpResponse Get(
            ContentTypeResponse<Token> onFound)
        {
            var token = new Token()
            {
                issuer = Security.AppSettings.TokenIssuer
                    .ConfigurationString(
                        i => i,
                        (why) => string.Empty),
                scope = Security.AppSettings.TokenScope
                    .ConfigurationString(
                        i => i,
                        (why) => string.Empty),
                expiration = DateTime.UtcNow + TimeSpan.FromDays(365), // TODO: Load from config default
            };

            return onFound(token);
        }
    }
}
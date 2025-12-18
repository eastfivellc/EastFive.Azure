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
    [FunctionViewController(
        Route = "AuthLogin",
        ContentType = "x-application/auth-login",
        ContentTypeVersion = "0.1")]
    public struct Login
    {
        #region Properties

        public const string WhenPropertyName = "when";
        [ApiProperty(PropertyName = WhenPropertyName)]
        [JsonProperty(PropertyName = WhenPropertyName)]
        public DateTime when;

        public const string ActorPropertyName = "actor";
        [ApiProperty(PropertyName = ActorPropertyName)]
        [JsonProperty(PropertyName = ActorPropertyName)]
        public Guid? actorId;

        public const string MethodPropertyName = "method";
        [ApiProperty(PropertyName = MethodPropertyName)]
        [JsonProperty(PropertyName = MethodPropertyName)]
        public string method;

        public const string NamePropertyName = "name";
        [ApiProperty(PropertyName = NamePropertyName)]
        [JsonProperty(PropertyName = NamePropertyName)]
        public string name;

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        public IRef<Authorization> authorization;

        #endregion

        //[Api.HttpGet]
        //public static async Task<HttpResponseMessage> GetAsync(
        //        Api.Azure.AzureApplication application,
        //        RequestMessage<Claim> claims,
        //        Authorization auth,
        //    MultipartResponseAsync<Claim> onFound,
        //    UnauthorizedResponse onUnauthorized)
        //{
        //    if (!auth.accountIdMaybe.HasValue)
        //        return onUnauthorized();
        //    Expression<Func<Claim, bool>> allQuery = (claim) => true;
        //    var claimsForUser = claims
        //        .Where(claim => claim.actorId == auth.accountIdMaybe.Value)
        //        .StorageGet();
        //    return await onFound(claimsForUser);
        //}

        [Api.HttpGet]
        [SuperAdminClaim]
        public static IHttpResponse AllAsync(
                [QueryParameter(Name = "start_time")]DateTime startTime,
                [QueryParameter(Name = "end_time")]DateTime endTime,
                RequestMessage<Authorization> authorizations,
                IAuthApplication application,
                EastFive.Azure.Auth.SessionTokenMaybe securityMaybe,
            MultipartAsyncResponse<Login> onFound)
        {
            var methodLookups = application.LoginProviders
                .Select(
                    (loginProvider) =>
                    {
                        return loginProvider.Value.Id.PairWithValue(loginProvider.Value.Method);
                    })
                .ToDictionary();
            var results = authorizations
                .Where(auth => auth.lastModified <= endTime)
                .Where(auth => auth.lastModified >= startTime)
                .StorageGet()
                .Take(100)
                .Select(
                    async auth =>
                    {
                        var name = auth.accountIdMaybe.HasValue ?
                            await application.GetActorNameDetailsAsync(auth.accountIdMaybe.Value,
                                (a, b, c) => $"{a} {b}",
                                () => "")
                            :
                            "";
                        return new Login()
                        {
                            actorId = auth.accountIdMaybe,
                            authorization = auth.authorizationRef,
                            name = name,
                            method = methodLookups.ContainsKey(auth.Method.id) ?
                                methodLookups[auth.Method.id]
                                :
                                auth.Method.id.ToString(),
                            when = auth.lastModified,
                        };
                    })
                .Await();
            return onFound(results);
        }
    }
}
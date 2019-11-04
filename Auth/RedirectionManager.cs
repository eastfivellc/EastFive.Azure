using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BlackBarLabs.Api;
using BlackBarLabs.Extensions;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Api.Controllers;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [FunctionViewController6(
        Route = "RedirectionManager",
        Resource = typeof(RedirectionManager),
        ContentType = "x-application/auth-redirection-manager",
        ContentTypeVersion = "0.1")]
    public struct RedirectionManager : IReferenceable
    {
        [JsonIgnore]
        public Guid id => redirectionManagerRef.id;

        public const string RedirectionManagerId = "id";
        [ApiProperty(PropertyName = RedirectionManagerId)]
        [JsonProperty(PropertyName = RedirectionManagerId)]
        [RowKey]
        [StandardParititionKey]
        public IRef<RedirectionManager> redirectionManagerRef;

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        [Storage]
        public IRefOptional<Authorization> authorization { get; set; }

        public const string Message = "message";
        [ApiProperty(PropertyName = Message)]
        [JsonProperty(PropertyName = Message)]
        [Storage]
        public string message { get; set; }

        public const string When = "when";
        [ApiProperty(PropertyName = When)]
        [JsonProperty(PropertyName = When)]
        [Storage]
        public DateTime when { get; set; }

        public const string Redirection = "redirection";
        [ApiProperty(PropertyName = Redirection)]
        [JsonProperty(PropertyName = Redirection)]
        [Storage]
        public Uri redirection { get; set; }

        [Api.HttpGet]
        public async static Task<HttpResponseMessage> GetAllSecureAsync(
                [QueryParameter(Name = "ApiKeySecurity")]string apiSecurityKey,
                [QueryParameter(Name = "method")]IRef<Method> methodRef,
                [OptionalQueryParameter(Name = "successOnly")]bool successOnly,
                ApiSecurity apiSecurity,
                AzureApplication application,
                HttpRequestMessage request,
            ContentTypeResponse<RedirectionManager[]> onContent,
            UnauthorizedResponse onUnauthorized,
            ConfigurationFailureResponse onConfigFailure)
        {
            Expression<Func<Authorization, bool>> allQuery =
                (authorization) => true;
            var redirections = await allQuery
                .StorageQuery()
                .Where(authorization => !authorization.Method.IsDefaultOrNull())
                .Where(authorization => authorization.Method.id == methodRef.id)
                .Select<Authorization, Task<RedirectionManager?>>(
                    async authorization =>
                    {
                        RedirectionManager? Failure(string why)
                        {
                            if (successOnly)
                                return default(RedirectionManager?);

                            return new RedirectionManager
                            {
                                authorization = authorization.authorizationRef.Optional(),
                                message = why,
                                when = authorization.lastModified
                            };
                        }

                        return await await Method.ById<Task<RedirectionManager?>>(authorization.Method, application,
                            async method =>
                            {
                                return await method.ParseTokenAsync(authorization.parameters, application,
                                    (externalId, loginProvider) =>
                                    {
                                        return new RedirectionManager
                                        {
                                            when = authorization.lastModified,
                                            message = $"Ready:{externalId}",
                                            authorization = authorization.authorizationRef.Optional(),
                                            redirection = new Uri(
                                                request.RequestUri,
                                                $"/api/RedirectionManager?ApiKeySecurity={apiSecurityKey}&authorization={authorization.id}"),
                                        };
                                    },
                                    (why) => Failure(why));
                            },
                            () => Failure("Method no longer supported").AsTask());
                    })
                //Parallel()
                .Throttle()
                .SelectWhereHasValue()
                .OrderByDescendingAsync(item => item.when);
            return onContent(redirections.ToArray());
        }

        [Api.HttpGet]
        public static Task<HttpResponseMessage> GetRedirection(
                [QueryParameter(Name = "ApiKeySecurity")]string apiSecurityKey,
                [QueryParameter(Name = "authorization")]IRef<Authorization> authRef,
                ApiSecurity apiSecurity,
                AzureApplication application,
                HttpRequestMessage request,
            RedirectResponse onRedirection,
            GeneralFailureResponse onFailure,
            UnauthorizedResponse onUnauthorized,
            ConfigurationFailureResponse onConfigFailure)
        {
            return authRef.StorageUpdateAsync(
                async (authorization, saveAsync) =>
                {
                    var url = await await Method.ById(authorization.Method, application,
                        async method =>
                        {
                            return await await method.ParseTokenAsync(authorization.parameters, application,
                                async (externalId, loginProvider) =>
                                {
                                    var tag = "OpioidTool";
                                    return await EastFive.Web.Configuration.Settings.GetString($"AffirmHealth.PDMS.PingRedirect.{tag}.PingAuthName",
                                        async pingAuthName =>
                                        {
                                            return await EastFive.Web.Configuration.Settings.GetGuid($"AffirmHealth.PDMS.PingRedirect.{tag}.PingReportSetId",
                                                reportSetId =>
                                                {
                                                    var requestParams = authorization.parameters
                                                        .AppendIf("PingAuthName".PairWithValue(pingAuthName), !authorization.parameters.ContainsKey("PingAuthName"))
                                                        .AppendIf("ReportSetId".PairWithValue(reportSetId.ToString()), !authorization.parameters.ContainsKey("ReportSetId"))
                                                        .ToDictionary();

                                                    return Auth.Redirection.ProcessAsync(authorization, 
                                                            updatedAuth => 1.AsTask(),
                                                            method, externalId, requestParams,
                                                            Guid.NewGuid(), request.RequestUri, application, loginProvider,
                                                        (uri) =>
                                                        {
                                                            return uri;
                                                        },
                                                        (why) => default(Uri),
                                                        application.Telemetry);
                                                },
                                                why =>
                                                {
                                                    return default(Uri).AsTask();
                                                });
                                        },
                                        why =>
                                        {
                                            return default(Uri).AsTask();
                                        });
                                },
                                (why) => default(Uri).AsTask());
                        },
                        () => default(Uri).AsTask());
                    if (url.IsDefaultOrNull())
                        return onFailure("Failed to determine correct redirect URL");

                    authorization.expired = false;
                    await saveAsync(authorization);
                    return onRedirection(url);
                });
        }

        [Api.HttpGet]
        public static async Task<HttpResponseMessage> GetAllSecureAsync(
                [QueryParameter(Name = "ApiKeySecurity")]string apiSecurityKey,
                [QueryParameter(Name = "authorization")]IRef<Authorization> authorizationRef,
                ApiSecurity apiSecurity,
                AzureApplication application,
                HttpRequestMessage request,
            MultipartResponseAsync<Authorization> onContent,
            RedirectResponse onSuccess,
            NotFoundResponse onNotFound,
            ForbiddenResponse onFailure)
        {
            return await await authorizationRef.StorageGetAsync(
                async authorization =>
                {
                    return await await Method.ById(authorization.Method, application,
                        async method =>
                        {
                            return await await method.ParseTokenAsync(authorization.parameters, application,
                                (externalId, loginProvider) =>
                                {
                                    return Auth.Redirection.ProcessAsync(authorization,
                                            async updatedAuth =>
                                            {

                                            }, method, externalId, authorization.parameters,
                                            Guid.NewGuid(), request.RequestUri, application, loginProvider,
                                        (uri) => onSuccess(uri),
                                        (why) => onFailure().AddReason(why),
                                        application.Telemetry);
                                },
                                why => onFailure().AddReason(why).AsTask());
                        },
                        () => onFailure().AddReason("Method no longer supported").AsTask());
                },
                () => onNotFound().AsTask());
        }
    }
}

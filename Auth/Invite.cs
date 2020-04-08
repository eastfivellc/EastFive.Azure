using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Routing;
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
using EastFive.Security;
using EastFive.Security.SessionServer;
using EastFive.Security.SessionServer.Exceptions;
using Microsoft.ApplicationInsights;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController(
        Route = "Invite",
        Resource = typeof(Invite),
        ContentType = "x-application/auth-invite",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    public struct Invite : IReferenceable
    {
        public Guid id => inviteRef.id;

        public const string InvitePropertyName = "id";
        [ApiProperty(PropertyName = InvitePropertyName)]
        [JsonProperty(PropertyName = InvitePropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<Invite> inviteRef;

        [Storage]
        public IRef<Authorization> authorizationRef { get; set; }

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        public Authorization authorization { get; set; }

        [Storage]
        public IRef<AccountMapping> accountMappingRef { get; set; }

        public const string AccountMappingPropertyName = "account_mapping";
        [ApiProperty(PropertyName = AccountMappingPropertyName)]
        [JsonProperty(PropertyName = AccountMappingPropertyName)]
        public AccountMapping accountMapping { get; set; }


        //[Api.HttpGet] //(MatchAllBodyParameters = false)]
        //public static Task<HttpResponseMessage> GetAsync(
        //        [QueryParameter(CheckFileName = true, Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
        //        Api.Azure.AzureApplication application, UrlHelper urlHelper,
        //    ContentTypeResponse<Authorization> onFound,
        //    NotFoundResponse onNotFound)
        //{
        //    return authorizationRef.StorageGetAsync(
        //        (authorization) => onFound(authorization),
        //        () => onNotFound());
        //}

        //[Api.HttpPost] //(MatchAllBodyParameters = false)]
        //public async static Task<HttpResponseMessage> CreateAsync(
        //        [Property(Name = AuthorizationIdPropertyName)]Guid authorizationId,
        //        [Property(Name = MethodPropertyName)]IRef<Method> method,
        //        [Property(Name = LocationAuthorizationReturnPropertyName)]Uri LocationAuthenticationReturn,
        //        [Resource]Authorization authorization,
        //        Api.Azure.AzureApplication application, UrlHelper urlHelper,
        //    CreatedBodyResponse<Authorization> onCreated,
        //    AlreadyExistsResponse onAlreadyExists,
        //    ReferencedDocumentDoesNotExistsResponse<Method> onAuthenticationDoesNotExist)
        //{
        //    return await await Auth.Method.ById(method, application,
        //        async (authentication) =>
        //        {
        //            //var authorizationIdSecure = authentication.authenticationId;
        //            authorization.LocationAuthentication = await authentication.GetLoginUrlAsync(
        //                application, urlHelper, authorizationId);

        //            return await authorization.StorageCreateAsync(
        //                createdId => onCreated(authorization),
        //                () => onAlreadyExists());
        //        },
        //        () => onAuthenticationDoesNotExist().AsTask());
        //}

        //[Api.HttpPatch] //(MatchAllBodyParameters = false)]
        //public async static Task<HttpResponseMessage> UpdateAsync(
        //        [Property(Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
        //        [Property(Name = LocationLogoutReturnPropertyName)]Uri locationLogoutReturn,
        //    NoContentResponse onUpdated,
        //    AlreadyExistsResponse onNotFound)
        //{
        //    return await authorizationRef.StorageUpdateAsync(
        //        async (authorization, saveAsync) =>
        //        {
        //            authorization.LocationLogoutReturn = locationLogoutReturn;
        //            await saveAsync(authorization);
        //            return onUpdated();
        //        },
        //        () => onNotFound());
        //}

    }
}
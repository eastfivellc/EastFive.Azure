﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Http.Routing;
using BlackBarLabs.Api;
using BlackBarLabs.Persistence.Azure.Attributes;
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
    [FunctionViewController4(
        Route = "XAuthorization",
        Resource = typeof(Authorization),
        ContentType = "x-application/auth-authorization",
        ContentTypeVersion = "0.1")]
    [StorageResource(typeof(StandardPartitionKeyGenerator))]
    [StorageTable]
    public struct Authorization : IReferenceable
    {
        public Guid id => authorizationRef.id;

        public const string AuthorizationIdPropertyName = "id";
        [ApiProperty(PropertyName = AuthorizationIdPropertyName)]
        [JsonProperty(PropertyName = AuthorizationIdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        public IRef<Authorization> authorizationRef;

        [LastModified]
        public DateTime lastModified;
        
        public const string MethodPropertyName = "method";
        [ApiProperty(PropertyName = MethodPropertyName)]
        [JsonProperty(PropertyName = MethodPropertyName)]
        [Storage(Name = MethodPropertyName)]
        public IRef<Method> Method { get; set; }

        public const string LocationAuthorizationPropertyName = "location_authentication";
        [ApiProperty(PropertyName = LocationAuthorizationPropertyName)]
        [JsonProperty(PropertyName = LocationAuthorizationPropertyName)]
        [Storage(Name = LocationAuthorizationPropertyName)]
        public Uri LocationAuthentication { get; set; }

        public const string LocationAuthorizationReturnPropertyName = "location_authentication_return";
        [ApiProperty(PropertyName = LocationAuthorizationReturnPropertyName)]
        [JsonProperty(PropertyName = LocationAuthorizationReturnPropertyName)]
        [Storage(Name = LocationAuthorizationReturnPropertyName)]
        public Uri LocationAuthenticationReturn { get; set; }

        public const string LocationLogoutPropertyName = "location_logout";
        [ApiProperty(PropertyName = LocationLogoutPropertyName)]
        [JsonProperty(PropertyName = LocationLogoutPropertyName)]
        [Storage(Name = LocationLogoutPropertyName)]
        public Uri LocationLogout { get; set; }

        public const string LocationLogoutReturnPropertyName = "location_logout_return";
        [ApiProperty(PropertyName = LocationLogoutReturnPropertyName)]
        [JsonProperty(PropertyName = LocationLogoutReturnPropertyName)]
        [Storage(Name = LocationLogoutReturnPropertyName)]
        public Uri LocationLogoutReturn { get; set; }

        public const string ParametersPropertyName = "parameters";
        [JsonIgnore]
        [Storage(Name = ParametersPropertyName)]
        public IDictionary<string, string> parameters;

        [Storage]
        public bool authorized;

        [Storage]
        public bool expired;

        [Storage]
        public Guid? accountIdMaybe;

        [Storage]
        public DateTime? deleted;

        [Api.HttpGet]
        public static Task<HttpResponseMessage> GetAsync(
                [QueryParameter(CheckFileName = true, Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
                Api.Azure.AzureApplication application, UrlHelper urlHelper,
                EastFive.Api.Controllers.SessionToken? securityMaybe,
            ContentTypeResponse<Authorization> onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized,
            BadRequestResponse onBadRequest)
        {
            return authorizationRef.StorageUpdateAsync(
                async (authorization, saveAsync) =>
                {
                    if (authorization.deleted.HasValue)
                        return onNotFound();
                    if (authorization.authorized)
                        authorization.LocationAuthentication = default(Uri);
                    if (!securityMaybe.HasValue)
                    {
                        if (authorization.authorized)
                        {
                            if (authorization.expired)
                                return onBadRequest();
                            if (authorization.lastModified - DateTime.UtcNow > TimeSpan.FromMinutes(1.0))
                                return onBadRequest();
                            authorization.expired = true;
                            await saveAsync(authorization);
                            return onFound(authorization);
                        }
                    }
                    return onFound(authorization);
                },
                () => onNotFound());
        }

        [Api.HttpPost]
        public async static Task<HttpResponseMessage> CreateAsync(
                [Property(Name = AuthorizationIdPropertyName)]Guid authorizationId,
                [Property(Name = MethodPropertyName)]IRef<Method> method,
                [Property(Name = LocationAuthorizationReturnPropertyName)]Uri LocationAuthenticationReturn,
                [Resource]Authorization authorization,
                Api.Azure.AzureApplication application, UrlHelper urlHelper,
            CreatedBodyResponse<Authorization> onCreated,
            AlreadyExistsResponse onAlreadyExists,
            ReferencedDocumentDoesNotExistsResponse<Method> onAuthenticationDoesNotExist)
        {
            return await await Auth.Method.ById(method, application,
                async (authentication) =>
                {
                    //var authorizationIdSecure = authentication.authenticationId;
                    authorization.LocationAuthentication = await authentication.GetLoginUrlAsync(
                        application, urlHelper, authorizationId);

                    return await authorization.StorageCreateAsync(
                        createdId => onCreated(authorization),
                        () => onAlreadyExists());
                },
                () => onAuthenticationDoesNotExist().AsTask());
        }

        [Api.HttpPatch] //(MatchAllBodyParameters = false)]
        public async static Task<HttpResponseMessage> UpdateAsync(
                [UpdateId(Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
                [Property(Name = LocationLogoutReturnPropertyName)]Uri locationLogoutReturn,
                EastFive.Api.Controllers.SessionToken? securityMaybe,
            NoContentResponse onUpdated,
            AlreadyExistsResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return await authorizationRef.StorageUpdateAsync(
                async (authorization, saveAsync) =>
                {
                    if (authorization.deleted.HasValue)
                        return onNotFound();
                    if (authorization.authorized)
                    {
                        authorization.LocationAuthentication = default(Uri);
                        if (!securityMaybe.HasValue)
                            return onUnauthorized();
                    }
                    authorization.LocationLogoutReturn = locationLogoutReturn;
                    await saveAsync(authorization);
                    return onUpdated();
                },
                () => onNotFound());
        }

        [HttpDelete]
        public static async Task<HttpResponseMessage> DeleteAsync(
                [UpdateId(Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
                Context context, UrlHelper urlHelper, AzureApplication application,
            NoContentResponse onLogoutComplete,
            AcceptedBodyResponse onExternalSessionActive,
            NotFoundResponse onNotFound,
            GeneralFailureResponse onFailure)
        {
            return await authorizationRef.StorageUpdateAsync(
                async (authorizationToDelete, updateAsync) =>
                {
                    authorizationToDelete.deleted = DateTime.UtcNow;
                    if (!authorizationToDelete.authorized)
                        return onLogoutComplete().AddReason("Deleted");

                    var locationLogout = await await Auth.Method.ById(authorizationToDelete.Method, application,
                        (authentication) =>
                        {
                            return authentication.GetLogoutUrlAsync(
                                application, urlHelper, authorizationRef.id);
                        },
                        () => default(Uri).AsTask());
                    authorizationToDelete.LocationLogout = locationLogout;
                    await updateAsync(authorizationToDelete);

                    bool NoRedirectRequired()
                    {
                        if (locationLogout.IsDefaultOrNull())
                            return true;
                        if (!locationLogout.IsAbsoluteUri)
                            return true;
                        if (locationLogout.AbsoluteUri.IsNullOrWhiteSpace())
                            return true;
                        return false;
                    }

                    if (NoRedirectRequired())
                        return onLogoutComplete().AddReason("Logout Complete");

                    return onExternalSessionActive(authorizationToDelete, "application/json")
                        .AddReason($"External session removal required:{locationLogout.AbsoluteUri}");
                },
                () => onNotFound());
        }

        public async Task<TResult> ParseCredentailParameters<TResult>(
                Api.Azure.AzureApplication application,
            Func<string, IProvideLogin, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var parameters = this.parameters;
            return await await Auth.Method.ById(this.Method, application, // TODO: Cleanup 
                (method) =>
                {
                    return application.LoginProviders
                        .SelectValues()
                        .Where(loginProvider => loginProvider.Method == method.name)
                        .FirstAsync(
                            (loginProvider) =>
                            {
                                return loginProvider.ParseCredentailParameters(parameters,
                                    (string userKey, Guid? authorizationId, Guid? deprecatedId) =>
                                    {
                                        return onSuccess(userKey, loginProvider);
                                    },
                                    (why) => onFailure(why));
                            },
                            () => onFailure("Method does not match any existing authentication."));
                },
                () => onFailure("Authentication not found").AsTask());
        }
    }
}
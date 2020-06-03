﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using EastFive.Security.SessionServer.Exceptions;
using Microsoft.ApplicationInsights;
using EastFive.Extensions;
using EastFive.Security.SessionServer;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Persistence;
using Newtonsoft.Json;
using BlackBarLabs.Persistence.Azure.Attributes;

namespace EastFive.Azure.Auth
{
    [StorageResource(typeof(StandardPartitionKeyGenerator))]
    [StorageTable]
    public class Redirection : IReferenceable
    {
        [JsonIgnore]
        public Guid id => webDataRef.id;

        [JsonIgnore]
        [RowKey]
        [StandardParititionKey]
        public IRef<Redirection> webDataRef;

        [Storage]
        public IRef<Method> method;

        [Storage]
        public IDictionary<string, string> values;

        [Storage]
        public Uri redirectedFrom;

        public static async Task<TResult> ProcessRequestAsync<TResult>(
                EastFive.Azure.Auth.Method method,
                IDictionary<string, string> values,
                AzureApplication application, HttpRequestMessage request,
                System.Web.Http.Routing.UrlHelper urlHelper,
            Func<Uri, TResult> onRedirect,
            Func<string, TResult> onBadCredentials,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onFailure)
        {
            var authorizationRequestManager = application.AuthorizationRequestManager;

            var telemetry = application.Telemetry;
            telemetry.TrackEvent($"ResponseController.ProcessRequestAsync - Requesting credential manager.");

            var requestId = Guid.NewGuid();
            var redirection = new Redirection
            {
                webDataRef = requestId.AsRef<Redirection>(),
                method = method.authenticationId,
                values = values,
                redirectedFrom = request.Headers.Referrer,
            };

            return await await redirection.StorageCreateAsync(
                discard =>
                {
                    var baseUri = request.RequestUri;
                    return AuthenticationAsync(
                            method, values, baseUri, application,
                            RefOptional<Authorization>.Empty(),
                        onRedirect,
                        () => onFailure("Authorization not found"),
                        onCouldNotConnect,
                        onFailure);
                },
                () => onFailure("GUID NOT UNIQUE").AsTask());
        }

        public async static Task<TResult> AuthenticationAsync<TResult>(
                EastFive.Azure.Auth.Method authentication, IDictionary<string, string> values, Uri baseUri,
                AzureApplication application,
                IRefOptional<Authorization> authorizationRefToCreate,
            Func<Uri, TResult> onRedirect,
            Func<TResult> onAuthorizationNotFound,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onGeneralFailure)
        {
            var authorizationRequestManager = application.AuthorizationRequestManager;
            var telemetry = application.Telemetry;
            return await await authentication.RedeemTokenAsync(values, application,
                async (externalAccountKey, authorizationRefMaybe, loginProvider, extraParams) =>
                {
                    #region Handle case where there was a direct link

                    if (!authorizationRefMaybe.HasValue)
                    {
                        var authorization = new Authorization
                        {
                            authorizationRef = authorizationRefToCreate.HasValue?
                                authorizationRefToCreate.Ref
                                :
                                new Ref<Authorization>(Security.SecureGuid.Generate()),
                            Method = authentication.authenticationId,
                            parameters = extraParams,
                            authorized = true,
                        };
                        return await ProcessAsync(authorization,
                                async (authorizationUpdated) =>
                                {
                                    bool created = await authorizationUpdated.StorageCreateAsync(
                                        authIdDiscard =>
                                        {
                                            return true;
                                        },
                                        () => throw new Exception("Secure guid not unique"));
                                },
                                authentication, externalAccountKey, extraParams,
                                baseUri, application, loginProvider,
                            onRedirect,
                            onGeneralFailure,
                                telemetry);
                    }

                    #endregion

                    var authorizationRef = authorizationRefMaybe.Ref;
                    return await authorizationRef.StorageUpdateAsync(
                        async (authorization, saveAsync) =>
                        {
                            return await ProcessAsync(authorization,
                                    saveAsync,
                                    authentication, externalAccountKey, extraParams,
                                    baseUri, application, loginProvider,
                                onRedirect,
                                onGeneralFailure,
                                    telemetry);
                        },
                        () =>
                        {
                            return onAuthorizationNotFound();
                        });
                },
                (authorizationRef, extraparams) => onGeneralFailure("Cannot use logout to authenticate").AsTask(),
                (why) => onCouldNotConnect(why).AsTask(),
                (why) => onGeneralFailure(why).AsTask());
        }

        public static async Task<TResult> ProcessAsync<TResult>(Authorization authorization,
                Func<Authorization, Task> saveAsync,
                Method authentication, string externalAccountKey,
                IDictionary<string, string> extraParams,
                Uri baseUri, AzureApplication application, IProvideLogin loginProvider,
            Func<Uri, TResult> onRedirect,
            Func<string, TResult> onGeneralFailure,
            TelemetryClient telemetry)
        {
            authorization.authorized = true;
            authorization.LocationAuthentication = null;

            var result = await await AccountMapping.FindByMethodAndKeyAsync(authentication.authenticationId, externalAccountKey,
                    authorization,

                // Found
                async internalAccountId =>
                {
                    authorization.parameters = extraParams;
                    authorization.accountIdMaybe = internalAccountId;
                    await saveAsync(authorization);
                    return await CreateLoginResponseAsync(
                                internalAccountId, extraParams,
                                authentication, authorization,
                                baseUri,
                                application, loginProvider,
                            onRedirect,
                            onGeneralFailure,
                            telemetry);
                },
                OnNotFound);
            return result;

            async Task<TResult> OnNotFound()
            {
                return await await UnmappedCredentialAsync(externalAccountKey, extraParams,
                            authentication, authorization,
                            loginProvider, application, baseUri,

                        // Create mapping
                        async (internalAccountId) =>
                        {
                            authorization.parameters = extraParams;
                            authorization.accountIdMaybe = internalAccountId;
                            await saveAsync(authorization);
                            return await await AccountMapping.CreateByMethodAndKeyAsync(
                                    authorization, externalAccountKey, internalAccountId,
                                () =>
                                {
                                    return CreateLoginResponseAsync(
                                            internalAccountId, extraParams,
                                            authentication, authorization,
                                            baseUri,
                                            application, loginProvider,
                                        onRedirect,
                                        onGeneralFailure,
                                        telemetry);
                                },
                                () =>
                                {
                                    return CreateLoginResponseAsync(
                                            internalAccountId, extraParams,
                                            authentication, authorization,
                                            baseUri,
                                            application, loginProvider,
                                        onRedirect,
                                        onGeneralFailure,
                                        telemetry);
                                });
                        },

                        // Allow self serve
                        async () =>
                        {
                            authorization.parameters = extraParams;
                            await saveAsync(authorization);
                            return await CreateLoginResponseAsync(
                                    default(Guid?), extraParams,
                                    authentication, authorization,
                                    baseUri,
                                    application, loginProvider,
                                onRedirect,
                                onGeneralFailure,
                                    telemetry);
                        },

                        // Intercept process
                        async (interceptionUrl) =>
                        {
                            authorization.parameters = extraParams;
                            await saveAsync(authorization);
                            return onRedirect(interceptionUrl);
                        },

                        // Failure
                        async (why) =>
                        {
                            // Save params so they can be used later
                            authorization.parameters = extraParams;
                            await saveAsync(authorization);
                            return onGeneralFailure(why);
                        },
                        telemetry);
            }
        }

        public static async Task<TResult> MapAccountAsync<TResult>(Authorization authorization,
            Guid internalAccountId, string externalAccountKey,
            Uri baseUri,
            AzureApplication application,
            Func<Uri, TResult> onRedirect,
            Func<string, TResult> onFailure,
            TelemetryClient telemetry)
        {
            return await await AccountMapping.CreateByMethodAndKeyAsync(authorization, externalAccountKey,
                internalAccountId,
                ProcessAsync,
                ProcessAsync);

            async Task<TResult> ProcessAsync()
            {
                return await await Method.ById(authorization.Method, application,
                        async method =>
                        {
                            return await await method.GetLoginProviderAsync(application,
                                (loginProviderMethodName, loginProvider) =>
                                {
                                    return CreateLoginResponseAsync(
                                           internalAccountId, authorization.parameters,
                                           method, authorization,
                                           baseUri,
                                           application, loginProvider,
                                       (url) => onRedirect(url),
                                       onFailure,
                                       telemetry);
                                },
                                () =>
                                {
                                    return onFailure("Login provider is no longer supported by the system").AsTask();
                                });
                        },
                        () => onFailure("Method no longer suppored.").AsTask());
            }
        }

        private static Task<TResult> CreateLoginResponseAsync<TResult>(
                Guid? accountId, IDictionary<string, string> extraParams,
                Method method, Authorization authorization,
                Uri baseUri,
                AzureApplication application, IProvideAuthorization authorizationProvider,
            Func<Uri, TResult> onRedirect,
            Func<string, TResult> onBadResponse,
            TelemetryClient telemetry)
        {
            return application.GetRedirectUriAsync( 
                    accountId, extraParams,
                    method, authorization,
                    baseUri,
                    authorizationProvider,
                (redirectUrlSelected) =>
                {
                    telemetry.TrackEvent($"CreateResponse - redirectUrlSelected1: {redirectUrlSelected.AbsolutePath}");
                    telemetry.TrackEvent($"CreateResponse - redirectUrlSelected2: {redirectUrlSelected.AbsoluteUri}");
                    return onRedirect(redirectUrlSelected);
                },
                (paramName, why) =>
                {
                    var message = $"Invalid parameter while completing login: {paramName} - {why}";
                    telemetry.TrackException(new ResponseException(message));
                    return onBadResponse(message);
                },
                (why) =>
                {
                    var message = $"General failure while completing login: {why}";
                    telemetry.TrackException(new ResponseException(message));
                    return onBadResponse(message);
                });
        }

        private static Task<TResult> UnmappedCredentialAsync<TResult>(
                string subject, IDictionary<string, string> extraParams,
                EastFive.Azure.Auth.Method authentication, Authorization authorization,
                IProvideAuthorization authorizationProvider, 
                AzureApplication application,
                Uri baseUri,
            Func<Guid, TResult> createMapping,
            Func<TResult> onAllowSelfServe,
            Func<Uri, TResult> onInterceptProcess,
            Func<string, TResult> onFailure,
            TelemetryClient telemetry)
        {
            return application.OnUnmappedUserAsync(subject, extraParams,
                    authentication, authorization, authorizationProvider, baseUri,
                (accountId) => createMapping(accountId),
                () => onAllowSelfServe(),
                (callback) => onInterceptProcess(callback),
                () =>
                {
                    var message = "Token is not connected to a user in this system";
                    telemetry.TrackException(new ResponseException(message));
                    return onFailure(message);
                });
        }
    }
}

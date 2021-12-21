using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Security.SessionServer.Exceptions;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Persistence;
using EastFive.Api;
using EastFive.Web.Configuration;
using EastFive.Api.Azure.Credentials;
using EastFive.Linq;

namespace EastFive.Azure.Auth
{
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

        public static async Task<IHttpResponse> ProcessRequestAsync(
                EastFive.Azure.Auth.Method method,
                IDictionary<string, string> values,
                IAzureApplication application, 
                IHttpRequest request,
                IInvokeApplication endpoints,
                IProvideUrl urlHelper,
            Func<Uri, Guid?, IHttpResponse> onRedirect,
            Func<string, IHttpResponse> onAuthorizationnotFound,
            Func<string, IHttpResponse> onCouldNotConnect,
            Func<string, IHttpResponse> onFailure)
        {
            //var authorizationRequestManager = application.AuthorizationRequestManager;

            var telemetry = application.Telemetry;
            telemetry.TrackEvent($"ResponseController.ProcessRequestAsync - Requesting credential manager.");

            var requestId = Guid.NewGuid();
            request.TryGetReferer(out Uri referer);
            var redirection = new Redirection
            {
                webDataRef = requestId.AsRef<Redirection>(),
                method = method.authenticationId,
                values = values,
                redirectedFrom = referer,
            };

            return await await redirection.StorageCreateAsync(
                discard =>
                {
                    return AppSettings.PauseRedirections.ConfigurationBoolean(
                        async pauseRedirections =>
                        {
                            if (pauseRedirections)
                                request.CreateResponse(System.Net.HttpStatusCode.OK, requestId);
                            return await ContinueAsync();
                        },
                        why => ContinueAsync(),
                        ContinueAsync);
                    
                    Task<IHttpResponse> ContinueAsync()
                    {
                        var baseUri = request.RequestUri;
                        return AuthenticationAsync(
                                method, values, application, request, endpoints,
                                request.RequestUri,
                                RefOptional<Authorization>.Empty(),
                            (uri, accountIdMaybe, modifier) =>
                            {
                                var response = onRedirect(uri, accountIdMaybe);
                                return modifier(response);
                            },
                            () => onAuthorizationnotFound("Authorization not found"),
                            onCouldNotConnect,
                            onFailure);
                    }
                },
                () => onFailure("GUID NOT UNIQUE").AsTask());
        }

        public async static Task<TResult> AuthenticationAsync<TResult>(
                EastFive.Azure.Auth.Method authentication, IDictionary<string, string> values,
                IAzureApplication application, IHttpRequest request,
                IInvokeApplication endpoints, Uri baseUri,
                IRefOptional<Authorization> authorizationRefToCreate,
            Func<Uri, Guid?, Func<IHttpResponse, IHttpResponse>, TResult> onRedirect,
            Func<TResult> onAuthorizationNotFound,
            Func<string, TResult> onCouldNotConnect,
            Func<string, TResult> onGeneralFailure)
        {
            var telemetry = application.Telemetry;
            return await await authentication.RedeemTokenAsync(values, application,
                async (externalAccountKey, authorizationRefMaybe, loginProvider, extraParams) =>
                {
                    #region Handle case where there was a direct link or a POST

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
                                        () =>
                                        {
                                            //if(authorizationRefToCreate.HasValueNotNull())
                                            //    throw new Exception("Authorization to create already exists.");
                                            //throw new Exception("Duplicated update from ProcessAsync.");
                                            return false;
                                        });
                                },
                                authentication, externalAccountKey, extraParams,
                                application, request, endpoints, loginProvider, baseUri,
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
                                    application, request, endpoints, loginProvider, baseUri,
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
                Method authenticationMethod, string externalAccountKey,
                IDictionary<string, string> extraParams,
                IAzureApplication application, IHttpRequest request,
                IInvokeApplication endpoints, IProvideLogin loginProvider,
                Uri baseUri,
            Func<Uri, Guid?, Func<IHttpResponse, IHttpResponse>, TResult> onRedirect,
            Func<string, TResult> onGeneralFailure,
            TelemetryClient telemetry)
        {
            return await await AuthorizeWithAccountAsync(
                    authorization, saveAsync,
                    authenticationMethod, externalAccountKey, extraParams,
                    application, request, loginProvider, baseUri,

                onAccountLocated: async (internalAccountId) =>
                {
                    return await await CreateLoginResponseAsync(
                                internalAccountId, extraParams,
                                authenticationMethod, authorization,
                                application, request, endpoints, baseUri, loginProvider,
                            async (url, modifier) =>
                            {
                                await saveAsync(authorization);
                                return onRedirect(url, internalAccountId, modifier);
                            },
                            async (why) =>
                            {
                                await saveAsync(authorization);
                                return onGeneralFailure(why);
                            },
                                telemetry);
                },

                onInterupted:(interceptionUrl, internalAccountId) =>
                {
                    return onRedirect(interceptionUrl, internalAccountId, m => m).AsTask();
                },

                onGeneralFailure:(why) =>
                {
                    return onGeneralFailure(why).AsTask();
                },
                    telemetry: telemetry);
        }

        public static async Task<TResult> AuthorizeWithAccountAsync<TResult>(Authorization authorization,
                Func<Authorization, Task> saveAsync,
                Method authenticationMethod, string externalAccountKey,
                IDictionary<string, string> extraParams,
                IAzureApplication application, IHttpRequest request,
                IProvideLogin loginProvider,
                Uri baseUri,
            Func<Guid, TResult> onAccountLocated,
            Func<Uri, Guid, TResult> onInterupted,
            Func<string, TResult> onGeneralFailure,
            TelemetryClient telemetry)
        {
            return await await IdentifyAccountAsync(authorization,
                authenticationMethod, externalAccountKey, extraParams,
                application, loginProvider, request,

                onLocated: async (internalAccountId, claims) =>
                {
                    authorization.parameters = extraParams;
                    authorization.accountIdMaybe = internalAccountId;
                    authorization.authorized = true;
                    authorization.claims = claims;
                    await saveAsync(authorization);
                    return onAccountLocated(internalAccountId);
                },

                onInterupted: async (interceptionUrl, internalAccountId, claims) =>
                {
                    authorization.parameters = extraParams;
                    authorization.accountIdMaybe = internalAccountId;
                    authorization.authorized = true;
                    authorization.claims = claims;
                    await saveAsync(authorization);
                    return onInterupted(interceptionUrl, internalAccountId);
                },

                onGeneralFailure: async (why) =>
                {
                    // Save params so they can be used later
                    authorization.parameters = extraParams;
                    await saveAsync(authorization);
                    return onGeneralFailure(why);
                },
                    telemetry: telemetry);
        }

        public static async Task<TResult> IdentifyAccountAsync<TResult>(Authorization authorization,
                Method authenticationMethod, string externalAccountKey,
                IDictionary<string, string> extraParams,
                IAzureApplication application,
                IProvideLogin loginProvider,
                IHttpRequest request,
            Func<Guid, IDictionary<string, string>, TResult> onLocated,
            Func<Uri, Guid, IDictionary<string, string>, TResult> onInterupted,
            Func<string, TResult> onGeneralFailure,
                TelemetryClient telemetry)
        {
            if (!(application is IProvideAccountInformation))
            {
                authorization.authorized = true;
                authorization.LocationAuthentication = null;
                // return await OnLegacy();
                return onGeneralFailure($"{application.GetType().FullName} does not implement {nameof(IProvideAccountInformation)}.");
            }
            var accountInfoProvider = (IProvideAccountInformation)application;

            return await accountInfoProvider.FindOrCreateAccountByMethodAndKeyAsync(
                    authenticationMethod, externalAccountKey,
                    authorization, extraParams,
                    loginProvider, request,
                    PopulateAccount,
                onAccountReady: (internalAccountId, claims) =>
                {
                    return onLocated(internalAccountId, claims);
                },
                onInterceptProcess:(url, internalAccountId, claims) =>
                {
                    return onInterupted(url, internalAccountId, claims);
                },
                onReject:onGeneralFailure);

            IAccount PopulateAccount(IAccount account)
            {
                account.AccountLinks = new AccountLinks
                {
                    accountLinks = new AccountLink[]
                    {
                        new AccountLink()
                        {
                            externalAccountKey = externalAccountKey,
                            method = authenticationMethod.authenticationId,
                        }
                    }
                };

                if (!(loginProvider is IProvideClaims))
                    return account;
                var claimProvider = (IProvideClaims)loginProvider;

                return account.GetType()
                    .GetPropertyAndFieldsWithAttributesInterface<PopulatedByAuthorizationClaim>()
                    .Aggregate(account,
                        (accountToUpdate, tpl) =>
                        {
                            var (member, populationAttr) = tpl;
                            return populationAttr.PopulateValue(accountToUpdate, member,
                                (string claimType, out string claimValue) =>
                                    claimProvider.TryGetStandardClaimValue(claimType, extraParams, out claimValue));
                        });
            }

            //Task<TResult> CreateAccountAsync()
            //{
            //    return accountInfoProvider.CreateUnpopulatedAccountAsync(
            //            authenticationMethod, externalAccountKey, application,
            //        onNeedsPopulated: async (account, saveAsync) =>
            //        {
            //            account.AccountLinks = account.AccountLinks
            //                .AppendCredentials(authenticationMethod, externalAccountKey);

            //            var populatedAccount = PopulateAccount();
            //            await saveAsync(populatedAccount);

            //            return onCreated(populatedAccount.id);

                        
            //        },
            //        onInterupted: (uri) => onInterupted(uri),
            //        onNotCreated: (string why) => onGeneralFailure(why),
            //        onNoEffect: () => throw new Exception("IProvideAccountInformation created account but did not want to populate it."));
            //}

            //Task<TResult> OnLegacy() => LegacyAccountMappingAsync(authorization,
            //    authenticationMethod, externalAccountKey,
            //    extraParams, application, loginProvider, baseUri,
            //    onLocated: onLocated,
            //    onCreated: onCreated,
            //    onSelfServe: onSelfServe,
            //    onInterupted: onInterupted,
            //    onGeneralFailure: onGeneralFailure,
            //        telemetry: telemetry);
        }

        //public static async Task<TResult> LegacyAccountMappingAsync<TResult>(Authorization authorization,
        //        Method authenticationMethod, string externalAccountKey,
        //        IDictionary<string, string> extraParams,
        //        IAzureApplication application,
        //        IProvideLogin loginProvider,
        //        Uri baseUri,
        //    Func<Guid, Func<bool, Task<TResult>>, TResult> onLocated,
        //    Func<Guid, TResult> onCreated,
        //    Func<TResult> onSelfServe,
        //    Func<Uri, TResult> onInterupted,
        //    Func<string, TResult> onGeneralFailure,
        //        TelemetryClient telemetry)
        //{
        //    return await await AccountMapping.FindByMethodAndKeyAsync(
        //            authenticationMethod.authenticationId, externalAccountKey,
        //            authorization,
        //            // Found
        //        internalAccountId =>
        //        {
        //            return onLocated(
        //                    internalAccountId,
        //                    (isAccountInvalid) => OnNotFound(isAccountInvalid))
        //                .AsTask();
        //        },
        //        () => OnNotFound(false));

        //    async Task<TResult> OnNotFound(bool isAccountInvalid)
        //    {
        //        return await await UnmappedCredentialAsync(externalAccountKey, extraParams,
        //                    authenticationMethod, authorization,
        //                    loginProvider, application, baseUri,

        //                // Create mapping
        //                (internalAccountId) =>
        //                {
        //                    return AccountMapping.CreateByMethodAndKeyAsync(
        //                            authorization, externalAccountKey, internalAccountId,
        //                        () =>
        //                        {
        //                            return onCreated(internalAccountId);
        //                        },
        //                        () =>
        //                        {
        //                            return onLocated(internalAccountId, default);
        //                        },
        //                            isAccountInvalid);
        //                },

        //                // Allow self serve
        //                () =>
        //                {
        //                    return onSelfServe().AsTask();
        //                },

        //                // Intercept process
        //                (interceptionUrl) =>
        //                {
        //                    return onInterupted(interceptionUrl).AsTask();
        //                },

        //                // Failure
        //                (why) =>
        //                {
        //                    return onGeneralFailure(why).AsTask();
        //                },
        //                telemetry);
        //    }
        //}

        private static Task<TResult> CreateLoginResponseAsync<TResult>(
                Guid? accountId, IDictionary<string, string> extraParams,
                Method method, Authorization authorization,
                IAuthApplication application,
                IHttpRequest request, IInvokeApplication endpoints,
                Uri baseUrl,
                IProvideAuthorization authorizationProvider,
            Func<Uri, Func<IHttpResponse, IHttpResponse>, TResult> onRedirect,
            Func<string, TResult> onBadResponse,
            TelemetryClient telemetry)
        {
            return application.GetRedirectUriAsync(
                    accountId, extraParams,
                    method, authorization,
                    request, endpoints,
                    baseUrl,
                    authorizationProvider,
                (redirectUrlSelected, modifier) =>
                {
                    telemetry.TrackEvent($"CreateResponse - redirectUrlSelected1: {redirectUrlSelected.AbsolutePath}");
                    telemetry.TrackEvent($"CreateResponse - redirectUrlSelected2: {redirectUrlSelected.AbsoluteUri}");
                    return onRedirect(redirectUrlSelected, modifier);
                },
                (paramName, why) =>
                {
                    var message = $"Invalid parameter while completing login: {paramName} - {why}";
                    telemetry.TrackException(new ResponseException(message));
                    return onBadResponse(message);
                },
                () =>
                {
                    var message = $"Invalid account while completing login";
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

        //private static Task<TResult> UnmappedCredentialAsync<TResult>(
        //        string subject, IDictionary<string, string> extraParams,
        //        EastFive.Azure.Auth.Method authentication, Authorization authorization,
        //        IProvideAuthorization authorizationProvider,
        //        IAuthApplication application,
        //        Uri baseUri,
        //    Func<Guid, TResult> createMapping,
        //    Func<TResult> onAllowSelfServe,
        //    Func<Uri, TResult> onInterceptProcess,
        //    Func<string, TResult> onFailure,
        //    TelemetryClient telemetry)
        //{
        //    return application.OnUnmappedUserAsync(subject, extraParams,
        //            authentication, authorization, authorizationProvider, baseUri,
        //        (accountId) => createMapping(accountId),
        //        () => onAllowSelfServe(),
        //        (callback) => onInterceptProcess(callback),
        //        () =>
        //        {
        //            var message = "Token is not connected to a user in this system";
        //            telemetry.TrackException(new ResponseException(message));
        //            return onFailure(message);
        //        });
        //}

        public static async Task<TResult> MapAccountAsync<TResult>(Authorization authorization,
            Guid internalAccountId, string externalAccountKey,
            IInvokeApplication endpoints,
            Uri baseUri,
            AzureApplication application, IHttpRequest request,
            Func<Uri, TResult> onRedirect,
            Func<string, TResult> onFailure,
            TelemetryClient telemetry)
        {
            // applies when intercept process is used
            if (authorization.accountIdMaybe.IsDefault())
            {
                authorization = await authorization.authorizationRef.StorageUpdateAsync(
                    async (a, saveAsync) =>
                    {
                        a.accountIdMaybe = internalAccountId;
                        await saveAsync(a);
                        return a;
                    });
            }

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
                                           application, request, endpoints, baseUri, loginProvider,
                                       (url, modifier) => onRedirect(url),
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
    }
}

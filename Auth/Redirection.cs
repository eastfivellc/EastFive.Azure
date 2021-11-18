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
                async discard =>
                {
                    var pauseRedirections = AppSettings.PauseRedirections.ConfigurationBoolean(
                        pr => pr,
                        why => false,
                        () => false);
                    if (pauseRedirections)
                        return request.CreateResponse(System.Net.HttpStatusCode.OK, requestId);

                    var baseUri = request.RequestUri;
                    return await AuthenticationAsync(
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
                Method authentication, string externalAccountKey,
                IDictionary<string, string> extraParams,
                IAzureApplication application, IHttpRequest request,
                IInvokeApplication endpoints, IProvideLogin loginProvider,
                Uri baseUri,
            Func<Uri, Guid?, Func<IHttpResponse, IHttpResponse>, TResult> onRedirect,
            Func<string, TResult> onGeneralFailure,
            TelemetryClient telemetry)
        {
            return await await IdentifyAccountAsync(authorization,
                authentication, externalAccountKey, extraParams,
                application, loginProvider, baseUri,

                // Found
                async (internalAccountId, onRecreate) =>
                {
                    authorization.parameters = extraParams;
                    authorization.accountIdMaybe = internalAccountId;
                    return await await CreateLoginResponseAsync(
                                internalAccountId, extraParams,
                                authentication, authorization,
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
                            async () =>
                            {
                                if (onRecreate.IsDefaultOrNull())
                                {
                                    await saveAsync(authorization);
                                    return onGeneralFailure(
                                        "System is mapped to an invalid account.");
                                }
                                return await await onRecreate(true);
                            },
                            telemetry);
                },

                // Created
                async (internalAccountId) =>
                {
                    authorization.parameters = extraParams;
                    await saveAsync(authorization);
                    return await CreateLoginResponseAsync(
                                            internalAccountId, extraParams,
                                            authentication, authorization,
                                            application, request, endpoints, baseUri, loginProvider,
                                        (url, modifier) => onRedirect(url, internalAccountId, modifier),
                                        onGeneralFailure,
                                        () => onGeneralFailure("An invalid account mapping was created."),
                                        telemetry);
                },

                // Allow self serve
                async () =>
                {
                    authorization.parameters = extraParams;
                    await saveAsync(authorization);
                    return await CreateLoginResponseAsync(
                            default(Guid?), extraParams,
                            authentication, authorization,
                            application, request, endpoints, baseUri, loginProvider,
                        (url, modifier) => onRedirect(url, default(Guid?), modifier),
                        onGeneralFailure,
                        () => onGeneralFailure("The system created an invalid account."),
                            telemetry);
                },

                // Intercept process
                async (interceptionUrl) =>
                {
                    authorization.parameters = extraParams;
                    await saveAsync(authorization);
                    return onRedirect(interceptionUrl, default, m => m);
                },

                // Failure
                async (why) =>
                {
                    // Save params so they can be used later
                    authorization.parameters = extraParams;
                    await saveAsync(authorization);
                    return onGeneralFailure(why);
                },
                    telemetry: telemetry);

            //authorization.authorized = true;
            //authorization.LocationAuthentication = null;

            //var result = await await AccountMapping.FindByMethodAndKeyAsync(authentication.authenticationId, externalAccountKey,
            //        authorization,

            //    // Found
            //    async internalAccountId =>
            //    {
            //        authorization.parameters = extraParams;
            //        authorization.accountIdMaybe = internalAccountId;
            //        return await await CreateLoginResponseAsync(
            //                    internalAccountId, extraParams,
            //                    authentication, authorization,
            //                    application, request, endpoints, baseUri, loginProvider,
            //                async (url, modifier) =>
            //                {
            //                    await saveAsync(authorization);
            //                    return onRedirect(url, internalAccountId, modifier);
            //                },
            //                async (why) =>
            //                {
            //                    await saveAsync(authorization);
            //                    return onGeneralFailure(why);
            //                },
            //                ()=> OnNotFound(true),
            //                telemetry);
            //    },
            //    () => OnNotFound(false));
            //return result;

            //async Task<TResult> OnNotFound(bool isAccountInvalid)
            //{
            //    return await await UnmappedCredentialAsync(externalAccountKey, extraParams,
            //                authentication, authorization,
            //                loginProvider, application, baseUri,

            //            // Create mapping
            //            async (internalAccountId) =>
            //            {
            //                authorization.parameters = extraParams;
            //                authorization.accountIdMaybe = internalAccountId;
            //                await saveAsync(authorization);
            //                return await await AccountMapping.CreateByMethodAndKeyAsync(
            //                        authorization, externalAccountKey, internalAccountId,
            //                    () =>
            //                    {
            //                        return CreateLoginResponseAsync(
            //                                internalAccountId, extraParams,
            //                                authentication, authorization,
            //                                application, request, endpoints, baseUri, loginProvider,
            //                            (url, modifier) => onRedirect(url, internalAccountId, modifier),
            //                            onGeneralFailure,
            //                            () => onGeneralFailure("An invalid account mapping was created."),
            //                            telemetry);
            //                    },
            //                    () =>
            //                    {
            //                        return CreateLoginResponseAsync(
            //                                internalAccountId, extraParams,
            //                                authentication, authorization,
            //                                application, request, endpoints, baseUri, loginProvider,
            //                            (url, modifier) => onRedirect(url, internalAccountId, modifier),
            //                            onGeneralFailure,
            //                            () => onGeneralFailure("System is mapped to an invalid account."),
            //                            telemetry);
            //                    },
            //                        isAccountInvalid);
            //            },

            //            // Allow self serve
            //            async () =>
            //            {
            //                authorization.parameters = extraParams;
            //                await saveAsync(authorization);
            //                return await CreateLoginResponseAsync(
            //                        default(Guid?), extraParams,
            //                        authentication, authorization,
            //                        application, request, endpoints, baseUri, loginProvider,
            //                    (url, modifier) => onRedirect(url, default(Guid?), modifier),
            //                    onGeneralFailure,
            //                    () => onGeneralFailure("The system created an invalid account."),
            //                        telemetry);
            //            },

            //            // Intercept process
            //            async (interceptionUrl) =>
            //            {
            //                authorization.parameters = extraParams;
            //                await saveAsync(authorization);
            //                return onRedirect(interceptionUrl, default, m => m);
            //            },

            //            // Failure
            //            async (why) =>
            //            {
            //                // Save params so they can be used later
            //                authorization.parameters = extraParams;
            //                await saveAsync(authorization);
            //                return onGeneralFailure(why);
            //            },
            //            telemetry);
            //}
        }

        public static async Task<TResult> IdentifyAccountAsync<TResult>(Authorization authorization,
                Method authenticationMethod, string externalAccountKey,
                IDictionary<string, string> extraParams,
                IAzureApplication application,
                IProvideLogin loginProvider,
                Uri baseUri,
            Func<Guid, Func<bool, Task<TResult>>, TResult> onLocated,
            Func<Guid, TResult> onCreated,
            Func<TResult> onSelfServe,
            Func<Uri, TResult> onInterupted,
            Func<string, TResult> onGeneralFailure,
                TelemetryClient telemetry)
        {
            authorization.authorized = true;
            authorization.LocationAuthentication = null;

            if (loginProvider is IProvideClaims)
            {
                var claimProvider = (IProvideClaims)loginProvider;
                if (application is IProvideAccountInformation)
                {
                    var accountInfoProvider = (IProvideAccountInformation)application;
                    return await await accountInfoProvider.FindOrCreateAccountByMethodAndKeyAsync(
                            authenticationMethod.authenticationId, externalAccountKey,
                        onFound: internalAccountId =>
                        {
                            return onLocated(
                                     internalAccountId,
                                     (isAccountInvalid) => CreateAccountAsync())
                                 .AsTask();
                        },
                        () => CreateAccountAsync());

                    Task<TResult> CreateAccountAsync()
                    {
                        return accountInfoProvider.CreateUnpopulatedAccountAsync(
                                authenticationMethod.authenticationId, externalAccountKey,
                            onNeedsPopulated: async (account, saveAsync) =>
                            {
                                var accountLinksToEdit = account.AccountLinks;
                                accountLinksToEdit.accountLinks = account.AccountLinks.accountLinks
                                    .NullToEmpty()
                                    .Where(al => al.method.id != authenticationMethod.authenticationId.id)
                                    .Append(
                                        new AccountLink()
                                        {
                                            externalAccountKey = externalAccountKey,
                                            method = authenticationMethod.authenticationId,
                                        })
                                    .ToArray();
                                account.AccountLinks = accountLinksToEdit;

                                var populatedAccount = account.GetType()
                                    .GetPropertyAndFieldsWithAttributesInterface<PopulatedByAuthorizationClaim>()
                                    .Aggregate(account,
                                        (accountToUpdate, tpl) =>
                                        {
                                            var (member, populationAttr) = tpl;
                                            return populationAttr.PopulateValue(accountToUpdate, member,
                                                (string claimType, out string claimValue) =>
                                                    claimProvider.GetStandardClaimValue(claimType, extraParams, out claimValue));
                                        });
                                await saveAsync(populatedAccount);

                                return onCreated(populatedAccount.id);
                            },
                            onInterupted: (uri) => onInterupted(uri),
                            onNotCreated: (string why) => onGeneralFailure(why));
                    }
                }
            }

            return await await AccountMapping.FindByMethodAndKeyAsync(authenticationMethod.authenticationId, externalAccountKey,
                    authorization,
                // Found
                internalAccountId =>
                {
                    return onLocated(
                            internalAccountId, 
                            (isAccountInvalid) => OnNotFound(isAccountInvalid))
                        .AsTask();
                },
                () => OnNotFound(false));

            async Task<TResult> OnNotFound(bool isAccountInvalid)
            {
                return await await UnmappedCredentialAsync(externalAccountKey, extraParams,
                            authenticationMethod, authorization,
                            loginProvider, application, baseUri,

                        // Create mapping
                        (internalAccountId) =>
                        {
                            return AccountMapping.CreateByMethodAndKeyAsync(
                                    authorization, externalAccountKey, internalAccountId,
                                () =>
                                {
                                    return onCreated(internalAccountId);
                                },
                                () =>
                                {
                                    return onLocated(internalAccountId, default);
                                },
                                    isAccountInvalid);
                        },

                        // Allow self serve
                        () =>
                        {
                            return onSelfServe().AsTask();
                        },

                        // Intercept process
                        (interceptionUrl) =>
                        {
                            return onInterupted(interceptionUrl).AsTask();
                        },

                        // Failure
                        (why) =>
                        {
                            return onGeneralFailure(why).AsTask();
                        },
                        telemetry);
            }
        }

        private static Task<TResult> CreateLoginResponseAsync<TResult>(
                Guid? accountId, IDictionary<string, string> extraParams,
                Method method, Authorization authorization,
                IAuthApplication application,
                IHttpRequest request, IInvokeApplication endpoints,
                Uri baseUrl,
                IProvideAuthorization authorizationProvider,
            Func<Uri, Func<IHttpResponse, IHttpResponse>, TResult> onRedirect,
            Func<string, TResult> onBadResponse,
            Func<TResult> onInvalidAccount,
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
                    return onInvalidAccount();
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
                IAuthApplication application,
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
                                       () => onFailure("Account mapping created a broken account."),
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

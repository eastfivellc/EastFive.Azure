﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Web.Configuration;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController(
        Route = "XSession",
        ContentType = "x-application/auth-session",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    public struct Session : IReferenceable
    {
        #region Properties

        public Guid id => sessionId.id;

        public const string SessionIdPropertyName = "id";
        [ApiProperty(PropertyName = SessionIdPropertyName)]
        [JsonProperty(PropertyName = SessionIdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        [ResourceIdentifier]
        public IRef<Session> sessionId;

        public const string CreatedScopeName = "created";
        [ScopeDateTime(CreatedScopeName, SpanUnits = TimeSpanUnits.years, IgnoreNull = true)]
        public DateTime? created;

        public const string ExpiresPropertyName = "expires";
        [ApiProperty(PropertyName = ExpiresPropertyName)]
        [JsonProperty(PropertyName = ExpiresPropertyName)]
        [Storage]
        public DateTime? expires;

        public const string AuthorizationPropertyName = "authorization";
        [ApiProperty(PropertyName = AuthorizationPropertyName)]
        [JsonProperty(PropertyName = AuthorizationPropertyName)]
        [Storage(Name = AuthorizationPropertyName)]
        public IRefOptional<Authorization> authorization { get; set; }

        public const string AccountPropertyName = "account";
        [JsonProperty(PropertyName = AccountPropertyName)]
        [ApiProperty(PropertyName = AccountPropertyName)]
        [Storage(Name = AccountPropertyName)]
        [ScopedLookup(AccountPropertyName, partitionScope: CreatedScopeName)]
        [ScopeId(AccountPropertyName, IgnoreNullOrDefault = true)]
        public Guid? account { get; set; }

        /// <summary>
        /// Determines if the session is authorized.
        /// </summary>
        /// <remarks>
        ///   The presence of .authorization is insufficent for this
        ///   determination because the referenced authorization may
        ///   not have been executed.
        /// </remarks>
        public const string AuthorizedTokenPropertyName = "authorized";
        [ApiProperty(PropertyName = AuthorizedTokenPropertyName)]
        [JsonProperty(PropertyName = AuthorizedTokenPropertyName)]
        public bool authorized;

        public const string HeaderNamePropertyName = "header_name";
        [ApiProperty(PropertyName = HeaderNamePropertyName)]
        [JsonProperty(PropertyName = HeaderNamePropertyName)]
        public string HeaderName
        {
            get
            {
                return "Authorization";
            }
            set
            {

            }
        }
        
        public const string TokenPropertyName = "token";
        [ApiProperty(PropertyName = TokenPropertyName)]
        [JsonProperty(PropertyName = TokenPropertyName)]
        public string token;
        
        public const string RefreshTokenPropertyName = "refresh_token";
        [ApiProperty(PropertyName = RefreshTokenPropertyName)]
        [JsonProperty(PropertyName = RefreshTokenPropertyName)]
        [Storage(Name = RefreshTokenPropertyName)]
        public string refreshToken;

        #endregion

        private static TimeSpan CreateExpirationDuration() =>
            // prefer an absolute time of day setting over a maximum duration setting
            AppSettings.Auth.TokenExpirationTimeOfDay.ConfigurationString(
                (timeAndOffset) =>
                {
                    if (DateTimeExtensions.GetDisappearingOffset(timeAndOffset, out TimeSpan duration))
                        return duration;
                    throw new Exception($"The settings key `{AppSettings.Auth.TokenExpirationTimeOfDay}` could not be parsed");
                },
                (unspecifiedMsg) => AppSettings.Auth.TokenExpirationInMinutes.ConfigurationDouble(
                    (minutes) => TimeSpan.FromMinutes(minutes)));

        private static async Task<TResult> GetClaimsAsync<TResult>(
            IAuthApplication application, IRefOptional<Authorization> authorizationRefMaybe,
            Func<IDictionary<string, string>, Guid?, bool, TResult> onClaims,
            Func<string, TResult> onFailure)
        {
            if (!authorizationRefMaybe.HasValueNotNull())
                return onClaims(new Dictionary<string, string>(), default(Guid?), false);
            var authorizationRef = authorizationRefMaybe.Ref;

            return await Api.AppSettings.ActorIdClaimType.ConfigurationString(
                (accountIdClaimType) =>
                {
                    return GetSessionAcountAsync(authorizationRef, application,
                        (accountId, claims, authorized) =>
                        {
                            var claimsWithAccountId = claims
                                .Append(accountIdClaimType.PairWithValue(accountId.ToString()))
                                .ToDictionary();
                            return onClaims(claimsWithAccountId, accountId, authorized);
                        },
                        (why, authorized) => onClaims(new Dictionary<string, string>(), default(Guid?), authorized));
                },
                (why) =>
                {
                    return GetSessionAcountAsync(authorizationRef, application,
                        (accountId, claims, authorized) =>
                        {
                            var claimsDictionary = claims
                                .NullToEmpty()
                                .ToDictionary();
                            return onClaims(claimsDictionary, accountId, authorized);
                        },
                        (why, authorized) => onClaims(new Dictionary<string, string>(), default(Guid?), authorized));
                });
        }

        #region Http Methods

        #region GET

        [Api.HttpGet]
        public static async Task<IHttpResponse> GetAsync(
                [QueryParameter(Name = SessionIdPropertyName, CheckFileName =true)]IRef<Session> sessionRef,
                EastFive.Azure.Auth.SessionTokenMaybe security,
                IAuthApplication application,
            ContentTypeResponse<Session> onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized,
            ConfigurationFailureResponse onConfigurationFailure)
        {
            if (!IsAnonSessionAllowed())
            {
                if (security.sessionId != sessionRef.id)
                    return onUnauthorized();
            }
            return await await sessionRef.StorageGetAsync(
                (session) =>
                {
                    return Web.Configuration.Settings.GetUri(
                            EastFive.Security.AppSettings.TokenScope,
                        scope =>
                        {
                            return GetClaimsAsync(application, session.authorization,
                                (claims, accountIdMaybe, authorized) =>
                                {
                                    session.account = accountIdMaybe;
                                    session.authorized = authorized;
                                    var duration = CreateExpirationDuration();
                                    return Api.Auth.JwtTools.CreateToken(session.id,
                                            scope, duration, claims,
                                        (tokenNew, whenIssued) =>
                                        {
                                            session.expires = whenIssued + duration;
                                            session.token = tokenNew;
                                            return onFound(session);
                                        },
                                        (missingConfig) => onConfigurationFailure("Missing", missingConfig),
                                        (configName, issue) => onConfigurationFailure(configName, issue));
                                },
                                (why) => onNotFound());
                        },
                        (why) => onConfigurationFailure("Missing", why).AsTask());
                },
                () => onNotFound().AsTask());

            bool IsAnonSessionAllowed()
            {
                var appType = application.GetType();
                if (!appType.TryGetAttributeInterface<IConfigureAuthorization>(out IConfigureAuthorization authConfig))
                    return false;
                return authConfig.IsAnonymousSessionAllowed;
            }
        }

        [Api.HttpGet]
        public static Task<IHttpResponse> GetByRequestIdAsync(
                [QueryParameter(Name = SessionIdPropertyName, CheckFileName = true)]IRef<Session> sessionRef,
                [QueryParameter(Name = EastFive.Api.Azure.AzureApplication.QueryRequestIdentfier)]IRef<Authorization> authorization,
                //EastFive.Api.SessionToken security,
                IAuthApplication application, IProvideUrl urlHelper,
            ContentTypeResponse<Session> onUpdated,
            NotFoundResponse onNotFound,
            ForbiddenResponse forbidden,
            ConfigurationFailureResponse onConfigurationFailure,
            GeneralConflictResponse onFailure)
        {
            return UpdateBodyAsync(sessionRef, authorization.Optional(),
                    application,
                onUpdated,
                onNotFound,
                forbidden,
                onConfigurationFailure,
                onFailure);
        }

        #endregion

        [Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Version = Workflows.AuthorizationFlow.Version,
            Step = 3.0)]
        [HttpPost]
        public async static Task<IHttpResponse> CreateAsync(
                [Api.Meta.Flows.WorkflowNewId]
                [Property(Name = SessionIdPropertyName)]
                IRef<Session> sessionId,

                [Api.Meta.Flows.WorkflowParameter(Value = "{{XAuthorization}}")]
                [PropertyOptional(Name = AuthorizationPropertyName)]
                IRefOptional<Authorization> authorizationRefMaybe,

                [Resource]Session session,
                IAuthApplication application,

            [Api.Meta.Flows.WorkflowVariable2(Workflows.AuthorizationFlow.Variables.AuthHeaderName, HeaderNamePropertyName)]
            [Api.Meta.Flows.WorkflowVariable(Workflows.AuthorizationFlow.Variables.TokenName, TokenPropertyName)]
            CreatedBodyResponse<Session> onCreated,

            AlreadyExistsResponse onAlreadyExists,
            ForbiddenResponse forbidden,
            ConfigurationFailureResponse onConfigurationFailure,
            GeneralConflictResponse onFailure)
        {
            session.refreshToken = Security.SecureGuid.Generate().ToString("N");

            return await Security.AppSettings.TokenScope.ConfigurationUri(
                async (scope) =>
                {
                    return await await GetClaimsAsync(application, authorizationRefMaybe,
                        (claims, accountIdMaybe, authorized) =>
                        {
                            var duration = CreateExpirationDuration();
                            session.account = accountIdMaybe;
                            session.authorized = authorized;
                            return session.StorageCreateAsync(
                                (sessionIdCreated) =>
                                {
                                    return Api.Auth.JwtTools.CreateToken(sessionId.id,
                                        scope, duration, claims,
                                        (tokenNew, whenIssued) =>
                                        {
                                            session.token = tokenNew;
                                            session.expires = whenIssued + duration;
                                            return onCreated(session);
                                        },
                                        (missingConfig) => onConfigurationFailure("Missing", missingConfig),
                                        (configName, issue) => onConfigurationFailure(configName, issue));
                                },
                                () =>
                                {
                                    return Api.Auth.JwtTools.CreateToken(sessionId.id,
                                        scope, duration, claims,
                                        (tokenNew, whenIssued) =>
                                        {
                                            session.token = tokenNew;
                                            session.expires = whenIssued + duration;
                                            return onCreated(session);
                                        },
                                        (missingConfig) => onConfigurationFailure("Missing", missingConfig),
                                        (configName, issue) => onConfigurationFailure(configName, issue));
                                    // onAlreadyExists()
                                });
                        },
                        (why) => onFailure(why).AsTask());
                },
                (why) => onConfigurationFailure("Missing", why).AsTask());
        }

        [HttpPatch]
        public static Task<IHttpResponse> UpdateBodyAsync(
                [UpdateId(Name = SessionIdPropertyName)]IRef<Session> sessionRef,
                [PropertyOptional(Name = AuthorizationPropertyName)]IRefOptional<Authorization> authorizationRefMaybe,
                IAuthApplication application,
            ContentTypeResponse<Session> onUpdated,
            NotFoundResponse onNotFound,
            ForbiddenResponse forbidden,
            ConfigurationFailureResponse onConfigurationFailure,
            GeneralConflictResponse onFailure)
        {
            return sessionRef.StorageUpdateAsync(
                (sessionStorage, saveSessionAsync) =>
                {
                    return Security.AppSettings.TokenScope.ConfigurationUri(
                        async (scope) =>
                        {
                            return await await GetClaimsAsync(application, authorizationRefMaybe,
                                async (claims, accountIdMaybe, authorized) =>
                                {
                                    sessionStorage.authorization = authorizationRefMaybe;
                                    sessionStorage.authorized = authorized;
                                    sessionStorage.account = accountIdMaybe;
                                    var duration = CreateExpirationDuration();
                                    return await Api.Auth.JwtTools.CreateToken(sessionRef.id,
                                            scope, duration, claims,
                                        async (tokenNew, whenIssued) =>
                                        {
                                            sessionStorage.token = tokenNew;
                                            sessionStorage.expires = whenIssued + duration;
                                            await saveSessionAsync(sessionStorage);
                                            return onUpdated(sessionStorage);
                                        },
                                        (missingConfig) => onConfigurationFailure("Missing", missingConfig).AsTask(),
                                        (configName, issue) => onConfigurationFailure(configName, issue).AsTask());
                                },
                                why => onFailure(why).AsTask());
                        },
                        (why) => onConfigurationFailure("Missing", why).AsTask());
                },
                onNotFound: () => onNotFound());
        }

        [HttpDelete]
        public static Task<IHttpResponse> DeleteAsync(
                [UpdateId(Name = SessionIdPropertyName)]IRef<Session> sessionRef,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound)
        {
            return sessionRef.StorageDeleteAsync(
                onDeleted:(discard) =>
                {
                    return onDeleted();
                },
                onNotFound: () => onNotFound());
        }

        #endregion

        public async static Task<Session> CreateAsync(
            IAuthApplication application, IRefOptional<Authorization> authorizationRefMaybe,
            IRefOptional<Session> sessionToCreateMaybe = default)
        {
            var sessionId = sessionToCreateMaybe.HasValueNotNull() ?
                sessionToCreateMaybe.Ref
                :
                Ref<Session>.SecureRef();
            var session = new Session()
            {
                sessionId = sessionId,
                refreshToken = Security.SecureGuid.Generate().ToString("N"),
                authorization = authorizationRefMaybe,
            };

            return await Security.AppSettings.TokenScope.ConfigurationUri(
                async (scope) =>
                {
                    return await await GetClaimsAsync(application, authorizationRefMaybe,
                        async (claims, accountIdMaybe, authorized) =>
                        {
                            session.account = accountIdMaybe;
                            session.authorized = authorized;
                            var duration = CreateExpirationDuration();
                            return await await session.StorageCreateAsync(
                                (sessionIdCreated) =>
                                {
                                    return Api.Auth.JwtTools.CreateToken(session.id,
                                        scope, duration, claims,
                                        (tokenNew, whenIssued) =>
                                        {
                                            session.token = tokenNew;
                                            session.expires = whenIssued + duration;
                                            return session;
                                        },
                                        (missingConfig) => throw new Exception(missingConfig),
                                        (configName, issue) => throw new Exception(issue)).AsTask();
                                },
                                onAlreadyExists:() =>
                                {
                                    return sessionId.StorageUpdateAsync(
                                        async (sessionToUpdate, saveAsync) =>
                                        {
                                            sessionToUpdate.authorization = authorizationRefMaybe;
                                            sessionToUpdate.account = accountIdMaybe;
                                            sessionToUpdate.authorized = authorized;
                                            await saveAsync(sessionToUpdate);
                                            return Api.Auth.JwtTools.CreateToken(sessionToUpdate.id,
                                                scope, duration, claims,
                                                (tokenNew, whenIssued) =>
                                                {
                                                    sessionToUpdate.token = tokenNew;
                                                    sessionToUpdate.expires = whenIssued + duration;
                                                    return sessionToUpdate;
                                                },
                                                (missingConfig) => throw new Exception(missingConfig),
                                                (configName, issue) => throw new Exception(issue));
                                        });
                                });
                        },
                        (why) => throw new Exception(why));
                },
                (why) => throw new Exception(why));
        }

        private static async Task<TResult> GetSessionAcountAsync<TResult>(IRef<Authorization> authorizationRef,
                IAuthApplication application,
            Func<Guid, IDictionary<string, string>, bool, TResult> onSuccess,
            Func<string, bool, TResult> onFailure)
        {
            return await await authorizationRef.StorageGetAsync(
                async (authorization) =>
                {
                    if (!authorization.accountIdMaybe.HasValue) // (!authorization.authorized)
                        return onFailure("Invalid authorization -- it is not authorized.", false);

                    if (!authorization.accountIdMaybe.HasValue)
                        return onFailure("Authorization is not connected to an account.", false);


                    // return onSuccess(authorization.accountIdMaybe.Value, authorization.claims, authorization.authorized);

                    var methodRef = authorization.Method;
                    return await await Method.ById(methodRef, application,
                        async method =>
                        {
                            return await await method.GetAuthorizationKeyAsync(application, authorization.parameters,
                                async (externalUserKey) =>
                                {
                                    if (application is IProvideAccountInformation)
                                    {
                                        var accountInformationProvider = application as IProvideAccountInformation;
                                        return await await accountInformationProvider
                                            .FindAccountByMethodAndKeyAsync(
                                                    method, externalUserKey,
                                                    authorization,
                                                onAccountFound :(accountId, updatedClaims) => onSuccess(accountId, updatedClaims, authorization.authorized).AsTask(),
                                                onReject :() => OnContinue());
                                    }
                                    return await OnContinue();
                                    Task<TResult> OnContinue()
                                    {
                                        return Auth.AccountMapping.FindByMethodAndKeyAsync(method.authenticationId, externalUserKey,
                                                authorization,
                                            accountId =>
                                            {
                                                return onSuccess(accountId, authorization.claims, authorization.authorized);
                                            },
                                            () =>
                                            {
                                                return onFailure("No mapping to that account.", authorization.authorized);
                                            });
                                    };
                                },
                                (why) => onFailure(why, authorization.authorized).AsTask(),
                                () => onFailure("This login method is no longer supported.", false).AsTask());
                        },
                        () => onFailure("Authorization method is no longer valid on this system.", authorization.authorized).AsTask());
                    //{
                    //    return CheckSuperAdminBeforeFailure(authorizationRef,
                    //            "Authorization method is no longer valid on this system.", authorization.authorized,
                    //        onSuccess, onFailure).AsTask();
                    //});
                },
                () => onFailure("Authorization not found.", false).AsTask());
                //{
                //    return CheckSuperAdminBeforeFailure(authorizationRef, "Authorization not found.", false,
                //        onSuccess, onFailure).AsTask();
                //});
        }

        private static TResult CheckSuperAdminBeforeFailure<TResult>( 
                IRef<Authorization> authorizationRef, string failureMessage, bool authorized,
            Func<Guid, bool, TResult> onSuccess,
            Func<string, bool, TResult> onFailure)
        {
            var isSuperAdminAuth = Api.AppSettings.AuthorizationIdSuperAdmin.ConfigurationGuid(
                (authorizationIdSuperAdmin) =>
                {
                    if (authorizationIdSuperAdmin == authorizationRef.id)
                        return true;
                    return false;
                },
                why => false);

            if (!isSuperAdminAuth)
                return onFailure(failureMessage, authorized);

            return Api.AppSettings.ActorIdSuperAdmin.ConfigurationGuid(
                (authorizationIdSuperAdmin) => onSuccess(authorizationIdSuperAdmin, authorized),
                (dontCare) => onFailure(failureMessage, authorized));
        }
    }
}
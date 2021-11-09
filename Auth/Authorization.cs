using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Threading.Tasks;
using EastFive.Api;
using EastFive.Api.Auth;
using EastFive.Api.Azure;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;

namespace EastFive.Azure.Auth
{
    [DataContract]
    [FunctionViewController(
        Route = "XAuthorization",
        ContentType = "x-application/auth-authorization",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    [InstigateAuthorization]
    public struct Authorization : IReferenceable
    {
        #region Properties

        #region Base

        public Guid id => authorizationRef.id;

        public const string AuthorizationIdPropertyName = "id";
        [ApiProperty(PropertyName = AuthorizationIdPropertyName)]
        [JsonProperty(PropertyName = AuthorizationIdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        [ResourceIdentifier]
        public IRef<Authorization> authorizationRef;

        [LastModified]
        [StorageQuery]
        public DateTime lastModified;

        #endregion

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
        [StorageQuery]
        [JsonIgnore]
        public bool authorized;

        [Storage]
        [JsonIgnore]
        public bool expired;

        [Storage]
        [JsonIgnore]
        public Guid? accountIdMaybe;

        [Storage]
        [JsonIgnore]
        public DateTime? deleted;

        #endregion

        #region Http Methods

        #region GET

        [Api.HttpGet]
        public static Task<IHttpResponse> GetAsync(
                [QueryId(Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
                EastFive.Api.SessionToken? securityMaybe,
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

        #endregion

        #region POST

        [Api.Meta.Flows.WorkflowStep(
            FlowName = Workflows.AuthorizationFlow.FlowName,
            Step = 2.0)]
        [Api.HttpPost]
        public async static Task<IHttpResponse> CreateAsync(
                [Api.Meta.Flows.WorkflowNewId]
                [Property(Name = AuthorizationIdPropertyName)]
                IRef<Authorization> authorizationRef,

                [Api.Meta.Flows.WorkflowParameter(Value = "{{AuthenticationMethod}}")]
                [Property(Name = MethodPropertyName)]
                IRef<Method> method,

                [Api.Meta.Flows.WorkflowParameter(Value = "http://example.com")]
                [Property(Name = LocationAuthorizationReturnPropertyName)]
                Uri locationAuthenticationReturn,

                [Resource]Authorization authorization,
                IAuthApplication application, IProvideUrl urlHelper,
            CreatedBodyResponse<Authorization> onCreated,
            AlreadyExistsResponse onAlreadyExists,
            ReferencedDocumentDoesNotExistsResponse<Method> onAuthenticationDoesNotExist)
        {
            return await await Auth.Method.ById(method, application,
                async (authentication) =>
                {
                    //var authorizationIdSecure = authentication.authenticationId;
                    authorization.LocationAuthentication = await authentication.GetLoginUrlAsync(
                        application, urlHelper, authorizationRef.id);

                    //throw new ArgumentNullException();
                    return await authorization.StorageCreateAsync(
                        createdId => onCreated(authorization),
                        () => onAlreadyExists());
                },
                () => onAuthenticationDoesNotExist().AsTask());
        }

        [Api.HttpPost]
        public async static Task<IHttpResponse> CreateAuthorizedAsync(
                [UpdateId(Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
                [Property(Name = MethodPropertyName)]IRef<Method> methodRef,
                [Property(Name = ParametersPropertyName)]IDictionary<string, string> parameters,
                [Resource]Authorization authorization,
                Api.Azure.AzureApplication application, IProvideUrl urlHelper,
                IInvokeApplication endpoints,
                IHttpRequest request,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists,
            ForbiddenResponse onAuthorizationFailed,
            ServiceUnavailableResponse onServericeUnavailable,
            ForbiddenResponse onInvalidMethod)
        {
            return await await Auth.Method.ById(methodRef, application,
                (method) =>
                {
                    var paramsUpdated = parameters;
                        //.Append(
                        //    authorizationRef.id.ToString().PairWithKey("state"))
                        //.ToDictionary();

                    return Redirection.AuthenticationAsync(
                            method,
                            paramsUpdated,
                            application, request, endpoints, request.RequestUri,
                            authorizationRef.Optional(),
                        (redirect, accountIdMaybe, discardModifier) => onCreated(),
                        () => onAuthorizationFailed().AddReason("Authorization was not found"), // Bad credentials
                        why => onServericeUnavailable().AddReason(why),
                        why => onAuthorizationFailed().AddReason(why));
                },
                () => onInvalidMethod().AddReason("The method was not found.").AsTask());
        }

        [Api.HttpPost]
        public async static Task<IHttpResponse> CreateAuthorizedAsync(
                [QueryParameter(Name = "session")] IRef<Session> sessionRef,
                [UpdateId(Name = AuthorizationIdPropertyName)] IRef<Authorization> authorizationRef,
                [Property(Name = MethodPropertyName)] IRef<Method> methodRef,
                [Property(Name = ParametersPropertyName)] IDictionary<string, string> parameters,
                Api.Azure.AzureApplication application,
                IInvokeApplication endpoints,
                IHttpRequest request,
            CreatedBodyResponse<Session> onCreated,
            AlreadyExistsResponse onAlreadyExists,
            ForbiddenResponse onAuthorizationFailed,
            ServiceUnavailableResponse onServericeUnavailable,
            ForbiddenResponse onInvalidMethod,
            GeneralConflictResponse onFailure)
        {
            return await await Auth.Method.ById(methodRef, application,
                async (method) =>
                {
                    var paramsUpdated = parameters
                        .Append(
                            authorizationRef.id.ToString().PairWithKey("state"))
                        .ToDictionary();
                    //var authorizationRequestManager = application.AuthorizationRequestManager;
                    return await await Redirection.AuthenticationAsync(
                            method,
                            paramsUpdated,
                            application, request,
                            endpoints,
                            request.RequestUri,
                            authorizationRef.Optional(),
                        async (redirect, accountIdMaybe, modifier) =>
                        {
                            var session = new Session()
                            {
                                sessionId = sessionRef,
                                account = accountIdMaybe,
                                authorization = authorizationRef.Optional(),
                            };
                            var responseCreated = await Session.CreateAsync(sessionRef, authorizationRef.Optional(),
                                    session,
                                    application,
                                (sessionCreated, contentType) =>
                                {
                                    var response = onCreated(sessionCreated, contentType: contentType);
                                    response.SetLocation(redirect);
                                    return response;
                                },
                                onAlreadyExists,
                                onAuthorizationFailed,
                                (why1, why2) => onServericeUnavailable(),
                                onFailure);
                            var modifiedResponse = modifier(responseCreated);
                            return modifiedResponse;
                        },
                        () => onAuthorizationFailed()
                            .AddReason("Authorization was not found")
                            .AsTask(), // Bad credentials
                        why => onServericeUnavailable()
                            .AddReason(why)
                            .AsTask(),
                        why => onAuthorizationFailed()
                            .AddReason(why)
                            .AsTask());
                },
                () => onInvalidMethod().AddReason("The method was not found.").AsTask());
        }

        #endregion

        #region PATCH

        [Api.HttpPatch] //(MatchAllBodyParameters = false)]
        public async static Task<IHttpResponse> UpdateAsync(
                [UpdateId(Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
                [Property(Name = LocationLogoutReturnPropertyName)]Uri locationLogoutReturn,
                EastFive.Api.SessionToken? securityMaybe,
            NoContentResponse onUpdated,
            NotFoundResponse onNotFound,
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

        #endregion

        #region DELETE

        [HttpDelete]
        public static async Task<IHttpResponse> DeleteAsync(
                [UpdateId(Name = AuthorizationIdPropertyName)]IRef<Authorization> authorizationRef,
                IProvideUrl urlHelper, AzureApplication application,
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

        #endregion

        #region ACTION

        [HttpAction("Replay")]
        [RequiredClaim(ClaimTypes.Role, ClaimValues.Roles.SuperAdmin)]
        public static async Task<IHttpResponse> ReplayAsync(
                [QueryId(Name = AuthorizationIdPropertyName)] IRef<Authorization> authorizationRef,
                Api.Azure.AzureApplication application,
                IInvokeApplication endpoints,
                IHttpRequest request,
            ContentTypeResponse<Session> onReplayed,
            NotFoundResponse onNotFound,
            ForbiddenResponse onAuthorizationFailed,
            ServiceUnavailableResponse onServericeUnavailable,
            ForbiddenResponse onInvalidMethod,
            GeneralConflictResponse onFailure)
        {
            return await await authorizationRef.StorageGetAsync(
                async (authorization) =>
                {
                    var methodRef = authorization.Method;
                    return await await Auth.Method.ById(methodRef, application,
                        async (method) =>
                        {   
                            var paramsUpdated = authorization.parameters
                                .Append(authorizationRef.id.ToString().PairWithKey("state"))
                                .ToDictionary();

                            //var authorizationRequestManager = application.AuthorizationRequestManager;
                            return await await Redirection.AuthenticationAsync(
                                method,
                                paramsUpdated,
                                application, request,
                                endpoints,
                                request.RequestUri,
                                authorizationRef.Optional(),
                                async (redirect, accountIdMaybe, modifier) =>
                                {
                                    var sessionRef = Ref<Session>.SecureRef();
                                    var session = new Session()
                                    {
                                        sessionId = sessionRef,
                                        account = accountIdMaybe,
                                        authorization = authorizationRef.Optional(),
                                    };
                                    var responseCreated = await Session.CreateAsync(sessionRef, authorizationRef.Optional(),
                                        session,
                                        application,
                                        (sessionCreated, contentType) =>
                                        {
                                            var response = onReplayed(sessionCreated, contentType: contentType);
                                            response.SetLocation(redirect);
                                            return response;
                                        },
                                        onAlreadyExists: default,
                                        onAuthorizationFailed,
                                        (why1, why2) => onServericeUnavailable(),
                                        onFailure);
                                    var modifiedResponse = modifier(responseCreated);
                                    return modifiedResponse;
                                },
                                () => onAuthorizationFailed()
                                    .AddReason("Authorization was not found")
                                    .AsTask(), // Bad credentials
                                why => onServericeUnavailable()
                                    .AddReason(why)
                                    .AsTask(),
                                why => onAuthorizationFailed()
                                    .AddReason(why)
                                    .AsTask());
                        },
                        () => onInvalidMethod().AddReason("The method was not found.").AsTask());
                },
                () => onNotFound().AsTask());
        }

        #endregion

        #endregion

        public async Task<TResult> ParseCredentailParameters<TResult>(
                IAuthApplication application,
            Func<string, IProvideLogin, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            var parameters = this.parameters;
            return await Auth.Method.ById(this.Method, application, // TODO: Cleanup 
                (method) =>
                {
                    var loginProviders = application.LoginProviders;
                    var methodName = method.name;
                    if (!loginProviders.ContainsKey(methodName))
                        return onFailure("Method does not match any existing authentication.");

                    var loginProvider = loginProviders[method.name];
                    return loginProvider.ParseCredentailParameters(parameters,
                        (string userKey, Guid? authorizationIdDiscard, Guid? deprecatedId) =>
                        {
                            return onSuccess(userKey, loginProvider);
                        },
                        (why) => onFailure(why));

                    //return application.LoginProviders
                    //    .SelectValues()
                    //    .Where(loginProvider => loginProvider.Method == method.name)
                    //    .FirstAsync(
                    //        (loginProvider) =>
                    //        {
                    //            return loginProvider.ParseCredentailParameters(parameters,
                    //                (string userKey, Guid? authorizationIdDiscard, Guid? deprecatedId) =>
                    //                {
                    //                    return onSuccess(userKey, loginProvider);
                    //                },
                    //                (why) => onFailure(why));
                    //        },
                    //        () => onFailure("Method does not match any existing authentication."));
                },
                () => onFailure("Authentication not found"));
        }
    }

    public class InstigateAuthorizationAttribute : Attribute, IInstigatable
    {
        public Task<IHttpResponse> Instigate(IApplication httpApp,
                IHttpRequest request, ParameterInfo parameterInfo,
            Func<object, Task<IHttpResponse>> onSuccess)
        {
            return request
                .GetSessionIdClaimsAsync(
                    async (sessionId, claims) =>
                    {
                        return await await sessionId.AsRef<Session>().StorageGetAsync(
                            async session =>
                            {
                                return await await session.authorization.StorageGetAsync(
                                    authorization => onSuccess(authorization),
                                    () => request
                                        .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                                        .AddReason("Session is not authorized.")
                                        .AsTask());
                            },
                            () => request
                                .CreateResponse(System.Net.HttpStatusCode.Unauthorized)
                                .AddReason("Session does not exist")
                                .AsTask());
                    });
        }
    }
}
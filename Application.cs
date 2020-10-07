using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Routing;

using Microsoft.ApplicationInsights;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage;

using BlackBarLabs.Api.Resources;
using EastFive.Linq;
using EastFive.Security.SessionServer;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Collections.Generic;
using EastFive.Azure.Auth;
using EastFive.Azure.Monitoring;
using EastFive.Web.Configuration;

namespace EastFive.Api.Azure
{

    [ApiResources(NameSpacePrefixes = "EastFive.Azure,EastFive.Search")]
    public class AzureApplication : EastFive.Api.HttpApplication
    {
        public const string QueryRequestIdentfier = "request_id";

        public TelemetryClient Telemetry { get; private set; }

        public AzureApplication()
            : base()
        {
            Telemetry = EastFive.Azure.AppSettings.ApplicationInsights.InstrumentationKey.LoadTelemetryClient();

            this.AddInstigator(typeof(EastFive.Security.SessionServer.Context),
                (httpApp, request, parameterInfo, onCreatedSessionContext) => onCreatedSessionContext(this.AzureContext));
            this.AddInstigator(typeof(EastFive.Azure.Functions.InvokeFunction),
                (httpApp, request, parameterInfo, onCreated) =>
                {
                    var baseUriString = request.RequestUri.GetLeftPart(UriPartial.Authority);
                    var baseUri = new Uri(baseUriString);
                    var apiPath = request.RequestUri.AbsolutePath.Trim('/'.AsArray()).Split('/'.AsArray()).First();
                    var invokeFunction = new EastFive.Azure.Functions.InvokeFunction(
                        httpApp as AzureApplication, baseUri, apiPath);
                    return onCreated(invokeFunction);
                });
            this.AddInstigator(typeof(InvokeApplicationDirect),
                (httpApp, request, parameterInfo, onCreated) =>
                {
                    var baseUriString = request.RequestUri.GetLeftPart(UriPartial.Authority);
                    var baseUri = new Uri(baseUriString);
                    var apiPath = request.RequestUri.AbsolutePath.Trim('/'.AsArray()).Split('/'.AsArray()).First();
                    var invokeFunction = new InvokeApplicationDirect(
                        httpApp, baseUri, apiPath, default(CancellationToken));
                    return onCreated(invokeFunction);
                });

        }

        public virtual async Task<bool> CanAdministerCredentialAsync(Guid actorInQuestion, Api.SessionToken security)
        {
            if (security.accountIdMaybe.HasValue)
            {
                if (actorInQuestion == security.accountIdMaybe.Value)
                    return true;
            }

            if (await IsAdminAsync(security))
                return true;

            return false;
        }

        public IInvokeApplication CDN
        {
            get
            {
                return Web.Configuration.Settings.GetUri(
                    EastFive.Azure.AppSettings.CDNEndpointHostname,
                    endpointHostname =>
                    {
                        return Web.Configuration.Settings.GetString(
                            EastFive.Azure.AppSettings.CDNApiRoutePrefix,
                            apiRoutePrefix =>
                            {
                                return new InvokeApplicationRemote(endpointHostname, apiRoutePrefix);
                            },
                            (why) => new InvokeApplicationRemote(endpointHostname, "api"));
                    },
                    (why) => new InvokeApplicationRemote(new Uri("http://example.com"), "api"));
            }
        }

        public virtual Task<bool> ShouldAuthorizeIntegrationAsync(XIntegration integration, EastFive.Azure.Auth.Authorization authorization)
        {
            if (authorization.accountIdMaybe.HasValue)
                if (integration.accountId != authorization.accountIdMaybe.Value)
                    return false.AsTask();
            return true.AsTask();
        }

        public virtual Task<bool> IsAdminAsync(SessionToken security)
        {
            return EastFive.Web.Configuration.Settings.GetGuid(
                EastFive.Api.AppSettings.ActorIdSuperAdmin,
                (actorIdSuperAdmin) =>
                {
                    if (security.accountIdMaybe.HasValue)
                    {
                        if (actorIdSuperAdmin == security.accountIdMaybe.Value)
                            return true;
                    }

                    return false;
                },
                (why) => false).AsTask();
        }

        protected override void Configure(HttpConfiguration config)
        {
            base.Configure(config);
            config.MessageHandlers.Add(new Api.Azure.Modules.SpaHandler(this, config));
            config.Routes.MapHttpRoute(name: "apple-app-links",
                routeTemplate: "apple-app-site-association",
                defaults: new { controller = "AppleAppSiteAssociation", id = RouteParameter.Optional });
            config.Routes.MapHttpRoute(name: "apple-developer-domain-association",
                routeTemplate: ".well-known/apple-developer-domain-association.txt",
                defaults: new { controller = "AppleDeveloperDomainAssociation", id = RouteParameter.Optional });
        }

        public IDictionaryAsync<string, IProvideAuthorization> AuthorizationProviders
        {
            get
            {
                return this.InstantiateAll<IProvideAuthorization>()
                    .Where(authorization => !authorization.IsDefaultOrNull())
                    .Select(authorization => authorization.PairWithKey(authorization.Method))
                    .ToDictionary();
            }
        }

        public IDictionaryAsync<string, IProvideLogin> LoginProviders
        {
            get
            {
                return this.InstantiateAll<IProvideLogin>()
                    .Where(loginProvider => !loginProvider.IsDefaultOrNull())
                    .Select(
                        loginProvider =>
                        {
                            return loginProvider.PairWithKey(loginProvider.Method);
                        })
                    .ToDictionary();
            }
        }

        public IDictionaryAsync<string, IProvideLoginManagement> CredentialManagementProviders
        {
            get
            {
                return this.InstantiateAll<IProvideLoginManagement>()
                    .Where(loginManager => !loginManager.IsDefaultOrNull())
                    .Select(loginManager => loginManager.PairWithKey(loginManager.Method))
                    .ToDictionary();
            }
        }

        public virtual Task SendServiceBusMessageAsync(string queueName, byte[] bytes)
        {
            return SendServiceBusMessageAsync(queueName, new[] { bytes });
        }

        public virtual Task SendServiceBusMessageAsync(string queueName, IEnumerable<byte[]> listOfBytes)
        {
            return AzureApplication.SendServiceBusMessageStaticAsync(queueName, listOfBytes);
        }

        public static async Task SendServiceBusMessageStaticAsync(string queueName, IEnumerable<byte[]> listOfBytes)
        {
            const int maxPayloadSize = 262_144;
            const int perMessageHeaderSize = 58;
            const int perMessageListSize = 8;

            if (!listOfBytes.Any())
                return;

            var client = EastFive.Security.SessionServer.Configuration.AppSettings.ServiceBusConnectionString.ConfigurationString(
                (connectionString) =>
                {
                    return new Microsoft.Azure.ServiceBus.QueueClient(connectionString, queueName);
                },
                (why) => throw new Exception(why));

            // The padding was estimated by this:
            //var bodyLen = 131_006;  // (131_006 + 58 + 8) * 2 messages = 262_144
            //var body = Enumerable.Range(0, bodyLen).Select(x => (byte)5).ToArray();
            //var msg1 = new Microsoft.Azure.ServiceBus.Message(body);
            //var msg2 = new Microsoft.Azure.ServiceBus.Message(body);
            //await client.SendAsync(new List<Microsoft.Azure.ServiceBus.Message> { msg1, msg2 });
            // If the send is successful, we haven't exceeded the max payload

            try
            {
                var messages = listOfBytes
                    .Select(bytes => new Microsoft.Azure.ServiceBus.Message(bytes)
                    {
                        ContentType = "application/octet-stream",
                    })
                    .ToArray();
                var maxMessageSize = messages.Select(x => x.Body.Length + perMessageHeaderSize + perMessageListSize).Max();
                int numberInBatch = maxPayloadSize / maxMessageSize;
                do
                {
                    var toBeSent = messages.Take(numberInBatch).ToArray();
                    try
                    {
                        await client.SendAsync(toBeSent);
                        messages = messages.Skip(toBeSent.Length).ToArray();
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("MessageSizeExceededException"))
                        {
                            numberInBatch -= 1;
                            if (numberInBatch > 0)
                                continue;
                        }
                        throw ex;
                    }
                } while (messages.Length > 0);
            }
            finally
            {
                await client.CloseAsync();
            }
        }

        public virtual async Task<CloudQueueMessage> SendQueueMessageAsync(string queueName, byte[] byteContent)
        {
            var appQueue = EastFive.Azure.AppSettings.ASTConnectionStringKey.ConfigurationString(
                (connString) =>
                {
                    var storageAccount = CloudStorageAccount.Parse(connString);
                    var queueClient = storageAccount.CreateCloudQueueClient();
                    var queue = queueClient.GetQueueReference(queueName);
                    queue.CreateIfNotExists();
                    return queue;
                },
                (why) => throw new Exception(why));

            var message = new CloudQueueMessage(byteContent);
            await appQueue.AddMessageAsync(message);
            return message;
        }

        public virtual async Task<TResult> GetNextQueueMessageAsync<TResult>(string queueName,
            Func<byte[],    // content
                Func<Task>, // dequeueAsync
                Task<TResult>> onNextMessage,
            Func<TResult> onEmpty)
        {
            var appQueue = EastFive.Web.Configuration.Settings.GetString(EastFive.Azure.AppSettings.ASTConnectionStringKey,
                (connString) =>
                {
                    var storageAccount = CloudStorageAccount.Parse(connString);
                    var queueClient = storageAccount.CreateCloudQueueClient();
                    var queue = queueClient.GetQueueReference(queueName);
                    queue.CreateIfNotExists();
                    return queue;
                },
                (why) => throw new Exception(why));
            
            var message = await appQueue.GetMessageAsync();
            if (null == message)
                return onEmpty();

            return await onNextMessage(
                message.AsBytes,
                () => appQueue.DeleteMessageAsync(message));
        }

        protected override async Task<Initialized> InitializeAsync()
        {
            return await base.InitializeAsync();
        }

        internal virtual Credentials.IManageAuthorizationRequests AuthorizationRequestManager
        {
            get
            {
                return new Credentials.AzureStorageTablesLogAuthorizationRequestManager();
            }
        }

        internal async Task<TResult> GetAuthorizationProviderAsync<TResult>(string method,
            Func<IProvideAuthorization, TResult> onSuccess,
            Func<TResult> onCredentialSystemNotAvailable,
            Func<string, TResult> onFailure)
        {
            return await this.AuthorizationProviders.TryGetValueAsync(method,
                onSuccess,
                onCredentialSystemNotAvailable);
        }
        
        internal async Task<TResult> GetLoginProviderAsync<TResult>(string method,
            Func<IProvideLogin, TResult> onSuccess,
            Func<TResult> onCredentialSystemNotAvailable,
            Func<string, TResult> onFailure)
        {
            // this.InitializationWait();
            return await this.LoginProviders.TryGetValueAsync(method,
                onSuccess,
                onCredentialSystemNotAvailable);
        }
        
        public virtual async Task<TResult> OnUnmappedUserAsync<TResult>(
                string subject, IDictionary<string, string> extraParameters,
                EastFive.Azure.Auth.Method authentication, EastFive.Azure.Auth.Authorization authorization,
                IProvideAuthorization authorizationProvider, Uri baseUri,
            Func<Guid, TResult> onCreatedMapping,
            Func<TResult> onAllowSelfServeAccounts,
            Func<Uri, TResult> onInterceptProcess,
            Func<TResult> onNoChange)
        {
            if (authorizationProvider is Credentials.IProvideAccountInformation)
            {
                var accountInfoProvider = authorizationProvider as Credentials.IProvideAccountInformation;
                return await accountInfoProvider
                    .CreateAccount(subject, extraParameters,
                            authentication, authorization, baseUri,
                            this,
                        onCreatedMapping,
                        onAllowSelfServeAccounts,
                        onInterceptProcess,
                        onNoChange);
            }
            return onNoChange();
        }

        public virtual Web.Services.ISendMessageService SendMessageService
        { get => Web.Services.ServiceConfiguration.SendMessageService(); }
        
        public virtual Web.Services.ITimeService TimeService { get => Web.Services.ServiceConfiguration.TimeService(); }
        
        internal virtual WebId GetActorLink(Guid actorId, UrlHelper url)
        {
            return EastFive.Security.SessionServer.Library.configurationManager.GetActorLink(actorId, url);
        }

        public virtual Task<TResult> GetActorNameDetailsAsync<TResult>(Guid actorId,
            Func<string, string, string, TResult> onActorFound,
            Func<TResult> onActorNotFound)
        {
            return EastFive.Security.SessionServer.Library.configurationManager.GetActorNameDetailsAsync(actorId, onActorFound, onActorNotFound);
        }

        public virtual async Task<TResult> GetRedirectUriAsync<TResult>(
                Guid? accountIdMaybe, IDictionary<string, string> authParams,
                EastFive.Azure.Auth.Method method,
                EastFive.Azure.Auth.Authorization authorization,
                IInvokeApplication endpoints,
                Uri baseUri,
                IProvideAuthorization authorizationProvider,
            Func<Uri, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure)
        {
            if(!(authorizationProvider is Credentials.IProvideRedirection))
                return await ComputeRedirectAsync(accountIdMaybe, authParams, 
                        method, authorization, endpoints,
                        authorizationProvider,
                    onSuccess,
                    onInvalidParameter,
                    onFailure);

            var redirectionProvider = authorizationProvider as Credentials.IProvideRedirection;
            return await await redirectionProvider.GetRedirectUriAsync(accountIdMaybe, 
                        authorizationProvider, authParams,
                        method, authorization,
                        this, endpoints, baseUri,
                    async (redirectUri) =>
                    {
                        var fullUri = redirectUri.IsAbsoluteUri?
                            redirectUri
                            :
                            await ResolveAbsoluteUrlAsync(baseUri, redirectUri, accountIdMaybe);
                        var redirectDecorated = this.SetRedirectParameters(authorization, fullUri);
                        return onSuccess(redirectDecorated);
                    },
                    () => ComputeRedirectAsync(accountIdMaybe, authParams,
                            method, authorization, endpoints,
                            authorizationProvider,
                        onSuccess,
                        onInvalidParameter,
                        onFailure),
                    onInvalidParameter.AsAsyncFunc(),
                    onFailure.AsAsyncFunc());
            
        }

        public virtual Task<Uri> ResolveAbsoluteUrlAsync(Uri requestUri, Uri relativeUri, Guid? accountIdMaybe)
        {
            var fullUri = new Uri(requestUri, relativeUri);
            return fullUri.AsTask();
        }

        private async Task<TResult> ComputeRedirectAsync<TResult>(
                Guid? accountIdMaybe, IDictionary<string, string> authParams,
                EastFive.Azure.Auth.Method method,
                EastFive.Azure.Auth.Authorization authorization, IInvokeApplication endpoints,
                IProvideAuthorization authorizationProvider,
            Func<Uri, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure)
        {
            if (!authorization.LocationAuthenticationReturn.IsDefaultOrNull())
            {
                if (authorization.LocationAuthenticationReturn.IsAbsoluteUri)
                {
                    var redirectUrl = SetRedirectParameters(authorization, authorization.LocationAuthenticationReturn);
                    return onSuccess(redirectUrl);
                }
            }

            if (null != authParams && authParams.ContainsKey(EastFive.Security.SessionServer.Configuration.AuthorizationParameters.RedirectUri))
            {
                Uri redirectUri;
                var redirectUriString = authParams[EastFive.Security.SessionServer.Configuration.AuthorizationParameters.RedirectUri];
                if (!Uri.TryCreate(redirectUriString, UriKind.Absolute, out redirectUri))
                    return onInvalidParameter("REDIRECT", $"BAD URL in redirect call:{redirectUriString}");
                var redirectUrl = SetRedirectParameters(authorization, redirectUri);
                return onSuccess(redirectUrl);
            }

            return await EastFive.Web.Configuration.Settings.GetUri(
                EastFive.Security.SessionServer.Configuration.AppSettings.LandingPage,
                (redirectUriLandingPage) =>
                {
                    var redirectUrl = SetRedirectParameters(authorization, redirectUriLandingPage);
                    return onSuccess(redirectUrl);
                },
                (why) => onFailure(why)).AsTask();
        }

        protected Uri SetRedirectParameters(EastFive.Azure.Auth.Authorization authorization, Uri redirectUri)
        {
            var redirectUrl = redirectUri
                //.SetQueryParam(parameterAuthorizationId, authorizationId.Value.ToString("N"))
                //.SetQueryParam(parameterToken, token)
                //.SetQueryParam(parameterRefreshToken, refreshToken)
                .SetQueryParam(AzureApplication.QueryRequestIdentfier, authorization.authorizationRef.id.ToString());
            return redirectUrl;
        }

        public EastFive.Security.SessionServer.Context AzureContext
        {
            get
            {
                return new EastFive.Security.SessionServer.Context(
                    () => new EastFive.Security.SessionServer.Persistence.DataContext(
                        EastFive.Azure.AppSettings.ASTConnectionStringKey));
            }
        }
        
        public TResult StoreMonitoring<TResult>(
            Func<StoreMonitoringDelegate, TResult> onMonitorUsingThisCallback,
            Func<TResult> onNoMonitoring)
        {
            StoreMonitoringDelegate callback = (monitorRecordId, authenticationId, when, method, controllerName, queryString) =>
                EastFive.Api.Azure.Monitoring.MonitoringDocument.CreateAsync(monitorRecordId, authenticationId,
                        when, method, controllerName, queryString, 
                        AzureContext.DataContext.AzureStorageRepository,
                        () => true);
            return onMonitorUsingThisCallback(callback);
        }

        public delegate Task<HttpResponseMessage> ExecuteAsyncDelegate(DateTime whenRequested, Action<double, string> updateProgress);

        private class ExecuteAsyncWrapper : IExecuteAsync
        {
            public DateTime when;
            public Expression<ExecuteAsyncDelegate> callback { get; set; }

            public bool ForceBackground => false;

            public Task<HttpResponseMessage> InvokeAsync(Action<double> updateCallback)
            {
                return callback.Compile().Invoke(
                    when,
                    (progress, msg) =>
                    {
                        updateCallback(progress);
                    });
            }
        }

        public IExecuteAsync ExecuteBackground(
            Expression<ExecuteAsyncDelegate> callback)
        {
            var wrapper = new ExecuteAsyncWrapper();
            wrapper.callback = callback;
            wrapper.when = DateTime.UtcNow;
            return wrapper;
        }
    }
}

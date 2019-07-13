﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;

using EastFive.Linq;
using BlackBarLabs.Api;
using BlackBarLabs.Extensions;
using BlackBarLabs.Api.Resources;
using System.Web.Http.Routing;
using EastFive.Security.SessionServer;
using EastFive.Extensions;
using EastFive.Api.Azure.Credentials.Attributes;
using System.Web.Http;
using Microsoft.ApplicationInsights;
using BlackBarLabs;
using EastFive.Linq.Async;
using BlackBarLabs.Linq.Async;
using EastFive.Api.Controllers;
using EastFive.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage;

namespace EastFive.Api.Azure
{
    public class AzureApplication : EastFive.Api.HttpApplication
    {
        public const string QueryRequestIdentfier = "request_id";

        public TelemetryClient Telemetry { get; private set; }

        public AzureApplication()
            : base()
        {
            Telemetry = EastFive.Web.Configuration.Settings.GetString(EastFive.Azure.AppSettings.ApplicationInsightsKey,
                (applicationInsightsKey) => new TelemetryClient { InstrumentationKey = applicationInsightsKey },
                (why) => new TelemetryClient());

            this.AddInstigator(typeof(Security.SessionServer.Context),
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
        }

        public virtual async Task<bool> CanAdministerCredentialAsync(Guid actorInQuestion, Api.Controllers.SessionToken security)
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
        public virtual Task SendServiceBusMessageAsync(string queueName, string contents)
        {
            return SendServiceBusMessageAsync(queueName, new[] { contents });
        }

        public virtual async Task SendServiceBusMessageAsync(string queueName, IEnumerable<string> contents)
        {
            const int payloadSize = 262_144;
            const int perMessageContainerSize = 70;

            if (!contents.Any())
                return;

            var client = EastFive.Web.Configuration.Settings.GetString(EastFive.Security.SessionServer.Configuration.AppSettings.ServiceBusConnectionString,
                (connectionString) =>
                {
                    return new Microsoft.Azure.ServiceBus.QueueClient(connectionString, queueName);
                },
                (why) => throw new Exception(why));

            try
            {
                var messages = contents
                    .Select(
                        content =>
                        {
                            var bytes = Encoding.UTF8.GetBytes(content);
                            return new Microsoft.Azure.ServiceBus.Message(bytes);
                        })
                    .ToArray();

                var first = messages[0];
                var remaining = messages.Skip(1).ToArray();
                await client.SendAsync(first);

                if (remaining.Length == 0)
                    return;

                var sizeFirst = first.Size;
                var numberInBatch = payloadSize / ((sizeFirst * 2) + perMessageContainerSize); // fuzzy attempt to batch send as many as possible
                var batches = await remaining
                    .Select((x, index) => new { x, index }) 
                    .GroupBy(x => x.index / numberInBatch, y => y.x)
                    .Select(
                        async g =>
                        {
                            var items = g.ToList();
                            await client.SendAsync(items);
                            return items.Count;
                        })
                    .WhenAllAsync(5);

                var sent = 1 + batches.Sum();
            }
            finally
            {
                await client.CloseAsync();
            }
        }

        public virtual async Task SendQueueMessageAsync(string queueName, byte[] byteContent)
        {
            var appQueue = EastFive.Web.Configuration.Settings.GetString("EastFive.Azure.StorageTables.ConnectionString",
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

        public virtual Web.Services.ISendMessageService SendMessageService { get => Web.Services.ServiceConfiguration.SendMessageService(); }
        
        public virtual Web.Services.ITimeService TimeService { get => Web.Services.ServiceConfiguration.TimeService(); }
        
        internal virtual WebId GetActorLink(Guid actorId, UrlHelper url)
        {
            return Security.SessionServer.Library.configurationManager.GetActorLink(actorId, url);
        }

        internal virtual Task<TResult> GetActorNameDetailsAsync<TResult>(Guid actorId,
            Func<string, string, string, TResult> onActorFound,
            Func<TResult> onActorNotFound)
        {
            return Security.SessionServer.Library.configurationManager.GetActorNameDetailsAsync(actorId, onActorFound, onActorNotFound);
        }

        public virtual async Task<TResult> GetRedirectUriAsync<TResult>(Guid requestId,
                Guid? accountIdMaybe, IDictionary<string, string> authParams,
                EastFive.Azure.Auth.Method method, EastFive.Azure.Auth.Authorization authorization,
                Uri baseUri,
                IProvideAuthorization authorizationProvider,
            Func<Uri, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure)
        {
            if(!(authorizationProvider is Credentials.IProvideRedirection))
                return await ComputeRedirectAsync(requestId, accountIdMaybe, authParams, 
                        method, authorization,
                        baseUri, authorizationProvider,
                    onSuccess,
                    onInvalidParameter,
                    onFailure);

            var redirectionProvider = authorizationProvider as Credentials.IProvideRedirection;
            return await await redirectionProvider.GetRedirectUriAsync(accountIdMaybe, authorizationProvider, authParams,
                        method, authorization,
                        baseUri, this,
                    async (redirectUri) =>
                    {
                        var fullUri = await ResolveAbsoluteUrlAsync(baseUri, redirectUri, accountIdMaybe);
                        var redirectDecorated = this.SetRedirectParameters(requestId, authorization, fullUri);
                        return onSuccess(redirectDecorated);
                    },
                    () => ComputeRedirectAsync(requestId, accountIdMaybe, authParams,
                            method, authorization,
                            baseUri, authorizationProvider,
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

        private async Task<TResult> ComputeRedirectAsync<TResult>(Guid requestId,
                Guid? accountIdMaybe, IDictionary<string, string> authParams,
                EastFive.Azure.Auth.Method method, EastFive.Azure.Auth.Authorization authorization,
                Uri baseUri,
                IProvideAuthorization authorizationProvider,
            Func<Uri, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<string, TResult> onFailure)
        {
            if (!authorization.LocationAuthenticationReturn.IsDefaultOrNull())
            {
                if (authorization.LocationAuthenticationReturn.IsAbsoluteUri)
                {
                    var redirectUrl = SetRedirectParameters(requestId, authorization, authorization.LocationAuthenticationReturn);
                    return onSuccess(redirectUrl);
                }
            }

            if (null != authParams && authParams.ContainsKey(Security.SessionServer.Configuration.AuthorizationParameters.RedirectUri))
            {
                Uri redirectUri;
                var redirectUriString = authParams[Security.SessionServer.Configuration.AuthorizationParameters.RedirectUri];
                if (!Uri.TryCreate(redirectUriString, UriKind.Absolute, out redirectUri))
                    return onInvalidParameter("REDIRECT", $"BAD URL in redirect call:{redirectUriString}");
                var redirectUrl = SetRedirectParameters(requestId, authorization, redirectUri);
                return onSuccess(redirectUrl);
            }

            return await EastFive.Web.Configuration.Settings.GetUri(
                EastFive.Security.SessionServer.Configuration.AppSettings.LandingPage,
                (redirectUriLandingPage) =>
                {
                    var redirectUrl = SetRedirectParameters(requestId, authorization, redirectUriLandingPage);
                    return onSuccess(redirectUrl);
                },
                (why) => onFailure(why)).ToTask();
        }

        protected Uri SetRedirectParameters(Guid requestId, EastFive.Azure.Auth.Authorization authorization, Uri redirectUri)
        {
            var redirectUrl = redirectUri
                //.SetQueryParam(parameterAuthorizationId, authorizationId.Value.ToString("N"))
                //.SetQueryParam(parameterToken, token)
                //.SetQueryParam(parameterRefreshToken, refreshToken)
                .SetQueryParam(AzureApplication.QueryRequestIdentfier, authorization.authorizationRef.id.ToString());
            return redirectUrl;
        }

        public Security.SessionServer.Context AzureContext
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

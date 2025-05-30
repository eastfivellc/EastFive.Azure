﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Reflection;

using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc.Razor;

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

using EastFive.Linq;
using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Collections.Generic;
using EastFive.Azure.Auth;
using EastFive.Azure.Monitoring;
using EastFive.Web.Configuration;
using EastFive.Api;
using EastFive.Azure.Auth.CredentialProviders;
using Azure.Messaging.ServiceBus;
using System.Collections.Concurrent;

namespace EastFive.Azure
{
    public interface IAuthApplication : IApiApplication
    {
        IDictionary<string, IProvideLogin> LoginProviders { get; }

        Task<TResult> GetRedirectUriAsync<TResult>(
                Guid? accountIdMaybe, IDictionary<string, string> authParams,
                Method method, EastFive.Azure.Auth.Authorization authorization,
                IHttpRequest request, IInvokeApplication endpoints,
                Uri baseUri, IProvideAuthorization authorizationProvider,
            Func<Uri, Func<IHttpResponse, IHttpResponse>, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<TResult> onInvalidAccount,
            Func<string, TResult> onFailure);

        Task<bool> CanAdministerCredentialAsync(Guid actorInQuestion, EastFive.Azure.Auth.SessionToken security);

        Task<bool> ShouldAuthorizeIntegrationAsync(XIntegration integration, EastFive.Azure.Auth.Authorization authorization);

        Task<TResult> GetActorNameDetailsAsync<TResult>(Guid actorId,
            Func<string, string, string, TResult> onActorFound,
            Func<TResult> onActorNotFound);
    }

    public interface IAzureApplication : IAuthApplication
    {
        EastFive.Api.IInvokeApplication CDN { get; }

        TelemetryClient Telemetry { get; }

        Task SendServiceBusMessageAsync(string queueName, string messageId, byte[] bytes);

        Task SendServiceBusMessageAsync(string queueName, IEnumerable<KeyValuePair<string, byte[]>> listOfBytes);
    }
}

namespace EastFive.Api.Azure
{

    [ApiResources(NameSpacePrefixes = "EastFive.Azure,EastFive.Search")]
    public class AzureApplication : EastFive.Api.HttpApplication, EastFive.Azure.IAzureApplication
    {
        public const string QueryRequestIdentfier = "request_id";
        public const string ParameterRedirectUrl = "REDIRECT_URI";

        public TelemetryClient Telemetry { get; private set; }

        public AzureApplication(IConfiguration configuration)
            : base(configuration)
        {

        }

        protected override void ConfigureCallback(IApplicationBuilder app, IHostEnvironment env, IRazorViewEngine razorViewEngine)
        {
            base.ConfigureCallback(app, env, razorViewEngine);

            Telemetry = EastFive.Azure.AppSettings.ApplicationInsights.InstrumentationKey.LoadTelemetryClient();

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

        public virtual async Task<bool> CanAdministerCredentialAsync(Guid actorInQuestion, EastFive.Azure.Auth.SessionToken security)
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

        private IDictionary<string, IProvideLogin> loginProviders;
        public IDictionary<string, IProvideLogin> LoginProviders
        {
            get
            {
                if(loginProviders.IsDefaultOrNull())
                {
                    var countDown = 1000;
                    while (!this.IsInitialized)
                    {
                        if (countDown <= 0)
                            break;
                        countDown--;
                        Thread.Sleep(10);
                    }
                }
                return loginProviders;
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

        public virtual Task SendServiceBusMessageAsync(string queueName, string messageId, byte[] bytes)
        {
            return SendServiceBusMessageStaticAsync(queueName, messageId, new[] { bytes });
        }

        public virtual Task SendServiceBusMessageAsync(string queueName, IEnumerable<KeyValuePair<string, byte[]>> listOfBytes)
        {
            return Task.WhenAll(listOfBytes
                .Select(kvp => SendServiceBusMessageAsync(queueName, kvp.Key, kvp.Value))
                .ToArray());
        }

        private static readonly Lazy<ServiceBusClient> serviceBusClient = new Lazy<ServiceBusClient>(
            () =>EastFive.Azure.AppSettings.ServiceBusConnectionString.ConfigurationString(
                (connectionString) => new ServiceBusClient(connectionString),
                (why) => throw new Exception(why)),
            true);

        private static readonly ConcurrentDictionary<string, ServiceBusSender> serviceBusSenders = new ConcurrentDictionary<string, ServiceBusSender>();

        // The message id is useful in case duplicate message detection is enabled on the service bus queue
        public static async Task SendServiceBusMessageStaticAsync(string queueName, string messageId, IEnumerable<byte[]> listOfBytes, Func<string> getSessionId = default)
        {
            const int maxPayloadSize = 262_144;
            const int perMessageHeaderSize = 58;
            const int perMessageListSize = 8;

            if (!listOfBytes.Any())
                return;

            var sender = serviceBusSenders.GetOrAdd(queueName, serviceBusClient.Value.CreateSender);

            // The padding was estimated by this:
            //var bodyLen = 131_006;  // (131_006 + 58 + 8) * 2 messages = 262_144
            //var body = Enumerable.Range(0, bodyLen).Select(x => (byte)5).ToArray();
            //var msg1 = new Microsoft.Azure.ServiceBus.Message(body);
            //var msg2 = new Microsoft.Azure.ServiceBus.Message(body);
            //await client.SendAsync(new List<Microsoft.Azure.ServiceBus.Message> { msg1, msg2 });
            // If the send is successful, we haven't exceeded the max payload

            var messages = listOfBytes
                .Select(bytes => new ServiceBusMessage(bytes)
                {
                    MessageId = messageId,
                    SessionId = getSessionId != default ? getSessionId() : default(string),
                    ContentType = "application/octet-stream",
                })
                .ToArray();
            var maxMessageSize = messages.Select(x => x.Body.ToArray().Length + perMessageHeaderSize + perMessageListSize).Max();
            int numberInBatch = maxPayloadSize / maxMessageSize;
            do
            {
                var toBeSent = messages.Take(numberInBatch).ToArray();
                try
                {
                    await sender.SendMessagesAsync(toBeSent);
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
                    throw;
                }
            } while (messages.Length > 0);
        }

        //public virtual async Task<SendReceipt> SendQueueMessageAsync(string queueName, byte[] byteContent)
        //{
        //    var appQueue = await EastFive.Azure.AppSettings.Persistence.StorageTables.ConnectionString.ConfigurationString(
        //        async (connString) =>
        //        {
        //            var queueClient = new QueueClient(connString, queueName);
        //            await queueClient.CreateIfNotExistsAsync();
        //            return queueClient;
        //        },
        //        (why) => throw new Exception(why));

        //    var receipt = await appQueue.SendMessageAsync(byteContent.ToBase64String());
        //    return receipt.Value;
        //}

        //public virtual async Task<TResult> GetNextQueueMessageAsync<TResult>(string queueName,
        //    Func<byte[],    // content
        //        Func<Task>, // dequeueAsync
        //        Task<TResult>> onNextMessage,
        //    Func<TResult> onEmpty)
        //{
        //    var appQueue = await EastFive.Azure.AppSettings.ASTConnectionStringKey.ConfigurationString(
        //        async (connString) =>
        //        {
        //            var queueClient = new QueueClient(connString, queueName);
        //            await queueClient.CreateIfNotExistsAsync();
        //            return queueClient;
        //        },
        //        (why) => throw new Exception(why));

        //    var response = await appQueue.ReceiveMessageAsync();
        //    if (null == response)
        //        return onEmpty();
        //    if (response.Value.IsDefaultOrNull())
        //        return onEmpty();

        //    var message = response.Value;
        //    return await onNextMessage(
        //        message.Body.ToArray(),
        //        () => appQueue.DeleteMessageAsync(message.MessageId, message.PopReceipt));
        //}

        protected override async Task<Initialized> InitializeAsync()
        {
            var applicationType = this.GetType();
            var loginProviderProviders = applicationType
                .GetAttributesInterface<IProvideLoginProvider>(true, true)
                .ToArray();
            var loginProviders = await loginProviderProviders
                .Select(
                    async loginProviderProvider =>
                    {
                        try
                        {
                            return await loginProviderProvider.ProvideLoginProviderAsync(
                                loginProvider => (true, loginProvider),
                                (why) => (false, default(IProvideLogin)));
                        }
                        catch (Exception)
                        {
                            return (false, default(IProvideLogin));
                        }
                    })
                .AsyncEnumerable()
                .SelectWhere()
                .ToArrayAsync();

            this.loginProviders = loginProviders.ToDictionary(ktem => ktem.Method);

            var initialized = await base.InitializeAsync();
            return initialized;
        }

        //internal virtual IManageAuthorizationRequests AuthorizationRequestManager
        //{
        //    get
        //    {
        //        return new AzureStorageTablesLogAuthorizationRequestManager();
        //    }
        //}

        internal async Task<TResult> GetAuthorizationProviderAsync<TResult>(string method,
            Func<IProvideAuthorization, TResult> onSuccess,
            Func<TResult> onCredentialSystemNotAvailable,
            Func<string, TResult> onFailure)
        {
            return await this.AuthorizationProviders.TryGetValueAsync(method,
                onSuccess,
                onCredentialSystemNotAvailable);
        }
        
        internal TResult GetLoginProvider<TResult>(string method,
            Func<IProvideLogin, TResult> onSuccess,
            Func<TResult> onCredentialSystemNotAvailable,
            Func<string, TResult> onFailure)
        {
            // this.InitializationWait();
            if(this.LoginProviders.ContainsKey(method))
            {
                return onSuccess(this.LoginProviders[method]);
            }
            return onCredentialSystemNotAvailable();
        }
        
        public virtual Web.Services.ISendMessageService SendMessageService
        { get => Web.Services.ServiceConfiguration.SendMessageService(); }
        
        public virtual Web.Services.ITimeService TimeService { get => Web.Services.ServiceConfiguration.TimeService(); }
        
        //internal virtual Uri GetActorLink(Guid actorId, IProvideUrl url)
        //{
        //    return Library.configurationManager.GetActorLink(actorId, url).Source;
        //}

        public virtual Task<TResult> GetActorNameDetailsAsync<TResult>(Guid actorId,
            Func<string, string, string, TResult> onActorFound,
            Func<TResult> onActorNotFound)
        {
            throw new NotImplementedException();
            // return EastFive.Security.SessionServer.Library.configurationManager.GetActorNameDetailsAsync(actorId, onActorFound, onActorNotFound);
        }

        public virtual async Task<TResult> GetRedirectUriAsync<TResult>(
                Guid? accountIdMaybe, IDictionary<string, string> authParams, 
                Method method, EastFive.Azure.Auth.Authorization authorization,
                IHttpRequest request, IInvokeApplication endpoints,
                Uri baseUri, IProvideAuthorization authorizationProvider,
            Func<Uri, Func<IHttpResponse, IHttpResponse>, TResult> onSuccess,
            Func<string, string, TResult> onInvalidParameter,
            Func<TResult> onInvalidAccount,
            Func<string, TResult> onFailure)
        {
            async Task<TResult> finishUrlAsync(Uri redirect,
                KeyValuePair<string,string>[] kvps = default,
                Func<EastFive.Azure.Auth.Authorization, Uri,Uri> authDecorator = default)
            {
                var (modifier, fullUri) = await ResolveAbsoluteUrlAsync(redirect,
                        request, accountIdMaybe, authParams);
                foreach (var kvp in kvps.NullToEmpty())
                    fullUri = fullUri.SetQueryParam(kvp.Key, kvp.Value);

                if (authDecorator == default)
                    return onSuccess(fullUri, x => x);

                var redirectDecorated = authDecorator(authorization, fullUri);
                return onSuccess(redirectDecorated, modifier);
            }

            if (!(authorizationProvider is IProvideRedirection))
                return await await ComputeRedirectAsync(accountIdMaybe, authParams, 
                        method, authorization, endpoints,
                        authorizationProvider,
                    (fullUri) => finishUrlAsync(fullUri),
                    onInvalidParameter.AsAsyncFunc(),
                    onFailure.AsAsyncFunc());

            var redirectionProvider = authorizationProvider as IProvideRedirection;
            return await await redirectionProvider.GetRedirectUriAsync(accountIdMaybe, 
                    authorizationProvider, authParams,
                    method, authorization,
                    this, request, endpoints, baseUri,
                (redirectUri, kvps) => finishUrlAsync(redirectUri, kvps, SetRedirectParameters),
                async () => await await ComputeRedirectAsync(accountIdMaybe, authParams,
                        method, authorization, endpoints,
                        authorizationProvider,
                    (fullUri) => finishUrlAsync(fullUri),
                    onInvalidParameter.AsAsyncFunc(),
                    onFailure.AsAsyncFunc()),
                onInvalidParameter.AsAsyncFunc(),
                onInvalidAccount.AsAsyncFunc(),
                onFailure.AsAsyncFunc());
        }

        public virtual Task<(Func<IHttpResponse, IHttpResponse>, Uri)> ResolveAbsoluteUrlAsync(Uri relativeUri,
            IHttpRequest request, Guid? accountIdMaybe, IDictionary<string, string> authParams)
        {
            var fullUriStart = new Uri(request.RequestUri, relativeUri);
            Func<IHttpResponse, IHttpResponse> noModifications = m => m;
            return this.GetType()
                .GetAttributesInterface<IResolveRedirection>(inherit: true, multiple: true)
                .Distinct(attr => attr.GetType().FullName) // Issue with duplicate attributes due to Global.asax class
                .OrderBy(attr => attr.Order)
                .Aggregate((noModifications, fullUriStart).AsTask(),
                    async (relUriTask, redirResolver) =>
                    {
                        var (modifier, fullUri) = await relUriTask;
                        var (nextModifier, nextfullUri) = await redirResolver.ResolveAbsoluteUrlAsync(fullUri, 
                            request, accountIdMaybe, authParams);
                        Func<IHttpResponse, IHttpResponse> combinedModifier = (response) =>
                        {
                            var nextResponse = nextModifier(response);
                            return modifier(nextResponse);
                        };
                        return (combinedModifier, nextfullUri);
                    });
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
                else
                {
                    var redirectUrl = EastFive.Web.Configuration.Settings.GetUri(
                        EastFive.Azure.AppSettings.Auth.LandingPage,
                        (landingPage) => new Uri(landingPage, authorization.LocationAuthenticationReturn),
                        (why) => default(Uri));
                    if (default != redirectUrl)
                    {
                        redirectUrl = SetRedirectParameters(authorization, redirectUrl);
                        return onSuccess(redirectUrl);
                    }
                }
            }

            if (null != authParams && authParams.ContainsKey(ParameterRedirectUrl))
            {
                Uri redirectUri;
                var redirectUriString = authParams[ParameterRedirectUrl];
                if (!Uri.TryCreate(redirectUriString, UriKind.Absolute, out redirectUri))
                    return onInvalidParameter("REDIRECT", $"BAD URL in redirect call:{redirectUriString}");
                var redirectUrl = SetRedirectParameters(authorization, redirectUri);
                return onSuccess(redirectUrl);
            }

            return await EastFive.Web.Configuration.Settings.GetUri(
                EastFive.Azure.AppSettings.Auth.LandingPage,
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
        
        public TResult StoreMonitoring<TResult>(
            Func<StoreMonitoringDelegate, TResult> onMonitorUsingThisCallback,
            Func<TResult> onNoMonitoring)
        {
            StoreMonitoringDelegate callback = (monitorRecordId, authenticationId, when, method, controllerName, queryString) =>
                EastFive.Api.Azure.Monitoring.MonitoringDocument.CreateAsync(monitorRecordId, authenticationId,
                        when, method, controllerName, queryString, 
                        () => true);
            return onMonitorUsingThisCallback(callback);
        }

        public delegate Task<IHttpResponse> ExecuteAsyncDelegate(DateTime whenRequested, Action<double, string> updateProgress);

        private class ExecuteAsyncWrapper : IExecuteAsync
        {
            public DateTime when;
            public Expression<ExecuteAsyncDelegate> callback { get; set; }

            public bool ForceBackground => false;

            public Task<IHttpResponse> InvokeAsync(Action<double> updateCallback)
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

        public virtual Task<TResult> CreateHijackableAuthorizationAsync<TResult>(Guid account,
            Func<EastFive.Azure.Auth.Authorization, TResult> onCreated,
            Func<TResult> onDisabled,
            Func<TResult> onNotFound,
            Func<string, TResult> onFailure)
        {
            return onDisabled().AsTask();
        }
    }
}

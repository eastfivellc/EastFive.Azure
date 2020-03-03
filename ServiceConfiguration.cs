using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Collections.Concurrent;
using System.Web.Http.Routing;

using BlackBarLabs;
using BlackBarLabs.Api;
using BlackBarLabs.Extensions;
using BlackBarLabs.Linq;
using EastFive.Linq;
using System.IO;
using System.IO.Compression;
using EastFive.Collections.Generic;
using System.Linq.Expressions;
using BlackBarLabs.Api.Resources;
using EastFive.Api.Azure.Credentials.Attributes;
using EastFive.Api.Azure.Credentials.Controllers;
using EastFive.Extensions;

namespace EastFive.Security.SessionServer
{
    public static class ServiceConfiguration
    {
        public delegate object IntegrationActivityDelegate(EastFive.Azure.Integration integration,
            Func<object, Task<object>> unlockAsync,
            Func<string, Task<object>> unlockWithIssueAsync);

        public delegate EastFive.Azure.Synchronization.Connections ConnectionActivityDelegate(object activity);
        
        internal static Dictionary<string, IProvideAuthorization> credentialProviders =
            default(Dictionary<string, IProvideAuthorization>);

        internal static Dictionary<string, IProvideLogin> loginProviders =
            default(Dictionary<string, IProvideLogin>);

        internal static Dictionary<string, IProvideLoginManagement> managementProviders =
            default(Dictionary<string, IProvideLoginManagement>);
        
        internal static IDictionary<Type, IDictionary<string, IntegrationActivityDelegate[]>> integrationActivites =
            new Dictionary<Type, IDictionary<string, IntegrationActivityDelegate[]>>();

        internal static IDictionary<string, IDictionary<Type, ConnectionActivityDelegate[]>> connections =
            new Dictionary<string, IDictionary<Type, ConnectionActivityDelegate[]>>();
        

        public static async Task<TResult> InitializeAsync<TResult>(IConfigureIdentityServer configurationManager,
                HttpConfiguration config,
                Func<
                    Func<IProvideAuthorization, IProvideAuthorization[]>, // onProvideAuthorization
                    Func<IProvideAuthorization[]>, // onProvideNothing
                    Func<string, IProvideAuthorization[]>, // onFailure
                    Task<IProvideAuthorization[]>> [] initializers,
                Expression<IntegrationActivityDelegate>[] activities,
                Expression<ConnectionActivityDelegate>[] connections,
            Func<TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            Library.configurationManager = configurationManager;
            
            //AddExternalControllers<Azure.Auth.OpenIdResponse>(config);
            //config.Routes.MapHttpRoute(name: "apple-app-links",
            //    routeTemplate: "apple-app-site-association",
            //    defaults: new { controller = "AppleAppSiteAssociation", id = RouteParameter.Optional });

            var credentialProvidersWithoutMethods = await initializers.Aggregate(
                (new IProvideAuthorization[] { }).ToTask(),
                async (providersTask, initializer) =>
                    {
                        var providers = await providersTask;
                        return await initializer(
                            (service) => providers.Append(service).ToArray(),
                            () => providers,
                            (why) => providers);
                    });
            credentialProviders = credentialProvidersWithoutMethods
                .ToDictionary(
                    credentialProvider =>
                    {
                        var methodName = credentialProvider.GetType().GetCustomAttribute<IntegrationNameAttribute, string>(
                            attr => attr.Name,
                            () => throw new Exception($"{credentialProvider.GetType().FullName} does not have IntegrationName attribute set"));
                        return methodName;
                    },
                    credentialProvider => credentialProvider);
            loginProviders = credentialProvidersWithoutMethods
                .Where(credentialProvider => typeof(IProvideLogin).IsAssignableFrom(credentialProvider.GetType()))
                .ToDictionary(
                    credentialProvider => credentialProvider.GetType().GetCustomAttribute<IntegrationNameAttribute, string>(
                        attr => attr.Name,
                        () => throw new Exception($"{credentialProvider.GetType().FullName} does not have IntegrationName attribute set")),
                    credentialProvider => (IProvideLogin)credentialProvider);
            managementProviders = credentialProviders
                .Where(credentialProvider => typeof(IProvideLoginManagement).IsAssignableFrom(credentialProvider.Value.GetType()))
                .ToDictionary(
                    credentialProvider => credentialProvider.Key,
                    credentialProvider => (IProvideLoginManagement)credentialProvider.Value);

            //var spaZipPath = System.Web.Hosting.HostingEnvironment.MapPath("~/Spa.zip");
            //var zipArchive = ZipFile.OpenRead(spaZipPath);

            //var lookupSpaFile = zipArchive.Entries
            //    .Select(
            //        entity => entity.FullName.PairWithValue(entity.Open().ToBytes()))
            //    .ToDictionary();

            integrationActivites = activities
                .Where(activity => activity.Body is System.Linq.Expressions.MethodCallExpression)
                .SelectMany(
                    (activity) =>
                    {
                        var method = (activity.Body as System.Linq.Expressions.MethodCallExpression).Method;
                        return method.DeclaringType.GetInterfaces()
                            .FlatMap(
                                (conformsTo, nextItem, skipItem) => method.GetCustomAttribute(
                                    (IntegrationNameAttribute integrationNameAttr) => nextItem(
                                        conformsTo.PairWithValue(integrationNameAttr.Name).PairWithValue(activity.Compile())),
                                    () => skipItem()),
                                (IEnumerable<KeyValuePair<KeyValuePair<Type, string>, IntegrationActivityDelegate>> activityKvps) => activityKvps);
                    })
                .GroupBy(activityKvp => activityKvp.Key.Key)
                .Select(
                    grp => grp.Key.PairWithValue(
                        (IDictionary<string, IntegrationActivityDelegate[]>)grp
                            .Select(item => item.Key.Value.PairWithValue(item.Value))
                            .GroupBy(kvp => kvp.Key)
                            .Select(grpInner => grpInner.Key.PairWithValue(grpInner.SelectValues().ToArray()))
                            .ToDictionary()))
                .ToDictionary();

            ServiceConfiguration.connections = connections
                .Where(activity => activity.Body is System.Linq.Expressions.MethodCallExpression)
                .FlatMap(
                    (activity, next, skip) =>
                    {
                        var method = (activity.Body as System.Linq.Expressions.MethodCallExpression).Method;
                        var paramType = method.GetParameters().First().ParameterType;
                        return method.GetCustomAttribute(
                            (IntegrationNameAttribute integrationNameAttr) => 
                                        next(integrationNameAttr.Name.PairWithValue(paramType).PairWithValue(activity.Compile())),
                            () => skip());
                    },
                    (IEnumerable<KeyValuePair<KeyValuePair<string, Type>, ConnectionActivityDelegate>> operations) =>
                    {
                        try
                        {
                            var conns = operations
                                .GroupBy(activityKvp => activityKvp.Key.Key)
                                .Select(
                                    grp =>
                                    {
                                        var connActivitiesByType = (IDictionary<Type, ConnectionActivityDelegate[]>)grp
                                            .Select(item => item.Key.Value.PairWithValue(item.Value))
                                            .GroupBy(kvp => kvp.Key)
                                            .Select(grpInner => grpInner.Key.PairWithValue(grpInner.SelectValues().ToArray()))
                                            .ToDictionary();
                                        return grp.Key.PairWithValue(connActivitiesByType);
                                    })
                                .ToDictionary();
                            return conns;
                        } catch(Exception ex)
                        {
                            throw ex; 
                        }
                    });
            
            return onSuccess();
        }
        
        internal static async Task<TResult> ConnectionsAsync<TResult>(Guid integrationId, string resourceType,
            Func<EastFive.Azure.Integration, Azure.Synchronization.Connections[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            var context = Context.LoadFromConfiguration();
            return await await context.Integrations.GetAuthenticatedByIdAsync(integrationId,
                async (integration) =>
                {
                    if (resourceType.IsNullOrWhiteSpace() || (!connections.ContainsKey(resourceType)))
                        return onFound(integration, new Azure.Synchronization.Connections[] { });
                    var connectionInitializerss = connections[resourceType];
                    
                    return await connectionInitializerss
                        .FlatMap(
                            async (connectionInitializersKvp, next, skip) =>
                            {
                                var connectionInitializerType = connectionInitializersKvp.Key;
                                var connectionInitializers = connectionInitializersKvp.Value;
                                var integrationConnectionInitializations = await context.Integrations.GetActivityAsync(integrationId, connectionInitializersKvp.Key);
                                var connections = integrationConnectionInitializations.Value
                                    .SelectMany(
                                        connectionInitialization => connectionInitializers
                                            .Select(connectionInitializer => connectionInitializer(connectionInitialization)))
                                    .ToArray();
                                return await next(connections);
                            },
                            (IEnumerable<Azure.Synchronization.Connections[]> connectionss) =>
                            {
                                return onFound(integration, connectionss.SelectMany().ToArray()).ToTask();
                            });
                },
                onNotFound.AsAsyncFunc());
        }

        internal static Task<TResult> IntegrationResourceTypesAsync<TResult>(Guid integrationId,
            Func<string[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            var context = Context.LoadFromConfiguration();
            return connections
                .SelectKeys()
                .FlatMap(
                    (resourceType, nextResourceType, skip) =>
                    {
                        var connectionInitializerss = connections[resourceType];
                        return connectionInitializerss
                            .First(
                                async (connectionInitializersKvp, nextInitializer) =>
                                {
                                    var connectionInitializerType = connectionInitializersKvp.Key;
                                    var connectionInitializers = connectionInitializersKvp.Value;
                                    var integrationConnectionInitializations = await context.Integrations.GetActivityAsync(integrationId, connectionInitializersKvp.Key);
                                    var connections = integrationConnectionInitializations.Value
                                        .SelectMany(
                                            connectionInitialization => connectionInitializers
                                                .Select(connectionInitializer => connectionInitializer(connectionInitialization)))
                                        .ToArray();
                                    return await connections
                                        .First(
                                            (connection, nextConnection) => (connection.ResourceType == resourceType) ?
                                                nextResourceType(resourceType)
                                                :
                                                nextConnection(),
                                            () => nextInitializer());
                                },
                                () =>
                                {
                                    return skip();
                                });
                    },
                    (IEnumerable<string> resourceTypes) => onFound(resourceTypes.ToArray()).ToTask());
        }

        private static void AddExternalControllers<TController>(HttpConfiguration config)
           where TController : ApiController
        {
            var routes = typeof(TController)
                .GetCustomAttributes<RoutePrefixAttribute>()
                .Select(routePrefix => routePrefix.Prefix)
                .Distinct();

            foreach (var routePrefix in routes)
            {
                config.Routes.MapHttpRoute(
                    name: routePrefix,
                    routeTemplate: routePrefix + "/{controller}/{id}",
                    defaults: new { id = RouteParameter.Optional });
            }

            //var assemblyRecognition = new InjectableAssemblyResolver(typeof(TController).Assembly,
            //    config.Services.GetAssembliesResolver());
            //config.Services.Replace(typeof(System.Web.Http.Dispatcher.IAssembliesResolver), assemblyRecognition);
        }
    }
}

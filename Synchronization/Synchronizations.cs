using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using BlackBarLabs;
using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Security.SessionServer;
using EastFive.Text;

namespace EastFive.Azure.Synchronization
{
    public static class Synchronizations
    {
        internal static async Task<TResult> GetServicesAsync<TResult>(Guid actorId,
            Func<KeyValuePair<EastFive.Azure.Integration, Connections>[], Task<TResult>> onSuccess)
        {
            var context = Context.LoadFromConfiguration();
            var integrations = await context.Integrations.GetActivitiesAsync<Connections>(actorId);
            return await onSuccess(integrations);
        }

        public static Task<TResult> ImportByActorServiceAndResourceAsync<TResult>(Guid actorId,
                string serviceName, string resourceType,
                Guid actorPerformingAs, System.Security.Claims.Claim[] claims,
            Func<EastFive.Azure.Synchronization.Connection[], TResult> onFound,
            Func<TResult> onUnsupportedResourceType,
            Func<TResult> onReferenceNotFound,
            Func<TResult> onUnauthorized)
        {
            // Get all external items
            return GetServicesAsync(actorId,
                (serviceKvps) =>
                {
                    return serviceKvps.SelectReduce<KeyValuePair<EastFive.Azure.Integration, Connections>, EastFive.Azure.Synchronization.Connection[], Task<TResult>>(
                        async (serviceKvp, next) =>
                        {
                            //var service = serviceKvp.Value;
                            //if (resourceType.IsNullOrWhiteSpace() || (!connections.ContainsKey(resourceType)))
                            //{
                            //    if (resourceType.IsNullOrWhiteSpace() || (!connections.ContainsKey(resourceType)))
                            //        return onUnsupportedResourceType();

                            //    var connection = connections[resourceType](service);
                            //    return await await connection.SynchronizeAsync(
                            //            actorId, actorId, serviceKvp.Key.integrationId,
                            //        (synchronizations) => next(synchronizations.ToArray()),
                            //        (why) => next(new EastFive.Azure.Synchronization.Connection[] { }));
                            //}
                            return await next(new EastFive.Azure.Synchronization.Connection[] { });
                            //var resource = resources[resourceType];
                            //return await await resource.SynchronizeAsync(
                            //        actorId, service, serviceKvp.Key,
                            //    (synchronizations) => next(synchronizations.ToArray()),
                            //    (why) => next(new EastFive.Azure.Synchronization.Connection[] { }));
                        },
                        (synchronizationss) =>
                        {
                            return onFound(synchronizationss.SelectMany().ToArray()).AsTask();
                        });
                });
        }
        
        public static async Task<TResult> FindAdaptersByIntgrationAndResourceTypeAsync<TResult>(Guid integrationId, string resourceType,
                Guid actorPerformingAs, System.Security.Claims.Claim[] claims,
            Func<EastFive.Azure.Synchronization.Adapter[], TResult> onFound,
            Func<TResult> onNotFound,
            Func<TResult> onUnsupportedResourceType,
            Func<TResult> onUnauthorizedIntegration,
            Func<TResult> onUnauthorized,
            Func<string, TResult> onFailure)
        {
            return await await ServiceConfiguration.ConnectionsAsync(integrationId, resourceType,
                async (integration, connections) =>
                {
                    if (!connections.Any())
                        return onUnsupportedResourceType();
                    var adaptersAll = await connections // There really only should be one
                        .Select(
                            connection => connection.GetAdaptersAsync(integrationId,
                                (adapters) => adapters.Select(
                                    adapter =>
                                    {
                                        adapter.integrationId = integrationId;
                                        adapter.resourceType = resourceType;
                                        return adapter;
                                    }).ToArray(),
                                why => new Adapter[] { }))
                        .WhenAllAsync()
                        .SelectManyAsync()
                        .ToArrayAsync();
                    return onFound(adaptersAll);
                },
                onNotFound.AsAsyncFunc());
        }
        
        internal static async Task<TResult> DeleteAsync<TResult>(
                string resourceKey, string resourceType, 
                Guid integrationId,
            Func<TResult> onSuccess,
            Func<TResult> onNotFound,
            Func<string, TResult> onFailure)
        {
            return await await ServiceConfiguration.ConnectionsAsync(integrationId, resourceType,
                async (integration, connections) =>
                {
                    if (!connections.Any())
                        return onFailure("Unsupported Resource Type");
                    return await connections
                        .First( // There really only should be one
                            (connection, next) =>
                            {
                                return connection.DeleteAsync(resourceType, resourceKey,
                                    onSuccess,
                                    onFailure);
                            },
                            () => onFailure("No connection activities could delete the resource.").AsTask());
                },
                onNotFound.AsAsyncFunc());
            
            
            
            //var serviceKvps = await context.Integrations.GetActivityAsync<IIntegrate>(integrationId);
            //bool[] results = await serviceKvps
            //    .Select(
            //        async (serviceKvp) =>
            //        {
            //            var service = serviceKvp.Value;
            //            var integrationName = serviceKvp.Key.method;
            //            var connection = this.connections[resourceType](service);
            //            return await connection.DeleteAsync(resourceType, resourceKey,
            //                () => true,
            //                (why) => false);
            //        })
            //    .WhenAllAsync();
            //return onSuccess();
        }
    }
}

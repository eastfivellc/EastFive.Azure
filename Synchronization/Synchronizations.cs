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
        
    }
}

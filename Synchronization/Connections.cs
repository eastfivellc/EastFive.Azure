﻿using BlackBarLabs;
using BlackBarLabs.Extensions;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Synchronization
{
    public struct Adapter
    {
        public Guid adapterId;
        public string key;
        public string name;
        public KeyValuePair<string, string>[] identifiers;
        public Guid integrationId;
        public string resourceType;
        public Guid[] connectorIds;
    }

    public struct Connector
    {
        public Guid connectorId;
        
        public Guid createdBy;

        public Guid adapterInternalId;
        public Guid adapterExternalId;

        public enum SynchronizationMethod
        {
            useInternal,
            useExternal,
            latest,
            ignore,
        }
        public SynchronizationMethod synchronizationMethod;
    }
    
    public struct Connection
    {
        public Connector connector;
        public Adapter adapterInternal;
        public Adapter adapterExternal;
    }

    public delegate Task<TResult> ResourceAllSynchronizationsAsync<TResult>(
        Func<IEnumerable<Adapter>, TResult> onFound,
        Func<string, TResult> onConnectionIssue,
        Func<string, TResult> onFailure,
        Func<TResult> onNotSupported);

    public abstract class Connections
    {
        abstract public string ResourceType { get; }

        public Task<Adapter[]> SaveAdaptersAsync(Guid integrationId, Adapter[] adapters)
        {
            var resourceType = this.ResourceType;
            return adapters
                .Select(
                    adapter => Persistence.AdapterDocument.FindOrCreateAsync(adapter.key,
                        integrationId, resourceType,
                        async (adapterInternalStorage, saveAsync) =>
                        {
                            adapterInternalStorage.name = adapter.name;
                            adapterInternalStorage.identifiers = adapter.identifiers;

                            // Update identifiers internally if there is no externally mapped resource
                            var adapterId = await saveAsync(adapterInternalStorage);
                            return adapterInternalStorage;
                        }))
                .WhenAllAsync();
        }

        public async Task<TResult> ResolveResourcesAsync<TResourceInternal, TResult>(
                TResourceInternal[] resourcesInternal, IEnumerable<Adapter> resourcesExternal,
                Guid actorId, Guid integrationIdInternal, Guid integrationIdExternal, string resourceType,
                Func<TResourceInternal, Adapter> getIdentifiersInternal,
            Func<IEnumerable<Connection>, TResult> onMatch,
            Func<string, TResult> onFailure)
            where TResourceInternal : struct
        {
            return await resourcesInternal
                .FlatMap(
                    resourcesExternal.ToDictionary(res => res.key, res => res),
                    async (resourceInternal, resourcesExternalUnmatched, next, skip) =>
                    {
                        var adapterInternal = getIdentifiersInternal(resourceInternal);
                        if (adapterInternal.key.IsNullOrWhiteSpace())
                            throw new ArgumentException("getIdentifiersInternal must return a synchronization that has a valid internal id", "getIdentifiersInternal");

                        var resourceKey = adapterInternal.key;
                        return await Persistence.AdapterDocument.FindOrCreateAsync(resourceKey, 
                                integrationIdInternal, resourceType, integrationIdExternal,
                            async (adapterInternalStorage, connectorAdapter, updateAsync) =>
                            {
                                var adapterLocal = new Adapter()
                                {
                                    key = resourceKey,
                                    name = adapterInternal.name,
                                    adapterId = adapterInternalStorage.adapterId,
                                    identifiers = adapterInternal.identifiers,
                                    integrationId = integrationIdInternal,
                                };
                                if (!connectorAdapter.HasValue)
                                {
                                    // Update identifiers internally if there is no externally mapped resource
                                    var connection = await updateAsync(adapterLocal, default(KeyValuePair<Connector, Adapter>?));
                                    return await next(connection, resourcesExternalUnmatched);
                                }
                                var adapterRemote = connectorAdapter.Value.Value;
                                var connector = connectorAdapter.Value.Key;

                                var remoteKey = adapterRemote.key;
                                if (!resourcesExternalUnmatched.ContainsKey(remoteKey))
                                {
                                    bool deleted = await Persistence.AdapterDocument.DeleteByIdAsync(adapterRemote.adapterId,
                                        () => true,
                                        () => false);
                                    var connectionMissing = await updateAsync(adapterLocal, default(KeyValuePair<Connector, Adapter>?));
                                    return await next(connectionMissing, resourcesExternalUnmatched);
                                }

                                var adapterExternal = resourcesExternalUnmatched[remoteKey];
                                var connectionUpdated = await updateAsync(adapterLocal, connector.PairWithValue(adapterExternal));
                                return await next(connectionUpdated, resourcesExternalUnmatched.Where(kvp => kvp.Key != remoteKey).ToDictionary());
                            },
                            async (saveLinkAsync) =>
                            {
                                var adapterToCreate = new Adapter()
                                {
                                    adapterId = Guid.NewGuid(),
                                    key = resourceKey,
                                    name = adapterInternal.name,
                                    identifiers = adapterInternal.identifiers,
                                    integrationId = integrationIdInternal,
                                };
                                var connection = await saveLinkAsync(adapterToCreate, default(KeyValuePair<Connector, Adapter>?));
                                return await next(connection, resourcesExternalUnmatched);
                            });
                    },
                    // Add in the unmatched synchronization
                    async (Connection[] synchronizationsInternalOrMatched, IDictionary<string, Adapter> resourcesExternalUnmatched) =>
                    {
                        var connections = await resourcesExternalUnmatched
                            .FlatMap(
                                async (resourceKvp, next, skip) =>
                                {
                                    var resourceKey = resourceKvp.Key;
                                    var adapter = resourceKvp.Value;
                                    return await Persistence.AdapterDocument.FindOrCreateAsync(resourceKey,
                                        integrationIdExternal, resourceType, integrationIdExternal,
                                        async (adapterExternalStorage, connector, updateAsync) =>
                                        {
                                            var adapterUpdated = new Adapter()
                                            {
                                                adapterId = adapterExternalStorage.adapterId,
                                                key = resourceKey,
                                                name = adapter.name,
                                                identifiers = adapter.identifiers,
                                                integrationId = integrationIdExternal,
                                            };
                                            var externalAdapter = resourceKvp.Value;

                                            // It may seem like this would not cause any changes but it's updating identifiers
                                            var connection = await updateAsync(adapterUpdated, default(KeyValuePair<Connector, Adapter>?));
                                            return await next(connection);
                                        },
                                        async (saveLinkAsync) =>
                                        {
                                            var adapterToCreate = new Adapter()
                                            {
                                                adapterId = Guid.NewGuid(),
                                                key = resourceKey,
                                                name = adapter.name,
                                                identifiers = adapter.identifiers,
                                                integrationId = integrationIdInternal,
                                            };

                                            var connection = await saveLinkAsync(adapterToCreate, default(KeyValuePair<Connector, Adapter>?));
                                            return await next(connection);
                                        });
                                },
                                (IEnumerable<Connection> synchronizationsFromExternal) =>
                                {
                                    return synchronizationsFromExternal
                                        .Concat(synchronizationsInternalOrMatched)
                                        .ToArray()
                                        .ToTask();
                                });
                        return onMatch(connections);
                    });
        }


        public static Task<TResult> FindAdapterByIdAsync<TResult>(Guid synchronizationId,
            Func<EastFive.Azure.Synchronization.Adapter, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return EastFive.Azure.Synchronization.Persistence.AdapterDocument.FindByIdAsync(synchronizationId,
                (synchronization) => onFound(synchronization),
                () => onNotFound());
        }

        protected async Task<TResult> GetSynchronizationsAsync<TResourceInternal, TResult>(
                TResourceInternal[] resourcesInternal, ResourceAllSynchronizationsAsync<Task<TResult>> fetchAllAsync,
                Guid actorId, Guid integrationIdInternal, Guid integrationIdExternal, string resourceType,
                Func<TResourceInternal, Adapter> getAdapterInternal,
            Func<IEnumerable<Connection>, Task<TResult>> onMatch,
            Func<string, Task<TResult>> onFailure)
            where TResourceInternal : struct
        {
            return await await fetchAllAsync(
                async (resourcesExternal) =>
                {
                    return await await ResolveResourcesAsync(resourcesInternal, resourcesExternal,
                            actorId, integrationIdInternal, integrationIdExternal, resourceType,
                            getAdapterInternal,
                        (synchronizations) => onMatch(synchronizations),
                        onFailure);
                },
                onFailure,
                onFailure,
                () => onFailure("Not supported"));
        }
        
        public static async Task<TResult> FindAdaptersByRelatedAsync<TResult>(Guid relatedAdapterId, Guid integrationId,
                System.Security.Claims.Claim[] claims,
            Func<Adapter[], TResult> onFound,
            Func<TResult> onReferenceNotFound,
            Func<TResult> onUnauthorized)
        {
            return await await Persistence.AdapterDocument.FindByIdAsync(relatedAdapterId,
                relatedAdapter =>
                {
                    return relatedAdapter.connectorIds
                        .First(
                            async (connectorId, next) => await await Persistence.ConnectorDocument.FindByIdWithAdapterRemoteAsync(connectorId,
                                (connector, remoteAdapter) => remoteAdapter.integrationId == integrationId? 
                                    onFound(remoteAdapter.AsArray()).ToTask()
                                    : 
                                    next(),
                                () => next()),
                            () => Persistence.AdapterDocument.FindAllAsync(integrationId, relatedAdapter.resourceType,
                                    (syncs) =>
                                    {
                                        var orderedSynchronizationsExternalToInternal = syncs
                                            .NullToEmpty()
                                            .Where(sync => !sync.connectorIds.Any())
                                            .OrderBy(sync => relatedAdapter.name.SmithWaterman(sync.name))
                                            .ToArray();
                                        return onFound(orderedSynchronizationsExternalToInternal);
                                    }));
                },
                onReferenceNotFound.AsAsyncFunc());
        }

        public abstract Task<TResult> SynchronizeAsync<TResult>(
            Guid actorId, Guid integrationIdInternal, Guid integrationIdExternal,
            Func<IEnumerable<Connection>, TResult> onMatch,
            Func<string, TResult> onFailure);

        public abstract Task<TResult> GetAdaptersAsync<TResult>(
            Guid actorId, Guid integrationId,
            Func<IEnumerable<Adapter>, TResult> onMatch,
            Func<string, TResult> onFailure);

        public abstract Task<TResult> DeleteAsync<TResult>(
                string resourceType, string resourceKey,
            Func<TResult> onSuccess,
            Func<string, TResult> onFailure);

    }
}

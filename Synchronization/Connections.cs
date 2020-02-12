using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using BlackBarLabs;
using BlackBarLabs.Extensions;
using EastFive;
using EastFive.Analytics;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Text;

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

        public DateTime? lastSynchronized;
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

    public interface IIntegrate
    {
        TResult IsResourceTypeSupported<TResult>(string resourceType, string key,
            Func<BlackBarLabs.Api.Resources.WebId, TResult> onSupportedByController,
            Func<TResult> onNotSupported);
    }

    public abstract class Connections
    {
        /// <summary>
        /// "Internal" integration id for convenience methods
        /// </summary>
        private static Guid defaultInternalIntegrationId = default(Guid);

        abstract public string ResourceType { get; }

        private IIntegrate service;

        public Connections(IIntegrate service)
        {
            this.service = service;
        }

        public Task<Adapter[]> SaveAdaptersAsync(Guid integrationId, Adapter[] adapters)
        {
            var resourceType = this.ResourceType;
            return adapters
                .Select(
                    adapter => Persistence.AdapterDocument.FindOrCreateAsync(adapter.key,
                        integrationId, resourceType,
                        async (created, adapterInternalStorage, mutateAsync) =>
                        {
                            // Update identifiers internally if there is no externally mapped resource
                            var adapterId = await mutateAsync(
                                adapterToUpdate =>
                                {
                                    adapterToUpdate.key = adapter.key; // SHIM?
                                    adapterToUpdate.name = adapter.name;
                                    adapterToUpdate.identifiers = adapter.identifiers;
                                    adapterToUpdate.integrationId = integrationId;
                                    adapterToUpdate.resourceType = resourceType;
                                    return adapterToUpdate;
                                });

                            return adapterInternalStorage;
                        }))
                .WhenAllAsync();
        }

        private struct AdapterInfo
        {
            public bool created;
            public Adapter adapter;
            public Func<Func<Adapter, Adapter>, Task<Adapter>> saveAsync;
        }

        public static async Task<TResult> CreateOrUpdateConnection<TResult>(string resourceKeyInternal, Guid internalSystemItegrationId,
            string resourceKeyExternalSystem, Guid externalSystemIntegrationId,
            string resourceType,
            Func<Connector, TResult> onSuccess)
        {
            var adapterInternalTask = Persistence.AdapterDocument.FindOrCreateAsync(resourceKeyInternal, internalSystemItegrationId, resourceType,
                (created, adapter, saveAsync) => new AdapterInfo { created = created, adapter = adapter, saveAsync = saveAsync }.AsTask());
            var adapterExternal = await Persistence.AdapterDocument.FindOrCreateAsync(resourceKeyExternalSystem, externalSystemIntegrationId, resourceType,
                (created, adapter, saveAsync) => new AdapterInfo { created = created, adapter = adapter, saveAsync = saveAsync }.AsTask());
            var adapterInternal = await adapterInternalTask;

            var mutualConnections = adapterInternal.adapter.connectorIds.Intersect(adapterExternal.adapter.connectorIds);
            var alreadyConnected = mutualConnections.Any();
            if (alreadyConnected)
            {
                var connectorMaybe = await mutualConnections
                    .Select(
                        async (connectorId) =>
                        {
                            //SHIM, corrects when connector incorrectly created with same external/internal adapter ids
                            var maybe = await Persistence.ConnectorDocument.ShimUpdateAsync(connectorId,
                                async (internalconn, saveAsync) =>
                                {
                                    if (internalconn.adapterExternalId == internalconn.adapterInternalId)
                                    {
                                        internalconn.adapterInternalId = adapterInternal.adapter.adapterId;
                                        internalconn.adapterExternalId = adapterExternal.adapter.adapterId;
                                        await saveAsync(internalconn.adapterInternalId, internalconn.adapterExternalId);
                                    }
                                    return internalconn.AsOptional();
                                },
                                () => default(Connector?));
                            // remove bad data so that this is more efficient the next time around
                            if (maybe.IsDefault())
                            {
                                await adapterInternal.saveAsync(
                                    (adapterInternalToUpdate) =>
                                    {
                                        adapterInternalToUpdate.connectorIds = adapterInternalToUpdate.connectorIds
                                            .Where(id => id != connectorId)
                                            .ToArray();
                                        return adapterInternalToUpdate;
                                    });
                                await adapterExternal.saveAsync(
                                    (adapterExternalToUpdate) =>
                                    {
                                        adapterExternalToUpdate.connectorIds = adapterExternalToUpdate.connectorIds
                                            .Where(id => id != connectorId)
                                            .ToArray();
                                        return adapterExternalToUpdate;
                                    });
                            }
                            return maybe;
                        })
                    .AsyncEnumerable()
                    .SelectWhereHasValue()
                    .FirstAsync(c => c, () => default(Connector?));
                if (connectorMaybe.HasValue)
                {
                    var connector = connectorMaybe.Value;
                    if (adapterInternal.created)
                        await adapterInternal.saveAsync(
                            (adapterInternalToUpdate) =>
                            {
                                adapterInternalToUpdate.connectorIds = adapterInternalToUpdate.connectorIds
                                    .Append(connector.connectorId)
                                    .Distinct()
                                    .ToArray();
                                return adapterInternalToUpdate;
                            });
                    if (adapterExternal.created)
                        await adapterExternal.saveAsync(
                            (adapterExternalToUpdate) =>
                            {
                                adapterExternalToUpdate.connectorIds = adapterExternalToUpdate.connectorIds
                                    .Append(connector.connectorId)
                                    .Distinct()
                                    .ToArray();
                                return adapterExternalToUpdate;
                            });

                    return onSuccess(connector);
                }
            }

            while (true)
            {
                try
                {
                    var connectorId = Guid.NewGuid();
                    return await await Persistence.ConnectorDocument.CreateWithoutAdapterUpdateAsync(connectorId,
                            adapterInternal.adapter.adapterId, adapterExternal.adapter.adapterId, Connector.SynchronizationMethod.ignore, resourceType,
                        async () =>
                        {
                            var savingInternal = adapterInternal.saveAsync(
                                (adapterInternalToUpdate) =>
                                {
                                    adapterInternalToUpdate.connectorIds = adapterInternalToUpdate.connectorIds
                                        .Append(connectorId)
                                        .Distinct()
                                        .ToArray();
                                    return adapterInternalToUpdate;
                                });

                            await adapterExternal.saveAsync(
                                (adapterExternalToUpdate) =>
                                {
                                    adapterExternalToUpdate.connectorIds = adapterExternalToUpdate.connectorIds
                                        .Append(connectorId)
                                        .Distinct()
                                        .ToArray();
                                    return adapterExternalToUpdate;
                                });
                            await savingInternal;

                            return onSuccess(
                                new Connector
                                {
                                    connectorId = connectorId,
                                    adapterExternalId = adapterExternal.adapter.adapterId,
                                    adapterInternalId = adapterInternal.adapter.adapterId,
                                    createdBy = adapterInternal.adapter.adapterId,
                                    synchronizationMethod = Connector.SynchronizationMethod.ignore,
                                });
                        },
                        () => throw new Exception("Guid not unique."),
                        async (existingConnector) =>
                        {
                            #region patch potential bad data

                            var savingInternal = adapterInternal.adapter.connectorIds.Contains(existingConnector.connectorId)?
                                adapterInternal.adapter.AsTask()
                                :
                                adapterInternal.saveAsync(
                                    (adapterInternalToUpdate) =>
                                    {
                                        adapterInternalToUpdate.connectorIds = adapterInternalToUpdate.connectorIds
                                            .Append(existingConnector.connectorId)
                                            .Distinct()
                                            .ToArray();
                                        return adapterInternalToUpdate;
                                    });

                            if(!adapterExternal.adapter.connectorIds.Contains(existingConnector.connectorId))
                                await adapterExternal.saveAsync(
                                    (adapterExternalToUpdate) =>
                                    {
                                        adapterExternalToUpdate.connectorIds = adapterExternalToUpdate.connectorIds
                                            .Append(existingConnector.connectorId)
                                            .Distinct()
                                            .ToArray();
                                        return adapterExternalToUpdate;
                                    });
                            await savingInternal;

                            #endregion

                            return onSuccess(existingConnector);
                        });
                }
                catch (Exception)
                {
                    // run it again trying a different guid
                }
            }
        }
        
        public static Task<TResult> FindAdapterByIdAsync<TResult>(Guid synchronizationId,
            Func<EastFive.Azure.Synchronization.Adapter, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return EastFive.Azure.Synchronization.Persistence.AdapterDocument.FindByIdAsync(synchronizationId,
                (synchronization) => onFound(synchronization),
                () => onNotFound());
        }
        
        public static async Task<TResult> FindAdaptersByRelatedAsync<TResult>(Guid relatedAdapterId, Guid integrationId,
                System.Security.Claims.Claim[] claims,
            Func<Adapter[], TResult> onFound,
            Func<TResult> onReferenceNotFound,
            Func<TResult> onUnauthorized)
        {
            return await await Persistence.AdapterDocument.FindByIdAsync(relatedAdapterId,
                async relatedAdapter =>
                {
                    var orderedAdapters = await Persistence.AdapterDocument
                        .FindAll(integrationId, relatedAdapter.resourceType)
                        .OrderByAsync(adapter => relatedAdapter.name.SmithWaterman(adapter.name));
                    return onFound(orderedAdapters.ToArray());
                },
                onReferenceNotFound.AsAsyncFunc());
        }

        public static Task<TResult> FindAdapterByKeyAsync<TResult>(string key, Guid integrationId, string resourceType,
                System.Security.Claims.Claim[] claims,
            Func<Adapter, TResult> onFound,
            Func<TResult> onReferenceNotFound,
            Func<TResult> onUnauthorized)
        {
            return FindAdapterByKeyAsync(key, integrationId, resourceType,
                onFound,
                onReferenceNotFound);
        }

        public static async Task<TResult> FindAdapterByKeyAsync<TResult>(string key, Guid integrationId, string resourceType,
            Func<Adapter, TResult> onFound,
            Func<TResult> onReferenceNotFound)
        {
            return await Persistence.AdapterDocument.FindByKeyAsync(key, integrationId, resourceType,
                relatedAdapter =>
                {
                    return onFound(relatedAdapter);
                },
                onReferenceNotFound);
        }

        public static async Task<TResult> FindAdapterConnectorByKeyAsync<TResult>(string localResourceKey, Guid localIntegrationId, string localResourceType,
                Guid remoteIntegrationId,
            Func<Connector, Adapter, TResult> onFound,
            Func<TResult> onLocalAdapterNotFound)
        {
            return await await FindAdapterByKeyAsync(localResourceKey, localIntegrationId, localResourceType,
                (localAdapter) => localAdapter.connectorIds
                    .SelectAsyncOptional<Guid, KeyValuePair<Adapter, Connector>>(
                        (connectorId, select, skip) => Persistence.ConnectorDocument.FindByIdWithAdapterRemoteAsync(connectorId, localAdapter,
                            (remoteConnector, remoteAdapter) => select(remoteAdapter.PairWithValue(remoteConnector)),
                            skip))
                   .FirstMatchAsync<KeyValuePair<Adapter, Connector>, TResult>(
                        (adapterConnectorKvp, match, next) =>
                        {
                            var remoteAdapter = adapterConnectorKvp.Key;
                            var remoteConnector = adapterConnectorKvp.Value;
                            if (remoteAdapter.integrationId != remoteIntegrationId)
                                return next();

                            var result = onFound(remoteConnector, remoteAdapter);
                            return match(result);
                        },
                        () => onLocalAdapterNotFound()),
                onLocalAdapterNotFound.AsAsyncFunc());
        }

        public static async Task<TResult> CreateOrUpdateAdapterConnectorByKeyAsync<TResult>(string localResourceKey, Guid localIntegrationId, string localResourceType,
                Guid remoteIntegrationId,
            Func<bool, Connector, Adapter, Func<DateTime, string, Task>, Task<TResult>> onUpdate)
        {
            async Task<TResult> adapterFoundAsync(Adapter localAdapter)
            {
                return await await localAdapter.connectorIds
                    .Select(
                        (connectorId) => Persistence.ConnectorDocument.UpdateSynchronizationWithAdapterRemoteAsync(connectorId, localAdapter,
                            async (remoteConnector, remoteAdapter, updateAsync) =>
                            {
                                var result = default(TResult);
                                var matched = remoteAdapter.integrationId == remoteIntegrationId;
                                if (!matched)
                                    return new { matched, result };

                                result = await onUpdate(false, remoteConnector, remoteAdapter,
                                    async (whenUpdated, adapterKeyUpdated) =>
                                    {
                                        await updateAsync(whenUpdated);

                                        if (remoteAdapter.key == adapterKeyUpdated)
                                            return;

                                        //TODO Fix with locking
                                        matched = await Persistence.AdapterDocument.UpdateAsync(remoteAdapter.adapterId,
                                            async (adapterCurrent, saveUpdatedAdapterAsync) =>
                                            {
                                                await saveUpdatedAdapterAsync(adapterCurrent.connectorIds, adapterKeyUpdated, adapterCurrent.identifiers);
                                                return true;
                                            },
                                            () =>
                                            {
                                                return false;
                                            });


                                    });
                                return new { matched, result };
                            },
                            () =>
                            {
                                var matched = false;
                                var result = default(TResult);
                                return new { matched, result };
                            }))
                    .AsyncEnumerable()
                    .Where(item => item.matched)
                    .FirstAsync(
                        (one) =>
                        {
                            return one.result.AsTask();
                        },
                        () =>
                        {
                            return createConnectorWithAdaptersAsync(localAdapter, false);
                        });
            }

            Task<TResult> createConnectorWithAdaptersAsync(Adapter localAdapter, bool createdLocalAdapter)
            {
                var connectorId = Guid.NewGuid();
                var remoteAdapter = new Adapter
                {
                    adapterId = Guid.NewGuid(),
                    connectorIds = new[] { connectorId },
                    integrationId = remoteIntegrationId,
                    resourceType = localResourceType,
                };

                var connector = new Connector
                {
                    connectorId = connectorId,
                    adapterInternalId = localAdapter.adapterId,
                    adapterExternalId = remoteAdapter.adapterId,
                };
                return onUpdate(true, connector, remoteAdapter,
                    async (whenUpdated, adapterKeyUpdated) =>
                    {
                        await Persistence.ConnectorDocument.CreateWithoutAdapterUpdateAsync(connector.connectorId,
                            localAdapter.adapterId, remoteAdapter.adapterId, Connector.SynchronizationMethod.ignore, localResourceType, whenUpdated,
                            () => true, () => false, (why) => false);

                        if (createdLocalAdapter)
                        {
                            localAdapter.connectorIds = localAdapter.connectorIds
                                .Append(connectorId)
                                .Distinct()
                                .ToArray();
                            createdLocalAdapter = await Persistence.AdapterDocument.CreateAsync(localAdapter, () => true, () => false);
                        }
                        else
                        {
                            bool updated = await Persistence.AdapterDocument.UpdateAsync(localAdapter.adapterId,
                                async (localAdapterCurrent, saveLocalAdapterCurrent) =>
                                {
                                    var connectorIds = localAdapterCurrent.connectorIds
                                        .Append(connectorId)
                                        .Distinct()
                                        .ToArray();
                                    await saveLocalAdapterCurrent(connectorIds, localAdapterCurrent.key, localAdapterCurrent.identifiers);
                                    return true;
                                },
                                () =>
                                {
                                    return false;
                                });
                        }
                        bool createdRemoteAdapter = await Persistence.AdapterDocument.CreateAsync(remoteAdapter, () => true, () => false);
                    });
            }

            return await await FindAdapterByKeyAsync(localResourceKey, localIntegrationId, localResourceType,
                (localAdapter) =>
                {
                    return adapterFoundAsync(localAdapter);
                },
                () =>
                {
                    var localAdapter = new Adapter
                    {
                        adapterId = Persistence.AdapterDocument.GetId(localResourceKey, localIntegrationId, localResourceType),
                        connectorIds = new Guid[] { },
                        integrationId = remoteIntegrationId,
                        resourceType = localResourceType,
                    };
                    return createConnectorWithAdaptersAsync(localAdapter, true);
                });
        }

        public static async Task<TResult> UpdateAdapterConnectorByKeyAsync<TResult>(string localResourceKey, Guid localIntegrationId, string localResourceType,
                Guid remoteIntegrationId,
            Func<Connector, Adapter, Func<DateTime?, Task>, Task<TResult>> onFound,
            Func<TResult> onLocalAdapterNotFound)
        {   
            return await await FindAdapterByKeyAsync(localResourceKey, localIntegrationId, localResourceType,
                (localAdapter) =>
                {
                    return localAdapter.connectorIds
                        .First(
                            async (connectorId, next) =>
                            {
                                return await await Persistence.ConnectorDocument.UpdateSynchronizationWithAdapterRemoteAsync<Task<TResult>>(connectorId, localAdapter,
                                    (remoteConnector, remoteAdapter, saveAsync) =>
                                    {
                                        if (remoteAdapter.integrationId != remoteIntegrationId)
                                            return next().AsTask();
                                        return onFound(remoteConnector, remoteAdapter, saveAsync).AsTask();
                                    },
                                    next);
                            },
                            () => onLocalAdapterNotFound().AsTask());
                },
                onLocalAdapterNotFound.AsAsyncFunc());
        }

        public static Task<TResult> ShimUpdateConnectorIdsAsync<TResult>(Adapter localAdapter, Guid remoteIntegrationId,
            Func<Connection, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return Persistence.ConnectorDocument.ShimFindByLocalAdapterAsync(localAdapter.adapterId,
                async (pairs) =>
                {
                    return await await pairs.FirstAsync(
                        async (pair) =>
                        {
                            var connector = pair.Key;
                            var remoteAdapter = pair.Value;
                            if (!remoteAdapter.connectorIds.Contains(connector.connectorId) || 
                                !localAdapter.connectorIds.Contains(connector.connectorId) ||
                                remoteAdapter.integrationId == default(Guid))
                            {
                                await Persistence.AdapterDocument.ShimUpdateAsync(remoteAdapter.adapterId,
                                    async (remote, saveRemoteAsync) =>
                                    {
                                        remote.connectorIds = remote.connectorIds
                                            .Append(connector.connectorId)
                                            .Distinct()
                                            .ToArray();
                                        remote.integrationId = remoteIntegrationId;
                                        await saveRemoteAsync(
                                            remote.connectorIds,
                                            remoteIntegrationId,
                                            remote.name,
                                            remote.identifiers);
                                        return await Persistence.AdapterDocument.UpdateAsync(localAdapter.adapterId,
                                            async (local, saveLocalAsync) =>
                                            {
                                                local.connectorIds = local.connectorIds
                                                    .Append(connector.connectorId)
                                                    .Distinct()
                                                    .ToArray();
                                                await saveLocalAsync(
                                                    local.connectorIds,
                                                    local.name,
                                                    local.identifiers);
                                                return onFound(new Connection
                                                {
                                                    adapterInternal = local,
                                                    adapterExternal = remote,
                                                    connector = connector
                                                });
                                            },
                                            () => onNotFound());
                                    },
                                    () => onNotFound());
                            }
                            return onFound(new Connection
                            {
                                adapterInternal = localAdapter,
                                adapterExternal = remoteAdapter,
                                connector = connector
                            });
                        },
                        onNotFound.AsAsyncFunc());
                });
        }

        public static Task<TResult> FindAdapterConnectorsByKeyAsync<TResult>(string localResourceKey, Guid localIntegrationId, string localResourceType,
            Func<IEnumerableAsync<KeyValuePair<Connector, Adapter>>, TResult> onFound,
            Func<TResult> onLocalAdapterNotFound)
        {
            return FindAdapterByKeyAsync(localResourceKey, localIntegrationId, localResourceType,
                (localAdapter) =>
                {
                    var adapterConnectorKpvs = localAdapter.connectorIds
                        .SelectAsyncOptional<Guid, KeyValuePair<Connector, Adapter>>(
                            (connectorId, select, skip) => Persistence.ConnectorDocument.FindByIdWithAdapterRemoteAsync(connectorId, localAdapter,
                                (remoteConnector, remoteAdapter) => select(remoteAdapter.PairWithKey(remoteConnector)),
                                skip));
                    return onFound(adapterConnectorKpvs);
                },
                onLocalAdapterNotFound);
        }

        public abstract Task<TResult> GetAdaptersAsync<TResult>(
            Guid integrationId,
            Func<IEnumerable<Adapter>, TResult> onMatch,
            Func<string, TResult> onFailure);

        public abstract Task<TResult> DeleteAsync<TResult>(
                string resourceType, string resourceKey,
            Func<TResult> onSuccess,
            Func<string, TResult> onFailure);

        #region Convenience methods
        
        public static Task<TResult> FindInternalAdapterConnectorByExternalKeyAsync<TResult>(string key, Guid integrationId, string resourceType,
            Func<Connector, Adapter, TResult> onFound,
            Func<TResult> onReferenceNotFound)
        {
            return FindAdapterConnectorByKeyAsync(key, integrationId, resourceType, defaultInternalIntegrationId,
                onFound,
                onReferenceNotFound);
        }

        /// <summary>
        /// Convenience method for creating an adapter for an "internal" and external resource, and a connector for the two adapters.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="resourceIdInternal"></param>
        /// <param name="resourceIdExternalSystem"></param>
        /// <param name="externalSystemIntegrationId"></param>
        /// <param name="resourceType"></param>
        /// <param name="onSuccess"></param>
        /// <returns></returns>
        public static Task<TResult> CreateOrUpdateInternalExternalConnection<TResult>(Guid resourceIdInternal,
            string resourceKeyExternal, Guid externalSystemIntegrationId,
            string resourceType,
            Func<Guid, TResult> onSuccess)
        {
            return CreateOrUpdateInternalExternalConnection(resourceIdInternal.ToString("N"), resourceKeyExternal, externalSystemIntegrationId, resourceType, onSuccess);
        }


        public static Task<TResult> CreateOrUpdateConnection<TResult>(Guid resourceIdInternal,
            string resourceKeyExternal, Guid externalSystemIntegrationId,
            string resourceType,
            Func<Connector, TResult> onSuccess)
        {
            return CreateOrUpdateInternalExternalConnection(resourceIdInternal.ToString("N"), resourceKeyExternal, externalSystemIntegrationId, resourceType, onSuccess);
        }

        public static Task<TResult> CreateOrUpdateInternalExternalConnection<TResult>(string resourceKeyInternal,
            string resourceKeyExternal, Guid externalSystemIntegrationId,
            string resourceType,
            Func<Guid, TResult> onSuccess)
        {
            return CreateOrUpdateInternalExternalConnection(resourceKeyInternal,
                    resourceKeyExternal, externalSystemIntegrationId,
                    resourceType,
                (Connector connector) => onSuccess(connector.connectorId));
        }

        public static Task<TResult> CreateOrUpdateInternalExternalConnection<TResult>(string resourceKeyInternal,
            string resourceKeyExternal, Guid externalSystemIntegrationId,
            string resourceType,
            Func<Connector, TResult> onSuccess)
        {
            return CreateOrUpdateConnection(resourceKeyInternal, defaultInternalIntegrationId,
                resourceKeyExternal, externalSystemIntegrationId,
                resourceType,
                onSuccess);
        }

        /// <summary>
        /// Create a connection between two resources. If connections exist (any connections,
        /// even connections to different integrations) they will be removed. To preserve
        /// existing connections use CreateOrUpdateBatchConnection.
        /// </summary>
        /// <param name="resourceIdKeys"></param>
        /// <param name="externalSystemIntegrationId"></param>
        /// <param name="resourceType"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        public static IEnumerableAsync<Guid> CreateOrReplaceBatchConnection(
            IEnumerableAsync<KeyValuePair<Guid, string>> resourceIdKeys,
            Guid externalSystemIntegrationId, string resourceType,
            Connector.SynchronizationMethod method = Connector.SynchronizationMethod.ignore)
        {
            var resourceIdKeyConnections = resourceIdKeys
                .Select(
                    resourceIdKey =>
                    {
                        var internalKey = resourceIdKey.Key.ToString("N");
                        var adapterInternalId = Persistence.AdapterDocument.GetId(internalKey, defaultInternalIntegrationId, resourceType);
                        var connectorId = Guid.NewGuid();
                        var adapterInternal = new Adapter
                        {
                            adapterId = adapterInternalId,
                            connectorIds = new[] { connectorId },
                            integrationId = defaultInternalIntegrationId,
                            key = internalKey,
                            resourceType = resourceType,
                        };
                        var externalKey = resourceIdKey.Value;
                        var adapterExternal = new Adapter
                        {
                            adapterId = Persistence.AdapterDocument.GetId(externalKey, externalSystemIntegrationId, resourceType),
                            connectorIds = new[] { connectorId },
                            integrationId = externalSystemIntegrationId,
                            key = externalKey,
                            resourceType = resourceType,
                        };
                        var connector = new Connector
                        {
                            connectorId = connectorId,
                            adapterInternalId = adapterInternal.adapterId,
                            adapterExternalId = adapterExternal.adapterId,
                            synchronizationMethod = method,
                        };
                        return adapterInternal.PairWithValue(adapterExternal).PairWithValue(connector);
                    });

            var connectors = resourceIdKeyConnections
                .Select(resourceIdKeyConnection => resourceIdKeyConnection.Value); // Get connectors
            return Persistence.ConnectorDocument.CreateBatch(connectors, method)
                .JoinTask(
                    async () =>
                    {
                        var adaptersInternal = resourceIdKeyConnections
                                .Select(resourceIdKeyConnection => resourceIdKeyConnection.Key.Key) // Get internal adapters
                                .Distinct(internalAdapter => internalAdapter.key);
                        Adapter[] adaptersInternalSaved = await Persistence.AdapterDocument
                            .CreateOrUpdateBatch(adaptersInternal, defaultInternalIntegrationId, resourceType)
                            .ToArrayAsync();
                    })
                .JoinTask(
                    async () =>
                    {
                        var externalAdapters = resourceIdKeyConnections
                                .Select(resourceIdKeyConnection => resourceIdKeyConnection.Key.Value) // Get external adapters
                                .Distinct(externalAdapter => externalAdapter.key);
                        Adapter[] adaptersExternalSaved = await Persistence.AdapterDocument
                                .CreateOrUpdateBatch(externalAdapters,
                                    externalSystemIntegrationId, resourceType)
                                .ToArrayAsync();
                    });
        }

        public static IEnumerableAsync<Guid> CreateOrUpdateBatchConnection(
            IEnumerableAsync<KeyValuePair<Guid, string>> resourceIdKeys,
            Guid externalSystemIntegrationId, string resourceType,
            Connector.SynchronizationMethod method = Connector.SynchronizationMethod.ignore)
        {
            var resourceIdKeyConnections = resourceIdKeys
                .Select(
                    async resourceIdKey =>
                    {
                        var externalKey = resourceIdKey.Value;
                        var internalKey = resourceIdKey.Key.ToString("N");
                        var adapterInternalId = Persistence.AdapterDocument.GetId(internalKey, defaultInternalIntegrationId, resourceType);
                        var needsUpdatedAdapterExternalKeyKvp = await await Persistence.AdapterDocument.FindByIdAsync(adapterInternalId,
                            (adapter) => adapter.connectorIds
                                .SelectAsyncOptional<Guid, KeyValuePair<Connector, Adapter>>(
                                    (adapterConnectorId, select, skip) => Persistence.ConnectorDocument.FindByIdWithAdapterRemoteAsync(adapterConnectorId, adapter,
                                    (connectorExisting, adapterExisting) => select(connectorExisting.PairWithValue(adapterExisting)),
                                    skip))
                                .Where(connectorAdapterKvp => connectorAdapterKvp.Value.integrationId == externalSystemIntegrationId)
                                .FirstAsync(
                                    (connectorAdapterKvp) => false.PairWithValue(adapter.PairWithValue(externalKey)),
                                    () => true.PairWithValue(adapter.PairWithValue(externalKey))),
                            () =>
                            {
                                var adapterInternal = new Adapter
                                {
                                    adapterId = adapterInternalId,
                                    connectorIds = new Guid [] { },
                                    integrationId = defaultInternalIntegrationId,
                                    key = internalKey,
                                    resourceType = resourceType,
                                };
                                var adapterInternalExternalKeyKvp = adapterInternal.PairWithValue(externalKey);
                                return true.PairWithValue(adapterInternalExternalKeyKvp).AsTask();
                            });
                        return needsUpdatedAdapterExternalKeyKvp;
                    })
                .Await()
                .Where(needsUpdatedAdapterExternalKeyKvp => needsUpdatedAdapterExternalKeyKvp.Key)
                .Select(
                    needsUpdatedAdapterExternalKeyKvp =>
                    {
                        var connectorId = Guid.NewGuid();
                        var adapterInternalExternalKeyKvp = needsUpdatedAdapterExternalKeyKvp.Value;
                        var adapterInternal = adapterInternalExternalKeyKvp.Key;
                        adapterInternal.connectorIds = adapterInternal.connectorIds
                            .Append(connectorId)
                            .ToArray();
                        var externalKey = adapterInternalExternalKeyKvp.Value;
                        var adapterExternal = new Adapter
                        {
                            adapterId = Persistence.AdapterDocument.GetId(externalKey, externalSystemIntegrationId, resourceType),
                            connectorIds = new[] { connectorId },
                            integrationId = externalSystemIntegrationId,
                            key = externalKey,
                            resourceType = resourceType,
                        };
                        var connector = new Connector
                        {
                            connectorId = connectorId,
                            adapterInternalId = adapterInternal.adapterId,
                            adapterExternalId = adapterExternal.adapterId,
                            synchronizationMethod = method,
                        };
                        return adapterInternal.PairWithValue(adapterExternal).PairWithValue(connector);
                    });

            var connectors = resourceIdKeyConnections
                .Select(resourceIdKeyConnection => resourceIdKeyConnection.Value); // Get connectors
            return Persistence.ConnectorDocument.CreateBatch(connectors, method)
                .JoinTask(
                    async () =>
                    {
                        var adaptersInternal = resourceIdKeyConnections
                                .Select(resourceIdKeyConnection => resourceIdKeyConnection.Key.Key) // Get internal adapters
                                .Distinct(internalAdapter => internalAdapter.key);
                        Adapter[] adaptersInternalSaved = await Persistence.AdapterDocument
                            .CreateOrUpdateBatch(adaptersInternal, defaultInternalIntegrationId, resourceType)
                            .ToArrayAsync();
                    })
                .JoinTask(
                    async () =>
                    {
                        var externalAdapters = resourceIdKeyConnections
                                .Select(resourceIdKeyConnection => resourceIdKeyConnection.Key.Value) // Get external adapters
                                .Distinct(externalAdapter => externalAdapter.key);
                        Adapter[] adaptersExternalSaved = await Persistence.AdapterDocument
                                .CreateOrUpdateBatch(externalAdapters,
                                    externalSystemIntegrationId, resourceType)
                                .ToArrayAsync();
                    });
        }


        public static IEnumerableAsync<Adapter> FindAdaptersByType(string resourceType)
        {
            var adapters = EastFive.Azure.Synchronization.Persistence.AdapterDocument.FindAll(resourceType);
            return adapters;
        }
        
        public static IEnumerableAsync<Connection> FindConnectionsByType(string resourceType)
        {
            var adapters = Persistence.ConnectorDocument.FindAllByType(resourceType);
            return adapters;
        }

        /// <summary>
        /// Convenience method for looking up an external to "internal"/default resource mapping.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="keyGuid"></param>
        /// <param name="integrationId"></param>
        /// <param name="resourceType"></param>
        /// <param name="onFound"></param>
        /// <param name="onReferenceNotFound"></param>
        /// <returns></returns>
        public static async Task<TResult> FindInternalIdByResourceKeyAsync<TResult>(string key, Guid integrationId, string resourceType,
            Func<Guid, TResult> onFound,
            Func<TResult> onConnectionNotFound)
        {
            return await await Persistence.AdapterDocument.FindByKeyAsync(key, integrationId, resourceType,
                adapterInternal =>
                {
                    return Persistence.ConnectorDocument.FindByAdapterWithConnectionAsync(adapterInternal,
                        (KeyValuePair<Connector, Adapter>[] connectorAdapterExternalIdKvps) =>
                        {
                            return connectorAdapterExternalIdKvps.First(
                                (connectorAdapterExternalIdKvp, next) =>
                                {
                                    if (connectorAdapterExternalIdKvp.Value.integrationId == defaultInternalIntegrationId)
                                        if(Guid.TryParse(connectorAdapterExternalIdKvp.Value.key, out Guid internalId))
                                            return onFound(internalId);

                                    return next();
                                },
                                onConnectionNotFound);
                        });
                },
                onConnectionNotFound.AsAsyncFunc());
        }

        /// <summary>
        /// Convenience method for looking up an "internal" to external resource mapping.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="keyGuid"></param>
        /// <param name="integrationId"></param>
        /// <param name="resourceType"></param>
        /// <param name="onFound"></param>
        /// <param name="onReferenceNotFound"></param>
        /// <returns></returns>
        public static Task<TResult> FindResourceKeyByInternalIdAsync<TResult>(Guid keyGuid, Guid integrationId, string resourceType,
            Func<string, TResult> onFound,
            Func<TResult> onConnectionNotFound,
            ILogger logger = default)
        {
            var key = keyGuid.ToString("N");
            return FindResourceKeyByInternalKeyAsync<TResult>(key, integrationId, resourceType,
                onFound,
                onConnectionNotFound,
                    logger: logger);
        }

        public static async Task<TResult> FindResourceKeyByInternalKeyAsync<TResult>(string keyInternal,
                Guid integrationId, string resourceType,
            Func<string, TResult> onFound,
            Func<TResult> onConnectionNotFound,
            ILogger logger = default)
        {
            var scopedLogger = logger.CreateScope($"{integrationId}/{resourceType}/{keyInternal}");
            scopedLogger.Trace($"Looking up...");
            return await await FindResourceKeysByInternalKeyAsync(keyInternal, resourceType,
                integrationIdResourceKeys =>
                {
                    scopedLogger.Trace($"...Returned Keys");
                    return integrationIdResourceKeys
                        .Where(
                            integrationIdResourceKey =>
                            {
                                var externalAdapter = integrationIdResourceKey.Value;
                                var isAdapterInDesiredIntegration = externalAdapter.integrationId == integrationId;
                                scopedLogger.Trace($"{externalAdapter.integrationId} == {integrationId} + `{externalAdapter.key}`.HasBlackSpace()");
                                var doesAdapterHaveKeyValue = externalAdapter.key.HasBlackSpace();
                                var useThisAdapter = isAdapterInDesiredIntegration && doesAdapterHaveKeyValue;
                                return useThisAdapter;
                            })
                        .FirstAsync(
                            (integrationIdResourceKey) => onFound(integrationIdResourceKey.Value.key),
                            () => onConnectionNotFound());
                    //.FirstAsyncMatchAsync(
                    //    async (integrationIdResourceKey, next) =>
                    //    {
                    //        var externalAdapter = integrationIdResourceKey.Value;
                    //        var isAdapterInDesiredIntegration = externalAdapter.integrationId == integrationId;
                    //        var doesAdapterHaveKeyValue = !externalAdapter.key.IsNullOrWhiteSpace();
                    //        var useThisAdapter = isAdapterInDesiredIntegration && doesAdapterHaveKeyValue;
                    //        if (useThisAdapter)
                    //            return onFound(externalAdapter.key);

                    //        return await next();
                    //    },
                    //    () => onConnectionNotFound());
                },
                () =>
                {
                    scopedLogger.Trace($"... FAILURE, no connection available.");
                    return onConnectionNotFound().AsTask();
                },
                logger: scopedLogger);
                
        }

        public static Task<TResult> FindResourceKeysByInternalIdAsync<TResult>(Guid keyGuid, string resourceType,
            Func<IEnumerableAsync<KeyValuePair<Connector, Adapter>>, TResult> onFound,
            Func<TResult> onAdapterNotFound)
        {
            var key = keyGuid.ToString("N");
            return FindResourceKeysByInternalKeyAsync(key, resourceType, onFound, onAdapterNotFound);
        }

        public static Task<TResult> FindResourceKeysByInternalKeyAsync<TResult>(string keyInternal, string resourceType,
            Func<IEnumerableAsync<KeyValuePair<Connector, Adapter>>, TResult> onFound,
            Func<TResult> onAdapterNotFound,
            ILogger logger = default)
        {
            return Persistence.AdapterDocument.FindByKeyAsync<TResult>(keyInternal, defaultInternalIntegrationId, resourceType,
                adapterInternal =>
                {
                    var scopedLogger = logger.CreateScope($"{adapterInternal.adapterId}");
                    scopedLogger.Trace($"has {adapterInternal.connectorIds.Length} connection ids");
                    var connectorAdapterKvps = adapterInternal.connectorIds
                        .SelectAsyncOptional<Guid, KeyValuePair<Connector, Adapter>>(
                            (connectorId, select, skip) =>
                            {
                                scopedLogger.Trace($"Processing connectorID {connectorId}");
                                return Persistence.ConnectorDocument.FindByIdWithAdapterRemoteAsync(connectorId, adapterInternal,
                                    (connector, adapter) =>
                                    {
                                        scopedLogger.Trace($"Found connector ID={connectorId}");
                                        return select(connector.PairWithValue(adapter));
                                    },
                                    () =>
                                    {
                                        scopedLogger.Trace($"FAILURE to find connector ID={connectorId}, skipping");
                                        return skip();
                                    },
                                    logger:scopedLogger);
                            },
                            logger:scopedLogger);
                    return onFound(connectorAdapterKvps);
                },
                () =>
                {
                    return onAdapterNotFound();
                });
        }
        
        public static Task<TResult> FindAdapterConnectorsByInternalIdAsync<TResult>(Guid internalGuidKey, string resourceType,
            Func<IEnumerableAsync<KeyValuePair<Connector, Adapter>>, TResult> onFoundInternalAdapter,
            Func<TResult> onInternalAdapterNotFound)
        {
            var internalKey = internalGuidKey.ToString("N");
            return FindAdapterConnectorsByKeyAsync(internalKey, defaultInternalIntegrationId, resourceType,
                onFoundInternalAdapter,
                onInternalAdapterNotFound);
        }

        public static Task<TResult> FindAdapterConnectorByInternalIdAsync<TResult>(Guid internalGuidKey, string resourceType, Guid remoteIntegrationId,
            Func<Connector, Adapter, TResult> onFoundInternalAdapter,
            Func<TResult> onInternalAdapterNotFound)
        {
            var internalKey = internalGuidKey.ToString("N");
            return FindAdapterConnectorByKeyAsync(internalKey, defaultInternalIntegrationId, resourceType, remoteIntegrationId,
                onFoundInternalAdapter,
                onInternalAdapterNotFound);
        }

        public static Task<TResult> CreateOrUpdateAdapterConnectorByInternalIdAsync<TResult>(Guid internalGuidKey, string resourceType, Guid remoteIntegrationId,
          Func<bool, Connector, Adapter, Func<DateTime, string, Task>, Task<TResult>> onFoundInternalAdapter)
        {
            var internalKey = internalGuidKey.ToString("N");
            return CreateOrUpdateAdapterConnectorByKeyAsync(internalKey, defaultInternalIntegrationId, resourceType, remoteIntegrationId,
                onFoundInternalAdapter);
        }

        public static Task<TResult> UpdateConnectorByIdAsync<TResult>(Guid internalGuidKey, string resourceType, Guid remoteIntegrationId,
            Func<Connector, Adapter, Func<DateTime?, Task>, Task<TResult>> onFoundInternalAdapter,
            Func<TResult> onInternalAdapterNotFound)
        {
            var internalKey = internalGuidKey.ToString("N");
            return UpdateAdapterConnectorByKeyAsync(internalKey, defaultInternalIntegrationId, resourceType, remoteIntegrationId,
                onFoundInternalAdapter,
                onInternalAdapterNotFound);
        }

        #endregion

        public static Task<TResult> SynchronizeLockedAsync<TResult>(Guid connectorId, string resourceType,
            Func<TimeSpan?, 
                Func<TResult, Task<Persistence.ConnectorDocument.ILockResult<TResult>>>, 
                Func<TResult, Task<Persistence.ConnectorDocument.ILockResult<TResult>>>,
                Task<Persistence.ConnectorDocument.ILockResult<TResult>>> onLockAquired,
            Func<int, 
                TimeSpan,
                TimeSpan?,
                Func<Task<TResult>>, 
                Func<Task<TResult>>,
                Task<TResult>> onAlreadyLocked,
            Func<TResult> onNotFound)
        {
            return Persistence.ConnectorDocument.SynchronizeLockedAsync(connectorId, resourceType,
                async (duration, unlockAndSave, unlock) =>
                {
                    var result = await onLockAquired(duration,
                        (t) => unlockAndSave(t),
                        (t) => unlock(t));
                    return result;
                },
                onAlreadyLocked:
                    (retryCount, retryDuration, lastSyncDuration, continueAquiring, force) =>
                    {
                        return onAlreadyLocked(retryCount, retryDuration, lastSyncDuration, continueAquiring, force);
                    },
                onNotFound:onNotFound);
        }

        public static async Task<TResult> DeleteAdapterAndConnections<TResult>(Adapter adapter, 
            Func<TResult> onDeleted,
            Func<string, TResult> onFailure)
        {
            bool [] successes = await adapter.connectorIds
                .Select(
                    async connectorId =>
                    {
                        bool deleted = await await Persistence.ConnectorDocument.DeleteByIdAsync(connectorId,
                            (adapterId1, adapterId2) =>
                            {
                                var otherAdapterId = adapterId1 == adapter.adapterId ?
                                    adapterId2
                                    :
                                    adapterId1;
                                return Persistence.AdapterDocument.UpdateAsync(otherAdapterId,
                                    async (otherAdapter, updateOtherAdapterAsync) =>
                                    {
                                        await updateOtherAdapterAsync(otherAdapter.connectorIds.Where(cId => cId != connectorId).ToArray(), otherAdapter.name, otherAdapter.identifiers);
                                        return true;
                                    },
                                    () => false);
                            },
                            () => false.AsTask());
                        return deleted;
                    })
                .AsyncEnumerable()
                .JoinTask(Persistence.AdapterDocument.DeleteByIdAsync(adapter.adapterId,
                    () => true,
                    () => false))
                .ToArrayAsync();
            return onDeleted();

        }
    }
}

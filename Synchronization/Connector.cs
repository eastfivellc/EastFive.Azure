using EastFive.Extensions;
using EastFive.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Synchronization
{
    public static class Connectors
    {
        public static Task<TResult> CreateConnectorAsync<TResult>(Guid connectorId, Guid sourceAdapterId, Guid destinationAdapterId,
                Connector.SynchronizationMethod flow,
                Guid performingAsActorId, Claim[] claims,
            Func<TResult> onCreated,
            Func<Connection, TResult> onCreatedAndModified,
            Func<TResult> onAlreadyExists,
            Func<Guid, TResult> onRelationshipAlreadyExists,
            Func<Guid, TResult> onAdapterDoesNotExist,
            Func<string, TResult> onFailure)
        {
            return Persistence.AdapterDocument.UpdateAsync(sourceAdapterId,
                (sourceAdapter, saveSourceAdapterAsync) =>
                {
                    return Persistence.AdapterDocument.UpdateAsync(destinationAdapterId,
                        async (destinationAdapter, saveDestinationAdapterAsync) =>
                        {
                            if (destinationAdapter.resourceType != sourceAdapter.resourceType)
                                return onFailure($"Cannot connect `{sourceAdapter.resourceType}` to `{destinationAdapter.resourceType}`.");

                            return await await Persistence.ConnectorDocument.CreateWithoutAdapterUpdateAsync(connectorId,
                                    sourceAdapterId, destinationAdapterId, flow, sourceAdapter.resourceType,
                                async () =>
                                {
                                    await saveSourceAdapterAsync(sourceAdapter.connectorIds
                                        .Append(connectorId)
                                        .Distinct()
                                        .ToArray(),
                                        sourceAdapter.name, sourceAdapter.identifiers);
                                    await saveDestinationAdapterAsync(destinationAdapter.connectorIds
                                        .Append(connectorId)
                                        .Distinct()
                                        .ToArray(),
                                        destinationAdapter.name, destinationAdapter.identifiers);
                                    //var connector = new Connector()
                                    //{
                                    //    connectorId = connectorId,
                                    //    adapterExternalId = destinationAdapterId,
                                    //    adapterInternalId = sourceAdapterId,
                                    //    createdBy = sourceAdapterId,
                                    //    synchronizationMethod = flow,
                                    //};
                                    //var connection = Convert(sourceAdapter, connector, destinationAdapter);
                                    return onCreated();
                                },
                                onAlreadyExists.AsAsyncFunc(),
                                (existingConnector) =>
                                {
                                    return onRelationshipAlreadyExists(existingConnector.connectorId).AsTask();
                                });
                        },
                        () => onAdapterDoesNotExist(destinationAdapterId));
                },
                () => onAdapterDoesNotExist(sourceAdapterId));
        }

        public static async Task<TResult> UpdateConnectorAsync<TResult>(Guid connectorId,
                Connector.SynchronizationMethod flow,
                Guid performingAsActorId, Claim[] claims,
            Func<TResult> onCreated,
            Func<TResult> onAdapterDoesNotExist,
            Func<string, TResult> onFailure)
        {
            return await Persistence.ConnectorDocument.UpdateAsync(connectorId,
                async (connector, saveAsync) =>
                {
                    await saveAsync(flow);
                    return onCreated();
                },
                onAdapterDoesNotExist);
        }

        public static Task<TResult> FindByIdAsync<TResult>(Guid connectorId,
                Guid performingAsAuthorization, System.Security.Claims.Claim[] claims,
            Func<Connector, TResult> onFound,
            Func<TResult> onNotFound,
            Func<TResult> onUnauthorized)
        {
            // TODO: SEcurity check
            return FindByIdAsync(connectorId,
                onFound,
                onNotFound);
        }

        public static Task<TResult> FindByIdAsync<TResult>(Guid connectorId,
            Func<Connector, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return Persistence.ConnectorDocument.FindByIdAsync(connectorId,
                (connector) =>
                {
                    return onFound(connector);
                },
                onNotFound);
        }

        public static Task<TResult> FindByIdAsync<TResult>(Guid connectorId, Guid integrationId,
            Func<Connector, Guid, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return Persistence.ConnectorDocument.FindByIdAsync(connectorId, integrationId,
                (connector, integrationIdOther) =>
                {
                    return onFound(connector, integrationIdOther);
                },
                onNotFound);
        }

        public static Task<TResult> FindByAdapterAsync<TResult>(Guid adapterId,
                Guid performingAsAuthorization, System.Security.Claims.Claim[] claims,
            Func<KeyValuePair<Connector, Guid>[], TResult> onFound,
            Func<TResult> onAdapterNotFound,
            Func<TResult> onUnauthorized)
        {
            return Persistence.ConnectorDocument.FindByAdapterAsync(adapterId,
                (adapter, connectors) =>
                {
                    return onFound(connectors);
                },
                onAdapterNotFound);
        }

        public static Task<TResult> FindConnectionByAdapterAsync<TResult>(Guid adapterId,
                Guid performingAsAuthorization, System.Security.Claims.Claim[] claims,
            Func<Connection[], TResult> onFound,
            Func<TResult> onAdapterNotFound,
            Func<TResult> onUnauthorized)
        {
            return Persistence.ConnectorDocument.FindByAdapterWithConnectionAsync(adapterId,
                (adapter, connectorAdapters) =>
                {
                    return onFound(
                        connectorAdapters
                            .Select(
                                connectorAdapter => 
                                    Convert(adapter, connectorAdapter.Key, connectorAdapter.Value))
                            .ToArray());
                },
                onAdapterNotFound);
        }

        private static Connection Convert(Adapter source, Connector connector, Adapter remote)
        {
            return new Connection()
            {
                connector = new Connector()
                {
                    connectorId = connector.connectorId,
                    adapterInternalId = source.adapterId,
                    adapterExternalId = connector.adapterInternalId == source.adapterId ?
                                            connector.adapterExternalId
                                            :
                                            connector.adapterInternalId,
                    synchronizationMethod = connector.adapterInternalId == source.adapterId ?
                                            connector.synchronizationMethod
                                            :
                                            connector.synchronizationMethod == Connector.SynchronizationMethod.useExternal ?
                                                Connector.SynchronizationMethod.useInternal
                                                :
                                                connector.synchronizationMethod == Connector.SynchronizationMethod.useInternal ?
                                                    Connector.SynchronizationMethod.useExternal
                                                    :
                                                    connector.synchronizationMethod,
                    createdBy = connector.createdBy,
                },
                adapterInternal = source,
                adapterExternal = remote,
            };
        }

        public static Task<TResult> DeleteByIdAsync<TResult>(Guid connectorId,
                Guid performingAsAuthorization, System.Security.Claims.Claim[] claims,
            Func<TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            return Persistence.ConnectorDocument.DeleteByIdAsync(connectorId,
                (localAdapter, remoteAdapter) =>
                {
                    return onDeleted();
                    //Synchronizations synchronizations;
                    //if ( !synchronizations..ContainsKey(resourceType))
                    //    return localId.PairWithValue(remoteId).PairWithValue($"Type [{resourceType}] is not supported by this integration");

                    //return await await this.resources[resourceType].DeleteAsync(service, remoteId,
                    //    async () =>
                    //    {
                    //        await deleteAsync();
                    //        return localId.PairWithValue(remoteId).PairWithValue(resourceType);
                    //    },
                    //    async () =>
                    //    {
                    //        await deleteAsync();
                    //        return localId.PairWithValue(remoteId).PairWithValue($"{resourceType}:None found");
                    //    },
                    //    async () =>
                    //    {
                    //        await deleteAsync();
                    //        return localId.PairWithValue(remoteId).PairWithValue($"{resourceType}:Delete not supported");
                    //    },
                    //    (why) => localId.PairWithValue(remoteId).PairWithValue($"{resourceType}:{why}").ToTask());
                    //return onDeleted();
                },
                onNotFound);
        }
    }
}

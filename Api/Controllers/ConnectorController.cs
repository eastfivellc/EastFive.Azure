﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Routing;
using System.Threading.Tasks;
using System.Linq.Expressions;

using BlackBarLabs;
using EastFive.Collections.Generic;
using EastFive;
using BlackBarLabs.Api;
using EastFive.Api.Controllers;
using EastFive.Extensions;
using EastFive.Linq;
using BlackBarLabs.Extensions;

using EastFive.Api;
using EastFive.Azure.Synchronization;
using EastFive.Security.SessionServer;

namespace EastFive.Api.Controllers
{
    [FunctionViewController(Route = "Connector")]
    public class ConnectorController
    {

        #region GET

        [EastFive.Api.HttpGet]
        public static Task<HttpResponseMessage> FindByIdAsync([QueryDefaultParameter][Required]Guid id,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            ContentResponse onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return Connectors.FindByIdAsync(id,
                    security.performingAsActorId, security.claims,
                (synchronization, destinationIntegrationId) => onFound(GetResource(synchronization, destinationIntegrationId, url)),
                () => onNotFound(),
                () => onUnauthorized());
        }
        
        [EastFive.Api.HttpGet]
        public static Task<Task<HttpResponseMessage>> FindByAdapterAsync([Required(Name ="adapter")]Guid adapterId,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            MultipartAcceptArrayResponseAsync onMultipart,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return EastFive.Azure.Synchronization.Connectors.FindByAdapterAsync(adapterId,
                    security.performingAsActorId, security.claims,
                connectors =>
                {
                    var r = onMultipart(connectors.Select(connector => GetResource(connector.Key, connector.Value, url)));
                    return r;
                },
                () => onReferenceNotFound().ToTask(),
                () => onUnauthorized().ToTask());
        }

        internal static EastFive.Api.Resources.Connector GetResource(Connector connector, Guid destinationIntegrationId,
            System.Web.Http.Routing.UrlHelper url)
        {
            var resource = new EastFive.Api.Resources.Connector()
            {
                Id = url.GetWebId<ConnectorController>(connector.connectorId),
                Flow = Enum.GetName(typeof(Connector.SynchronizationMethod), connector.synchronizationMethod),

                Source = url.GetWebId<Controllers.AdapterController>(connector.adapterInternalId),
                Destination = url.GetWebId<Controllers.AdapterController>(connector.adapterExternalId),

                DestinationIntegration = destinationIntegrationId,
            };
            return resource;
        }

        #endregion

        [EastFive.Api.HttpPost(Type = typeof(EastFive.Api.Resources.Connector), MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> CreateConnectorAsync([PropertyGuid]Guid id,
                [PropertyGuid(Name = EastFive.Api.Resources.Connector.SourcePropertyName)]Guid source,
                [PropertyGuid(Name = EastFive.Api.Resources.Connector.DestinationPropertyName)]Guid? destination,
                [PropertyEnum(Name = EastFive.Api.Resources.Connector.FlowPropertyName)]Connector.SynchronizationMethod Flow,
                [PropertyGuid(Name = EastFive.Api.Resources.Connector.DestinationIntegrationPropertyName)]Guid? destinationIntegration,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            CreatedResponse onCreated,
            CreatedBodyResponse onCreatedAndModified,
            AlreadyExistsResponse onAlreadyExists,
            AlreadyExistsReferencedResponse onRelationshipAlreadyExists,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized,
            GeneralConflictResponse onFailure)
        {
            return Connectors.CreateConnectorAsync(id, source, destination,
                    Flow, destinationIntegration, security.performingAsActorId, security.claims,
                () => onCreated(),
                (connection) =>
                {
                    return request.Headers.Accept
                        .OrderByDescending(accept => accept.Quality.HasValue ? accept.Quality.Value : 1.0)
                        .First(
                            (accept, next) =>
                            {
                                if(
                                    accept.MediaType.ToLower() == "x-ordering/connection" || 
                                    accept.MediaType.ToLower() == "x-ordering/connection+json")
                                    return onCreatedAndModified(
                                        ConnectionController.GetResource(connection, url),
                                        "x-ordering/connection+json");

                                if (
                                    accept.MediaType.ToLower() == "x-ordering/connector" ||
                                    accept.MediaType.ToLower() == "x-ordering/connector+json" ||
                                    accept.MediaType.ToLower() == "application/json")
                                    return onCreatedAndModified(
                                        GetResource(connection.connector, connection.adapterExternal.integrationId, url),
                                        "x-ordering/connector+json");
                                
                                return next();
                            },
                            () => onCreatedAndModified(GetResource(connection.connector, connection.adapterExternal.integrationId, url)));
                },
                () => onAlreadyExists(),
                (existingConnectorId) => onRelationshipAlreadyExists(existingConnectorId),
                (brokenId) => onReferenceNotFound(),
                (why) => onFailure(why));
        }


        [EastFive.Api.HttpPut(Type = typeof(EastFive.Api.Resources.Connector), MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> UpdateConnectorAsync([PropertyGuid]Guid id,
                [PropertyGuid(Name = EastFive.Api.Resources.Connector.SourcePropertyName)]Guid source,
                [PropertyGuid(Name = EastFive.Api.Resources.Connector.DestinationPropertyName)]Guid? destination,
                [PropertyEnum(Name = EastFive.Api.Resources.Connector.FlowPropertyName)]Connector.SynchronizationMethod Flow,
                [PropertyGuid(Name = EastFive.Api.Resources.Connector.DestinationIntegrationPropertyName)]Guid? destinationIntegration,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            NoContentResponse onUpdated,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized,
            GeneralConflictResponse onFailure)
        {
            return Connectors.UpdateConnectorAsync(id,
                    Flow, security.performingAsActorId, security.claims,
                () => onUpdated(),
                () => onNotFound(),
                (why) => onFailure(why));
        }

        [EastFive.Api.HttpDelete(
            Type = typeof(EastFive.Api.Resources.Connector),
            MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> DeleteByIdAsync(
                [QueryDefaultParameter][PropertyGuid(Name = ResourceBase.IdPropertyName)]Guid synchronizationId,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            ContentResponse onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return Connectors.DeleteByIdAsync(synchronizationId,
                    security.performingAsActorId, security.claims,
                () => onFound(true),
                () => onNotFound());
        }

        [EastFive.Api.HttpOptions]
        public static HttpResponseMessage Options(HttpRequestMessage request, UrlHelper url,
            ContentResponse onOption,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            var adapter1Id = Guid.NewGuid();
            var adapter2Id = Guid.NewGuid();
            var connectorId = Guid.NewGuid();
            var integration1 = Guid.NewGuid();
            return onOption(
                GetResource(
                    new Connector()
                    {
                        adapterExternalId = adapter1Id,
                        adapterInternalId = adapter2Id,
                        connectorId = connectorId,
                        createdBy = adapter1Id,
                        synchronizationMethod = Connector.SynchronizationMethod.useExternal,
                    },
                    Guid.NewGuid(),
                    url));
        }

    }
}

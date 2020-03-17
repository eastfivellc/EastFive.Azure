using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Microsoft.AspNetCore.Mvc.Routing;
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
    //[FunctionViewController(Route = "Connector")]
    public class ConnectorController
    {

        #region GET

        [EastFive.Api.HttpGet]
        public static Task<HttpResponseMessage> FindByIdAsync(
                [QueryParameter(CheckFileName = true)]Guid id,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            ContentResponse onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return Connectors.FindByIdAsync(id,
                    security.performingAsActorId, security.claims,
                (synchronization) => onFound(GetResource(synchronization, url)),
                () => onNotFound(),
                () => onUnauthorized());
        }
        
        [EastFive.Api.HttpGet]
        public static async Task<HttpResponseMessage> FindByAdapterAsync(
                [QueryParameter(Name ="adapter")]Guid adapterId,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            MultipartAcceptArrayResponseAsync onMultipart,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return await await EastFive.Azure.Synchronization.Connectors.FindByAdapterAsync(adapterId,
                    security.performingAsActorId, security.claims,
                connectors =>
                {
                    var r = onMultipart(connectors.Select(connector => GetResource(connector.Key, url)));
                    return r;
                },
                () => onReferenceNotFound().ToTask(),
                () => onUnauthorized().ToTask());
        }

        internal static EastFive.Api.Resources.Connector GetResource(Connector connector,
            UrlHelper url)
        {
            var resource = new EastFive.Api.Resources.Connector()
            {
                Id = url.GetWebId<ConnectorController>(connector.connectorId),
                Flow = Enum.GetName(typeof(Connector.SynchronizationMethod), connector.synchronizationMethod),

                Source = url.GetWebId<Controllers.AdapterController>(connector.adapterInternalId),
                Destination = url.GetWebId<Controllers.AdapterController>(connector.adapterExternalId),
                
            };
            return resource;
        }

        #endregion

        [EastFive.Api.HttpPost(Type = typeof(EastFive.Api.Resources.Connector), MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> CreateConnectorAsync(
                [Property]Guid id,
                [Property(Name = EastFive.Api.Resources.Connector.SourcePropertyName)]Guid source,
                [Property(Name = EastFive.Api.Resources.Connector.DestinationPropertyName)]Guid destination,
                [Property(Name = EastFive.Api.Resources.Connector.FlowPropertyName)]Connector.SynchronizationMethod Flow,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            CreatedResponse onCreated,
            CreatedBodyResponse<Resources.Connection> onCreatedAndModifiedConnection,
            CreatedBodyResponse<Resources.Connector> onCreatedAndModified,
            AlreadyExistsResponse onAlreadyExists,
            AlreadyExistsReferencedResponse onRelationshipAlreadyExists,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized,
            GeneralConflictResponse onFailure)
        {
            return Connectors.CreateConnectorAsync(id, source, destination,
                    Flow, security.performingAsActorId, security.claims,
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
                                    return onCreatedAndModifiedConnection(
                                        ConnectionController.GetResource(connection, url),
                                        "x-ordering/connection+json");

                                if (
                                    accept.MediaType.ToLower() == "x-ordering/connector" ||
                                    accept.MediaType.ToLower() == "x-ordering/connector+json" ||
                                    accept.MediaType.ToLower() == "application/json")
                                    return onCreatedAndModified(
                                        GetResource(connection.connector, url),
                                        "x-ordering/connector+json");
                                
                                return next();
                            },
                            () => onCreatedAndModified(GetResource(connection.connector, url)));
                },
                () => onAlreadyExists(),
                (existingConnectorId) => onRelationshipAlreadyExists(existingConnectorId),
                (brokenId) => onReferenceNotFound(),
                (why) => onFailure(why));
        }


        [EastFive.Api.HttpPut(Type = typeof(EastFive.Api.Resources.Connector), MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> UpdateConnectorAsync(
                [Property]Guid id,
                [Property(Name = EastFive.Api.Resources.Connector.SourcePropertyName)]Guid source,
                [PropertyOptional(Name = EastFive.Api.Resources.Connector.DestinationPropertyName)]Guid? destination,
                [Property(Name = EastFive.Api.Resources.Connector.FlowPropertyName)]Connector.SynchronizationMethod Flow,
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
                [QueryParameter(CheckFileName = true, Name = ResourceBase.IdPropertyName)]Guid synchronizationId,
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
                    url));
        }

    }
}

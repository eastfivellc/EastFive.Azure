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

using EastFive.Security.SessionServer.Api.Controllers;
using EastFive.Api;
using EastFive.Security.SessionServer;
using EastFive.Api.Azure.Credentials.Controllers;

namespace EastFive.Api.Controllers
{
    [FunctionViewController(Route = "Adapter")]
    public class AdapterController
    {

        #region GET
        
        [EastFive.Api.HttpGet]
        public static Task<HttpResponseMessage> FindByIdAsync(
                [QueryParameter(CheckFileName = true, Name ="id")] Guid adapterId,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            ContentResponse onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return EastFive.Azure.Synchronization.Connections.FindAdapterByIdAsync(adapterId,
                (adapter) => onFound(GetResource(adapter, url)),
                () => onNotFound());
        }

        
        [EastFive.Api.HttpGet]
        public static async Task<HttpResponseMessage> FindByRelatedAsync(
                [QueryParameter]Guid relatedTo, [QueryParameter]Guid integration, // int top, int skip
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            MultipartAcceptArrayResponseAsync onMultipart,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return await await EastFive.Azure.Synchronization.Connections.FindAdaptersByRelatedAsync(relatedTo, integration,
                    security.claims,
                synchronizations =>
                {
                    var r = onMultipart(synchronizations.Select(synchronization => GetResource(synchronization, url)));
                    return r;
                },
                () => onReferenceNotFound().ToTask(),
                () => onUnauthorized().ToTask());
        }
        
        [EastFive.Api.HttpGet]
        public static Task<HttpResponseMessage> FindByRelatedAsync([QueryParameter]string key, [QueryParameter]Guid integration, [QueryParameter]string resourceType,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            ContentResponse onFound,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return EastFive.Azure.Synchronization.Connections.FindAdapterByKeyAsync(key, integration, resourceType,
                    security.claims,
                adapter =>
                {
                    var r = onFound(GetResource(adapter, url));
                    return r;
                },
                () => onReferenceNotFound(),
                () => onUnauthorized());
        }

        [EastFive.Api.HttpGet]
        public static async Task<HttpResponseMessage> FindByIntegrationAsync(
                [QueryParameter]Guid integration,
                [QueryParameter]string resourceType,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            MultipartAcceptArrayResponseAsync onMultipart,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return await await EastFive.Azure.Synchronization.Synchronizations.FindAdaptersByIntgrationAndResourceTypeAsync(integration, resourceType,
                    security.performingAsActorId, security.claims,
                synchronizations =>
                {
                    var r = onMultipart(synchronizations.Select(synchronization => GetResource(synchronization, url)));
                    return r;
                },
                // TODO: Clean these up
                () => onReferenceNotFound().ToTask(),
                () => onReferenceNotFound().AddReason($"Resource type [{resourceType}] is not currently supported.").ToTask(),
                () => onReferenceNotFound().AddReason($"The integration needs to be authenticated before it can be queried (this should be a 409).").ToTask(),
                () => onUnauthorized().ToTask(),
                (why) => onReferenceNotFound().AddReason(why + " (this should be a 409)").ToTask());
        }

        #endregion
        
        internal static EastFive.Api.Resources.Adapter GetResource(EastFive.Azure.Synchronization.Connection connection,
            UrlHelper url)
        {
            var adapter = connection.adapterInternal;
            return GetResource(adapter, url);
        }
        
        internal static EastFive.Api.Resources.Adapter GetResource(
            EastFive.Azure.Synchronization.Adapter adapter,
            UrlHelper url)
        {
            var resource = new EastFive.Api.Resources.Adapter()
            {
                Id = url.GetWebId<AdapterController>(adapter.adapterId),
                ResourceType = adapter.resourceType,

                //ResourceId = ServiceConfiguration.ResourceControllerLookup(adapter.resourceType,
                //    controllerType => url.GetWebId(controllerType, adapter.key),
                //    () => default(BlackBarLabs.Api.Resources.WebId)),
                ResourceKey = adapter.key,
                Name = adapter.name,
                Keys = adapter.identifiers.NullToEmpty().SelectKeys().ToArray(),
                Values = adapter.identifiers.NullToEmpty().SelectValues().ToArray(),
                Integration = url.GetWebId<AdapterController>(adapter.integrationId),
                Connectors = adapter.connectorIds.NullToEmpty().Select(connectionId => url.GetWebId<ConnectorController>(connectionId)).ToArray(),
            };
            return resource;
        }

    }
}

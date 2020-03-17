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
    [FunctionViewController(Route = "Connection")]
    public class ConnectionController
    {
        #region GET
        
        [EastFive.Api.HttpGet]
        public static async Task<HttpResponseMessage> FindByAdapterAsync([QueryParameter]Guid adapter,
                Security security, Context context, HttpRequestMessage request, UrlHelper url,
            MultipartAcceptArrayResponseAsync onMultipart,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return await await EastFive.Azure.Synchronization.Connectors.FindConnectionByAdapterAsync(adapter,
                    security.performingAsActorId, security.claims,
                connectors =>
                {
                    var r = onMultipart(connectors.Select(connector => GetResource(connector, url)));
                    return r;
                },
                () => onReferenceNotFound().ToTask(),
                () => onUnauthorized().ToTask());
        }

        internal static EastFive.Api.Resources.Connection GetResource(Connection connection,
            UrlHelper url)
        {
            var resource = new EastFive.Api.Resources.Connection()
            {
                Id = url.GetWebId<ConnectionController>(connection.connector.connectorId),
                Connector = ConnectorController.GetResource(connection.connector, url),
                Source = AdapterController.GetResource(connection.adapterInternal, url),
                Destination = AdapterController.GetResource(connection.adapterExternal, url),
            };
            return resource;
        }

        #endregion
        
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
                    new Connection()
                    {
                        connector = new Connector()
                        {
                            adapterExternalId = adapter1Id,
                            adapterInternalId = adapter2Id,
                            connectorId = connectorId,
                            createdBy = adapter1Id,
                            synchronizationMethod = Connector.SynchronizationMethod.useExternal,
                        },
                        adapterInternal = new Adapter()
                        {
                            adapterId = adapter1Id,
                            connectorIds = new Guid[] { connectorId },
                            identifiers = new KeyValuePair<string, string> []
                            {
                                "name".PairWithValue("Example Name"),
                            },
                            integrationId = integration1,
                            key = Guid.NewGuid().ToString(),
                            name = "Example",
                            resourceType = "Product",
                        },
                        adapterExternal = new Adapter()
                        {
                            adapterId = adapter1Id,
                            connectorIds = new Guid[] { connectorId },
                            identifiers = new KeyValuePair<string, string>[]
                            {
                                "name".PairWithValue("Test Name"),
                            },
                            integrationId = integration1,
                            key = Guid.NewGuid().ToString("N"),
                            name = "Test",
                            resourceType = "Product",
                        },
                    }, url));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.Routing;

using BlackBarLabs.Api;
using System.Linq;
using BlackBarLabs;
using EastFive;
using BlackBarLabs.Extensions;
using EastFive.Security.SessionServer;

namespace EastFive.Api.Azure
{
    public static class RoleActions
    {
        public static async Task<HttpResponseMessage> CreateAsync(this Resources.Role role, HttpRequestMessage request, UrlHelper url)
        {
            var roleId = role.Id.ToGuid();
            if (!roleId.HasValue)
                return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("Id must have value");

            var actorId = role.Actor.ToGuid();
            if (!actorId.HasValue)
                return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("Actor must have value");

            var context = request.GetSessionServerContext();
            return await context.Roles.CreateAsync(roleId.Value, actorId.Value, role.Name,
                () => request.CreateResponse(HttpStatusCode.Created),
                () => request.CreateResponse(HttpStatusCode.Conflict).AddReason("Contact already exists"),
                () => request.CreateResponse(HttpStatusCode.Conflict).AddReason("Actor does not exists"));
        }

        public static async Task<HttpResponseMessage> GetAsync(this Resources.Queries.RoleQuery query, HttpRequestMessage request, UrlHelper url)
        {
            return await query.ParseAsync(request,
                (q) => QueryById(q.Id.ParamSingle(), request, url),
                (q) => QueryByIds(q.Id.ParamOr(), request, url),
                (q) => QueryByActorId(q.Actor.ParamSingle(), request, url));
        }

        private static Task<HttpResponseMessage[]> QueryByIds(IEnumerable<Guid> contactIds, HttpRequestMessage request, UrlHelper url)
        {
            return contactIds
                .Select(contactId => QueryById(contactId, request, url))
                .WhenAllAsync<HttpResponseMessage>();
        }

        private static async Task<HttpResponseMessage> QueryById(Guid roleId, HttpRequestMessage request, UrlHelper url)
        {
            var context = request.GetSessionServerContext();
            return await context.Roles.GetByIdAsync(roleId,
                (role) =>
                {
                    var roleResource = new Resources.Role()
                    {
                        //Id = url.GetWebId<Controllers.RoleController>(role.id),
                        Actor = Library.configurationManager.GetActorLink(role.actorId, url),
                        Name = role.name,
                    };
                    return request.CreateResponse(HttpStatusCode.OK, roleResource);
                },
                () => request.CreateResponse(HttpStatusCode.NotFound));
        }

        private static async Task<HttpResponseMessage[]> QueryByActorId(Guid actorId, HttpRequestMessage request, UrlHelper url)
        {
            var context = request.GetSessionServerContext();
            return await context.Roles.GetByActorIdAsync(actorId,
                (roles) =>
                {
                    var resources = roles.Select(role =>
                    {
                        var resourceRole = new Resources.Role()
                        {
                            //Id = url.GetWebId<Controllers.RoleController>(role.id),
                            Actor = Library.configurationManager.GetActorLink(role.actorId, url),
                            Name = role.name,
                        };

                        return request.CreateResponse(HttpStatusCode.OK, resourceRole);
                    }).ToArray();
                    return resources;
                },
                () => request.CreateResponse(HttpStatusCode.NotFound).AsEnumerable().ToArray());
        }

        public static async Task<HttpResponseMessage> UpdateAsync(this Resources.Role contact,
            HttpRequestMessage request, UrlHelper url)
        {
            var context = request.GetSessionServerContext();
            return await context.Roles.UpdateAsync(contact.Id.UUID,
                    contact.Actor.ToGuid(), contact.Name,
                () => request.CreateResponse(HttpStatusCode.NoContent), 
                (why) => request.CreateResponse(HttpStatusCode.Conflict).AddReason(why),
                () => request.CreateResponse(HttpStatusCode.NotFound));
        }

        public async static Task<HttpResponseMessage> DeleteAsync(this Resources.Queries.RoleQuery query, HttpRequestMessage request, UrlHelper url)
        {
            return await query.ParseAsync(request,
                (q) => DeleteById(q.Id.ParamSingle(), request, url));
        }

        private static async Task<HttpResponseMessage> DeleteById(Guid roleId, HttpRequestMessage request, UrlHelper url)
        {
            var context = request.GetSessionServerContext();
            return await context.Roles.DeleteByIdAsync(roleId,
                () => request.CreateResponse(HttpStatusCode.NoContent),
                () => request.CreateResponse(HttpStatusCode.NotFound));
        }
    }
}
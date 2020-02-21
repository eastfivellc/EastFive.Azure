//using BlackBarLabs.Api;
//using BlackBarLabs.Extensions;
//using EastFive.Collections.Generic;
//using EastFive.Extensions;
//using EastFive.Linq;
//using EastFive.Security.SessionServer;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Threading.Tasks;
//using System.Web.Http.Routing;

//namespace EastFive.Api.Azure.Credentials
//{
//    public static class IntegrationActions
//    {
//        //public static async Task<HttpResponseMessage> CreateAsync(this Resources.Integration authenticationRequest,
//        //    HttpRequestMessage request, UrlHelper urlHelper)
//        //{
//        //    var context = request.GetSessionServerContext();
//        //    var credentialId = authenticationRequest.Id.ToGuid();
//        //    if (!credentialId.HasValue)
//        //        return request.CreateResponse(HttpStatusCode.BadRequest).AddReason("Id must have value");
            
//        //    if (authenticationRequest.AuthorizationId.IsDefault())
//        //        return request.CreateResponseEmptyId(authenticationRequest, ar => ar.AuthorizationId)
//        //            .AddReason("Authorization Id must have value for integration");
            
//        //    return await request.GetActorIdClaimsAsync(
//        //        (actorId, claims) =>
//        //            context.Integrations.CreateLinkAsync(credentialId.Value,
//        //                    urlHelper.GetLocation<Controllers.OpenIdResponseController>(),
//        //                    authenticationRequest.Method, authenticationRequest.LocationAuthenticationReturn,
//        //                    authenticationRequest.AuthorizationId, actorId, claims,
//        //                    type => urlHelper.GetLocation(type),
//        //                (authenticationRequestPopulated) =>
//        //                {
//        //                    var resource = Convert(authenticationRequestPopulated, urlHelper);
//        //                    return request.CreateResponse(HttpStatusCode.Created, resource);
//        //                },
//        //                () => request.CreateAlreadyExistsResponse<Controllers.SessionController>(
//        //                    credentialId.Value, urlHelper),
//        //                (why) => request.CreateResponse(HttpStatusCode.Unauthorized).AddReason(why),
//        //                () => request.CreateResponse(HttpStatusCode.BadRequest)
//        //                    .AddReason($"Method [{authenticationRequest.Method}] is not enabled for this system"),
//        //                (why) => request.CreateResponse(HttpStatusCode.ServiceUnavailable)
//        //                    .AddReason(why),
//        //                (why) => request.CreateResponse(HttpStatusCode.InternalServerError)
//        //                    .AddReason(why)));
//        //}

        
//        #region Actionables

//        public static Task<HttpResponseMessage> QueryAsync(this Resources.Queries.IntegrationQuery query,
//            HttpRequestMessage request, UrlHelper urlHelper)
//        {
//            return request.GetActorIdClaimsAsync(
//                (actingAs, claims) => query.ParseAsync(request,
//                    q => QueryByIdAsync(q.Id.ParamSingle(), actingAs, claims, request, urlHelper),
//                    q => QueryByActorAsync(q.ActorId.ParamSingle(), actingAs, claims, request, urlHelper),
//                    q => QueryAllAsync(actingAs, claims, request, urlHelper)));
//        }

//        private static async Task<HttpResponseMessage> QueryByIdAsync(Guid authenticationRequestId,
//                Guid actingAs, System.Security.Claims.Claim[] claims,
//            HttpRequestMessage request, UrlHelper urlHelper)
//        {
//            var context = request.GetSessionServerContext();
//            return await context.Integrations.GetAsync(authenticationRequestId,
//                    (controllerType) => urlHelper.GetLocation(controllerType),
//                (authenticationRequest) =>
//                {
//                    var response = request.CreateResponse(HttpStatusCode.OK,
//                        Convert(authenticationRequest, urlHelper));
//                    return response;
//                },
//                () => request.CreateResponse(HttpStatusCode.NotFound),
//                (why) => request.CreateResponseUnexpectedFailure(why));
//        }

//        private static async Task<HttpResponseMessage[]> QueryByActorAsync(Guid actorId,
//                Guid actingAs, System.Security.Claims.Claim[] claims,
//            HttpRequestMessage request, UrlHelper urlHelper)
//        {
//            var context = request.GetSessionServerContext();
//            return await context.Integrations.GetByActorAsync(actorId,
//                    (controllerType) => urlHelper.GetLocation(controllerType),
//                    actingAs, claims,
//                (authenticationRequests) =>
//                {
//                    var response = authenticationRequests
//                        //.Append(
//                        //    new Session()
//                        //    {
//                        //        action = AuthenticationActions.link,
//                        //        authorizationId = actorId,
//                        //        id = actorId,
//                        //        method = "account",
//                        //        name = "Account",
//                        //    })
//                        .Select(authenticationRequest => 
//                            request.CreateResponse(HttpStatusCode.OK, Convert(authenticationRequest, urlHelper)))
//                        .ToArray();
//                    return response;
//                },
//                () => request.CreateResponse(HttpStatusCode.NotFound).AsArray(),
//                () => request.CreateResponse(HttpStatusCode.Unauthorized).AsArray(),
//                (why) => request.CreateResponseUnexpectedFailure(why).AsArray());
//        }

//        private static async Task<HttpResponseMessage[]> QueryAllAsync(
//                Guid performingAsActor, System.Security.Claims.Claim[] claims,
//            HttpRequestMessage request, UrlHelper urlHelper)
//        {
//            var context = request.GetSessionServerContext();
//            return await context.Integrations.GetAllAsync(
//                    (controllerType) => urlHelper.GetLocation(controllerType),
//                    performingAsActor, claims,
//                (authenticationRequests) =>
//                {
//                    var response = authenticationRequests
//                        .Select(authenticationRequest =>
//                            request.CreateResponse(HttpStatusCode.OK, Convert(authenticationRequest, urlHelper)))
//                        .ToArray();
//                    return response;
//                },
//                () => request.CreateResponse(HttpStatusCode.NotFound).AsArray(),
//                () => request.CreateResponse(HttpStatusCode.Unauthorized).AsArray(),
//                (why) => request.CreateResponseUnexpectedFailure(why).AsArray());
//        }

//        private static Resources.Integration Convert(Session authenticationRequest, UrlHelper urlHelper)
//        {
//            var userParameters = (Dictionary<string, EastFive.Security.SessionServer.CustomParameter>) authenticationRequest.userParams ?? new Dictionary<string, EastFive.Security.SessionServer.CustomParameter>();
//            return new Resources.Integration
//            {
//                Id = urlHelper.GetWebId<Controllers.IntegrationController>(authenticationRequest.id),
//                Name = authenticationRequest.name,
//                Method = authenticationRequest.method,
//                AuthorizationId = authenticationRequest.authorizationId.HasValue ?
//                    authenticationRequest.authorizationId.Value
//                    :
//                    default(Guid),
//                ExtraParams = authenticationRequest.extraParams
//                    .NullToEmpty()
//                    .ToDictionary(),
//                UserParameters = userParameters
//                    .NullToEmpty()
//                    .Select(
//                        param => param.Key.PairWithValue(
//                            new Resources.Integration.CustomParameter
//                                {
//                                    Label = param.Value.Label,
//                                    Type = param.Value.Type.Name,
//                                    Description = param.Value.Description,
//                                    Value = param.Value.Value
//                                }))
//                    .ToDictionary(),
//                LocationAuthentication = authenticationRequest.loginUrl,
//                LocationAuthenticationReturn = authenticationRequest.redirectUrl,
//                ResourceTypes = authenticationRequest.resourceTypes
//                    .NullToEmpty()
//                    .Select(
//                        resourceType => new Resources.AuthorizationRequest.ResourceType()
//                        {
//                            Name = resourceType.Value,
//                            Value = resourceType.Key,
//                            Type = new Uri($"urn:{resourceType.Key}::*"),
//                        })
//                    .ToArray(),
//            };
//        }

//        public static async Task<HttpResponseMessage> UpdateAsync(this Resources.Integration resource,
//            HttpRequestMessage request)
//        {
//            return await request.GetActorIdClaimsAsync(
//                async (actingAs, claims) =>
//                {
//                    var context = request.GetSessionServerContext();

//                    var application = request.GetApplication(
//                        app => app as AzureApplication,
//                        () => null);

//                    // Can't update a session that does not exist
//                    var session = await context.Integrations.UpdateAsync(resource.Id.ToGuid().Value,
//                        actingAs, claims, application, resource.Name,
//                        resource.UserParameters
//                            .ToDictionary(
//                                userParam => userParam.Key,
//                                userParam => userParam.Value.Value),
//                        ()=> request.CreateResponse(HttpStatusCode.NoContent),
//                        (sessionId, authId, token, refreshToken, action, extraParams, redirectUrl) =>
//                        {
//                            resource.AuthorizationId = authId;
//                            return request.CreateResponse(HttpStatusCode.Accepted, resource);
//                        },
//                        (logoutRedirect, why, extraParams) => request.CreateRedirectResponse(logoutRedirect).AddReason(why),
//                        (why) => request.CreateResponse(HttpStatusCode.NotFound).AddReason(why),
//                        () => request.CreateResponse(HttpStatusCode.Conflict).AddReason("User in token is not connected to this system"),
//                        (why) => request.CreateResponse(HttpStatusCode.BadGateway).AddReason(why),
//                        (why) => request.CreateResponseConfiguration(string.Empty, why),
//                        (why) => request.CreateResponseUnexpectedFailure(why));
//                    return session;
//                });
//        }

//        public static async Task<HttpResponseMessage> DeleteAsync(this Resources.Queries.IntegrationQuery credential,
//            HttpRequestMessage request, UrlHelper urlHelper)
//        {
//            return await request.GetActorIdClaimsAsync(
//                (actingAs, claims) =>
//                    credential.ParseAsync(request,
//                        q => DeleteByIdAsync(q.Id.ParamSingle(), actingAs, claims, request, urlHelper)));
//        }

//        private static async Task<HttpResponseMessage> DeleteByIdAsync(Guid integrationId,
//                Guid actingAs, System.Security.Claims.Claim [] claims,
//            HttpRequestMessage request, UrlHelper urlHelper)
//        {
//            var context = request.GetSessionServerContext();
//            return await context.Integrations.DeleteByIdAsync(integrationId,
//                    actingAs, claims, request,
//                (response) =>
//                {
//                    return response;
//                },
//                () => request.CreateResponse(HttpStatusCode.NotFound),
//                () => request.CreateResponse(HttpStatusCode.Unauthorized));
//        }

//        #endregion
//    }
//}
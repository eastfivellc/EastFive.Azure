﻿using BlackBarLabs.Api;
using BlackBarLabs.Api.Resources;
using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web.Http.Routing;
using EastFive.Api.Controllers;
using EastFive.Azure;
using BlackBarLabs.Extensions;
using System.Linq;
using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Extensions;

namespace EastFive.Api.Azure.Resources
{
    [DataContract]
    [FunctionViewController(Route = "ProcessStageType")]
    public class ProcessStageType : ResourceBase
    {
        public const string GroupPropertyName = "group";
        [JsonProperty(PropertyName = GroupPropertyName)]
        public WebId Group { get; set; }

        public const string OwnerPropertyName = "owner";
        [JsonProperty(PropertyName = OwnerPropertyName)]
        public WebId Owner { get; set; }

        public const string TitlePropertyName = "title";
        [JsonProperty(PropertyName = TitlePropertyName)]
        public string Title { get; set; }

        public const string ResourceTypePropertyName = "resource_type";
        [JsonProperty(PropertyName = ResourceTypePropertyName)]
        public string ResourceType { get; set; }

        public const string ResourceKeysPropertyName = "resource_keys";
        [JsonProperty(PropertyName = ResourceKeysPropertyName)]
        public string [] ResourceKeys { get; set; }

        public const string ResourceTypesPropertyName = "resource_types";
        [JsonProperty(PropertyName = ResourceTypesPropertyName)]
        public string[] ResourceTypes { get; set; }


        #region GET

        [EastFive.Api.HttpGet]
        public static async Task<HttpResponseMessage> FindByIdAsync(
                [QueryParameter(CheckFileName = true)]Guid processStageTypeId,
                EastFive.Api.Security security, HttpRequestMessage request, UrlHelper url,
            ContentResponse onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return await await ProcessStageTypes.FindAllAsync(security,
                types =>
                    types.First(
                        async (stage, next) =>
                        {
                            if (stage.processStageTypeId == processStageTypeId)
                                return onFound(stage);
                            return await next();
                        },
                        () => onNotFound().ToTask()),
                () => onUnauthorized().ToTask());

            //return Connectors.FindByIdAsync(id,
            //        security.performingAsActorId, security.claims,
            //    (synchronization, destinationIntegrationId) => onFound(GetResource(synchronization, destinationIntegrationId, url)),
            //    () => onNotFound(),
            //    () => onUnauthorized());
        }

        [EastFive.Api.HttpGet]
        public static async Task<HttpResponseMessage> FindAllAsync(
                EastFive.Api.Security security, HttpRequestMessage request, UrlHelper url,
            MultipartAcceptArrayResponseAsync onMultipart,
            UnauthorizedResponse onUnauthorized)
        {
            return await await ProcessStageTypes.FindAllAsync(security,
                types => onMultipart(types.Select(type => GetResource(type, url))),
                () => onUnauthorized().ToTask());
        }

        internal static Resources.ProcessStageType GetResource(EastFive.Azure.ProcessStageType processStageType, UrlHelper urlHelper)
        {
            return new Resources.ProcessStageType
            {
                Id = urlHelper.GetWebId<EastFive.Api.Azure.Resources.ProcessStageType>(processStageType.processStageTypeId),

                Group = urlHelper.GetWebId<EastFive.Api.Azure.Resources.ProcessStageGroup>(processStageType.processStageGroupId),

                Title = processStageType.title,

                ResourceType = processStageType.resourceType.GetCustomAttribute<EastFive.Api.HttpResourceAttribute, string>(
                    attr => attr.ResourceName,
                    () => processStageType.resourceType.AssemblyQualifiedName),
                ResourceTypes = processStageType.resourceKeys
                    .SelectValues(
                        type => processStageType.resourceType.GetCustomAttribute<EastFive.Api.HttpResourceAttribute, string>(
                            attr => attr.ResourceName,
                            () => processStageType.resourceType.AssemblyQualifiedName))
                    .ToArray(),
                ResourceKeys = processStageType.resourceKeys
                    .SelectKeys()
                    .ToArray(),
            };
        }

        #endregion

        [EastFive.Api.HttpPost(Type = typeof(Resources.ProcessStageType), MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> CreateAsync(
                [Property(Name = Resources.ProcessStageType.IdPropertyName)]Guid processStageTypeId,
                [Property(Name = Resources.ProcessStageType.OwnerPropertyName)]Guid ownerId,
                [Property(Name = Resources.ProcessStageType.GroupPropertyName)]Guid processStageGroupId,
                [Property(Name = Resources.ProcessStageType.TitlePropertyName)]string title,
                [Property(Name = Resources.ProcessStageType.ResourceTypePropertyName)]Type resourceType,
                [Property(Name = Resources.ProcessStageType.ResourceKeysPropertyName)]string[] resourceKeys,
                [Property(Name = Resources.ProcessStageType.ResourceTypesPropertyName)]Type[] resourceTypes,
                EastFive.Api.Security security, HttpRequestMessage request, UrlHelper url,
            CreatedResponse onCreated,
            CreatedBodyResponse<ProcessStageType> onCreatedAndModified,
            AlreadyExistsResponse onAlreadyExists,
            AlreadyExistsReferencedResponse onRelationshipAlreadyExists,
            ReferencedDocumentNotFoundResponse onReferenceNotFound,
            UnauthorizedResponse onUnauthorized,
            GeneralConflictResponse onFailure)
        {
            var resourceList = resourceKeys.Zip(resourceTypes, (k, v) => k.PairWithValue(v)).ToArray();
            return ProcessStageTypes.CreateAsync(processStageTypeId, ownerId, processStageGroupId, title,
                    resourceType, resourceList,
                    security,
                () => onCreated(),
                () => onAlreadyExists(),
                () => onReferenceNotFound(),
                (brokenId) => onReferenceNotFound(),
                (why) => onFailure(why));
        }



        [EastFive.Api.HttpOptions(MatchAllBodyParameters = false)]
        public static HttpResponseMessage Options(HttpRequestMessage request, UrlHelper url,
            ContentResponse onOption)
        {
            var stage =
            //    new Resources.ProcessStageType
            //{
            //    Id = Guid.NewGuid(),
            //    Group = ProcessStagesGroups.group1Id,
            //    Title = "Buyer Confirm",
            //    ResourceType = "order",
            //    ResourceKeys = new string[] { "ship_to" },
            //    ResourceTypes = new string[] { "fulfillment" },
            //};
                new Resources.ProcessStageType
                {
                    Id = Guid.NewGuid(),
                    Group = ProcessStagesGroups.group2Id,
                    Title = "Seller Confirm",
                    ResourceType = "order",
                    ResourceKeys = new string[] { "ship_from" },
                    ResourceTypes = new string[] { "fulfillment" },
                };
            return onOption(stage);
        }

    }
}
﻿using BlackBarLabs.Api;
using BlackBarLabs.Api.Resources;
using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using System.Net.Http;
using EastFive.Api.Controllers;
using System.Linq;
using System.Web.Http.Routing;
using System.Threading.Tasks;
using EastFive.Linq;
using BlackBarLabs.Extensions;

namespace EastFive.Api.Azure.Resources
{
    [DataContract]
    [FunctionViewController(Route = "ProcessStep")]
    public class ProcessStep : ResourceBase
    {
        public const string StagePropertyName = "stage";
        [JsonProperty(PropertyName = StagePropertyName)]
        public WebId Stage { get; set; }

        public const string ResourcePropertyName = "resource";
        [JsonProperty(PropertyName = ResourcePropertyName)]
        public WebId Resource { get; set; }

        public const string CreatedOnPropertyName = "created_on";
        [JsonProperty(PropertyName = CreatedOnPropertyName)]
        public DateTime CreatedOn { get; set; }

        public const string ResourceKeysPropertyName = "resource_keys";
        [JsonProperty(PropertyName = ResourceKeysPropertyName)]
        public string[] ResourceKeys { get; set; }

        public const string ResourcesPropertyName = "resources";
        [JsonProperty(PropertyName = ResourcesPropertyName)]
        public WebId[] Resources { get; set; }
        
        public const string ConfirmedByPropertyName = "confirmed_by";
        [JsonProperty(PropertyName = ConfirmedByPropertyName)]
        public WebId ConfirmedBy { get; set; }

        public const string ConfirmedWhenPropertyName = "confirmed_when";
        [JsonProperty(PropertyName = ConfirmedWhenPropertyName)]
        public DateTime? ConfirmedWhen { get; set; }

        public const string PreviousPropertyName = "previous";
        [JsonProperty(PropertyName = PreviousPropertyName)]
        public WebId Previous { get; set; }


        #region GET

        [EastFive.Api.HttpGet]
        public static Task<HttpResponseMessage> FindByIdAsync(
                [QueryDefaultParameter][Required(Name = ProcessStep.IdPropertyName)]Guid id,
                AzureApplication httpApplication, EastFive.Api.Controllers.Security security, UrlHelper url,
            ContentResponse onFound,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return EastFive.Azure.Processes.FindByIdAsync(id, security,
                (process) =>
                    onFound(GetResource(process, httpApplication, url)),
                () => onNotFound(),
                () => onUnauthorized());
        }

        internal static Resources.ProcessStep GetResource(EastFive.Azure.Process process, AzureApplication httpApplication, UrlHelper urlHelper)
        {
            return new Resources.ProcessStep
            {
                Id = urlHelper.GetWebId<ProcessStep>(process.processId),
                Stage = urlHelper.GetWebId<EastFive.Api.Azure.Resources.ProcessStage>(process.processStageId),
                Resource = httpApplication.GetResourceLink(process.resourceType, process.resourceId, urlHelper),
                CreatedOn = process.createdOn,

                ConfirmedBy = process.confirmedBy.HasValue ?
                    Security.SessionServer.Library.configurationManager.GetActorLink(process.confirmedBy.Value, urlHelper)
                    :
                    default(WebId),
                ConfirmedWhen = process.confirmedWhen,
                Previous = urlHelper.GetWebId<ProcessStep>(process.previousStep),
                Resources = process.resources
                    .Select(resource => httpApplication.GetResourceLink(process.resourceType, resource.resourceId, urlHelper))
                    .ToArray(),
                ResourceKeys = process.resources
                    .Select(resource => (resource.key))
                    .ToArray(),
            };
        }

        #endregion

        [EastFive.Api.HttpPost(Type = typeof(Resources.ProcessStep), MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> CreateAsync(
                [Property(Name = ProcessStep.IdPropertyName)]Guid processId,
                [PropertyOptional(Name = ProcessStep.PreviousPropertyName)]Guid? previousStepId,
                [Property(Name = ProcessStep.ResourcePropertyName)]Guid resourceId,
                [Property(Name = ProcessStep.StagePropertyName)]Guid processStageId,
                [Property(Name = ProcessStep.CreatedOnPropertyName)]DateTime createdOn,
                [PropertyOptional(Name = ProcessStep.ConfirmedByPropertyName)]Guid? confirmedById,
                [PropertyOptional(Name = ProcessStep.ConfirmedWhenPropertyName)]DateTime? confirmedWhen,
                [PropertyOptional(Name = ProcessStep.ResourceKeysPropertyName)]string[] resourceKeys,
                [PropertyOptional(Name = ProcessStep.ResourcesPropertyName)]Guid[] resources,
                EastFive.Api.Controllers.Security security, UrlHelper url,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists,
            ReferencedDocumentDoesNotExistsResponse<Resources.ProcessStage> onStageNotFound,
            UnauthorizedResponse onUnauthorized,
            GeneralConflictResponse onFailure)
        {
            return EastFive.Azure.Processes.CreateAsync(processId, processStageId, resourceId, createdOn,
                    resourceKeys.NullToEmpty().Zip(resources.NullToEmpty(), (k, id) => k.PairWithValue(id)).ToArray(),
                    previousStepId, confirmedWhen, confirmedById,
                    security,
                () => onCreated(),
                () => onAlreadyExists(),
                () => onStageNotFound(),
                (why) => onFailure(why));
        }

        [EastFive.Api.HttpPatch(Type = typeof(Resources.ProcessStep), MatchAllBodyParameters = false)]
        public static Task<HttpResponseMessage> UpdateConnectorAsync(
                [Property(Name = ProcessStep.IdPropertyName)]Guid processId,
                [PropertyOptional(Name = ProcessStep.ConfirmedByPropertyName)]Guid? confirmedById,
                [PropertyOptional(Name = ProcessStep.ConfirmedWhenPropertyName)]DateTime? confirmedWhen,
                [PropertyOptional(Name = ProcessStep.ResourceKeysPropertyName)]string[] resourceKeys,
                [PropertyOptional(Name = ProcessStep.ResourcesPropertyName)]Guid[] resources,
                EastFive.Api.Controllers.Security security, UrlHelper url,
            NoContentResponse onUpdated,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized,
            GeneralConflictResponse onFailure)
        {
            return EastFive.Azure.Processes.UpdateAsync(processId,
                    confirmedById, confirmedWhen,
                    resourceKeys.NullToEmpty().Zip(resources.NullToEmpty(), (k, id) => k.PairWithValue(id)).ToArray(),
                    security,
                () => onUpdated(),
                () => onNotFound(),
                () => onUnauthorized(),
                (why) => onFailure(why));

            //return Connectors.UpdateConnectorAsync(id,
            //        Flow, security.performingAsActorId, security.claims,
            //    () => onUpdated(),
            //    () => onNotFound(),
            //    (why) => onFailure(why));
        }

        [EastFive.Api.HttpDelete]
        public static Task<HttpResponseMessage> DeleteByIdAsync(
                [QueryDefaultParameter][Required(Name = ProcessStep.IdPropertyName)]Guid processStepId,
                EastFive.Api.Controllers.Security security,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return EastFive.Azure.Processes.DeleteByIdAsync(processStepId, security,
                () => onDeleted(),
                () => onNotFound(),
                () => onUnauthorized());
        }

        [EastFive.Api.HttpOptions(MatchAllBodyParameters = false)]
        public static HttpResponseMessage Options(HttpRequestMessage request, UrlHelper url, AzureApplication application,
            ContentResponse onOption)
        {
            return onOption(
                GetResource(
                    new EastFive.Azure.Process()
                    {
                        processStageId = Guid.NewGuid(),
                        createdOn = DateTime.UtcNow,
                        processId = Guid.NewGuid(),
                        resourceId = Guid.NewGuid(),
                        resourceType = typeof(EastFive.Azure.ProcessStage),
                        confirmedBy = Guid.NewGuid(),
                        confirmedWhen = DateTime.UtcNow,
                        previousStep = Guid.NewGuid(),
                        resources = Enumerable
                            .Range(0, 3)
                            .Select(
                                i => new EastFive.Azure.Process.ProcessStageResource()
                                {
                                    key = $"key{i}",
                                    resourceId = Guid.NewGuid(),
                                    type = typeof(EastFive.Azure.Process),
                                })
                            .ToArray(),
                    },
                    application, url));
        }

    }
}
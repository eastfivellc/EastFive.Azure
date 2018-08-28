﻿using BlackBarLabs.Api;
using BlackBarLabs.Api.Resources;
using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using EastFive.Api;
using System.Net.Http;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using EastFive.Api.Controllers;
using System.Linq;
using BlackBarLabs.Extensions;
using System.Web.Http.Routing;

namespace EastFive.Azure.Api.Resources
{
    [DataContract]
    [FunctionViewController(
        Route = "ProcessResourceView",
        Resource = typeof(Resources.ProcessResourceView),
        ContentType = "x-application/process-resource-view",
        ContentTypeVersion = "0.1")]
    public class ProcessResourceView : ResourceBase
    {
        #region identification of the view resources
        public const string ActorPropertyName = "actor";
        [JsonProperty(PropertyName = ActorPropertyName)]
        public WebId Actor { get; set; }

        public const string ResourcePropertyName = "resource";
        [JsonProperty(PropertyName = ResourcePropertyName)]
        public WebId Resource { get; set; }
        
        [JsonProperty(PropertyName = ProcessStageType.ResourceTypePropertyName)]
        public string ResourceType { get; set; }
        #endregion

        #region progress bar layout
        public const string CurrentProcessStepPropertyName = "current_process_step";
        [JsonProperty(PropertyName = CurrentProcessStepPropertyName)]
        public WebId CurrentProcessStep { get; set; }
        
        public const string TitlesPropertyName = "titles";
        [JsonProperty(PropertyName = TitlesPropertyName)]
        public string [] Titles { get; set; }

        public const string CompletionsPropertyName = "completions";
        [JsonProperty(PropertyName = CompletionsPropertyName)]
        public DateTime?[] Completions { get; set; }

        public const string InvalidationsPropertyName = "invalidations";
        [JsonProperty(PropertyName = InvalidationsPropertyName)]
        public DateTime?[] Invalidations { get; set; }
        #endregion

        #region resource accumulation
        public const string ResourcesDisplayedPropertyName = "resources_displayed";
        [JsonProperty(PropertyName = ResourcesDisplayedPropertyName)]
        public string[] ResourcesDisplayed { get; set; }
        
        public const string ResourcesProvidedPropertyName = "resources_provided";
        [JsonProperty(PropertyName = ResourcesProvidedPropertyName)]
        public ConfirmableResource[] ResourcesProvided { get; set; }

        public class ConfirmableResource
        {
            public const string KeyPropertyName = "key";
            [JsonProperty(PropertyName = KeyPropertyName)]
            public string Key { get; set; }
            
            [JsonProperty(PropertyName = ProcessStep.ResourcePropertyName)]
            public WebId Resource { get; set; }

            public const string TypePropertyName = "type";
            [JsonProperty(PropertyName = TypePropertyName)]
            public string Type { get; set; }
        }
        #endregion
        
        #region Available actions
        public const string NextStagesPropertyName = "next_stages";
        [JsonProperty(PropertyName = NextStagesPropertyName)]
        public WebId[] NextStages { get; set; }

        public const string EditablePropertyName = "editable";
        [JsonProperty(PropertyName = EditablePropertyName)]
        public bool Editable { get; set; }

        public const string CompletablePropertyName = "completable";
        [JsonProperty(PropertyName = CompletablePropertyName)]
        public bool Completable { get; set; }
        #endregion


        #region GET

        [EastFive.Api.HttpGet]
        public static async Task<HttpResponseMessage> FindByResourceAsync(
                [EastFive.Api.Required(Name = Resources.ProcessResourceView.ActorPropertyName)]Guid actorId,
                [EastFive.Api.Required(Name = Resources.ProcessStageType.ResourceTypePropertyName)]Type resourceType,
                EastFive.Api.Controllers.Security security, Application application, UrlHelper url,
            [Display(Name = "Found")]MultipartAcceptArrayResponseAsync onMultipart,
            ReferencedDocumentNotFoundResponse onResourceNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return await await ProcessResourceViews.FindByResourceAsync(actorId, resourceType,
                    security,
                (views) => onMultipart(views.Select(ps => GetResource(ps, application, url))),
                () => onResourceNotFound().ToTask(),
                () => onUnauthorized().ToTask());
        }

        internal static Resources.ProcessResourceView GetResource(Azure.ProcessResourceView view, Application application, UrlHelper url)
        {
            return new Resources.ProcessResourceView
            {
                Id = url.GetWebId<ProcessResourceView>(view.processViewId),
                Actor = application.GetActorLink(view.actorId, url),
                Resource = application.GetResourceLink(view.resourceType, view.resourceId, url),
                ResourceType = application.GetResourceMime(view.resourceType),

                CurrentProcessStep = url.GetWebId<Controllers.ProcessStepController>(view.currentProcessStepId),
                Titles = view.titles,
                Completions = view.completions,
                Invalidations = view.invalidations,

                ResourcesDisplayed = view.displayResources,
                ResourcesProvided = view.resourcesProvided
                    .Select(
                        resourceProvided => new Resources.ProcessResourceView.ConfirmableResource
                        {
                            Key = resourceProvided.key,
                            Resource = application.GetResourceLink(resourceProvided.type, resourceProvided.resourceId, url),
                            Type = application.GetResourceMime(resourceProvided.type),
                        })
                    .ToArray(),

                NextStages = view.nextStages
                    .Select(nextStageId => url.GetWebId<ProcessStage>(nextStageId.processStageId))
                    .ToArray(),
                Editable = view.editable,
                Completable = view.completable,
            };
        }

        #endregion


        [EastFive.Api.HttpOptions(MatchAllBodyParameters = false)]
        public static HttpResponseMessage Options(HttpRequestMessage request, Application application, UrlHelper url,
            ContentResponse onOption)
        {
            return onOption(
                GetResource(
                    new Azure.ProcessResourceView()
                    {
                        processViewId = Guid.NewGuid(),
                        actorId = Guid.NewGuid(),
                        resourceId = Guid.NewGuid(),
                        resourceType = typeof(Process),

                        currentProcessStepId = Guid.NewGuid(),
                        titles = new string[] { "Step 1", "Step 2", "Step 1", "Step 3" },
                        completions = new DateTime?[]
                            {
                                DateTime.UtcNow - TimeSpan.FromDays(4.0),
                                default(DateTime?),
                                DateTime.UtcNow - TimeSpan.FromDays(2.0),
                                DateTime.UtcNow - TimeSpan.FromDays(1.0),
                            },
                        invalidations = new DateTime?[]
                            {
                                default(DateTime?),
                                DateTime.UtcNow - TimeSpan.FromDays(3.0),
                                default(DateTime?),
                                default(DateTime?),
                            },

                        displayResources = new string[] { "process", "process" },
                        resourcesProvided = new Process.ProcessStageResource[]
                        {
                            new Process.ProcessStageResource
                            {

                            },
                            new Process.ProcessStageResource
                            {

                            },
                        },

                        nextStages = new Azure.ProcessStage[]
                        {
                            new Azure.ProcessStage
                            {
                                processStageId = Guid.NewGuid(),
                            }
                        },
                        editable = true,
                        completable = true,
                    },
                    application,
                    url));
        }

    }
}
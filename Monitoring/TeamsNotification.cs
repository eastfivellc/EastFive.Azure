using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Linq;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Api.Azure;
using EastFive.Azure.Auth;
using EastFive.Azure.Media;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Collections.Generic;
using EastFive.Linq.Async;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Serialization;
using EastFive.Text;
using EastFive.Persistence.Azure;
using EastFive.Analytics;
using EastFive.Azure;
using EastFive.Azure.Persistence;
using EastFive.Api.Meta.Flows;

namespace EastFive.Azure.Monitoring
{
    [FunctionViewController(
        Namespace = "admin",
        Route = "TeamsNotification")]
    [StorageTable]
    [DisplayEntryPoint]
    public class TeamsNotification : IReferenceable
    {
        #region Properties

        #region ID / Persistence

        [JsonIgnore]
        public Guid id => teamsNotificationRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [RowKeyPrefix(Characters = 1)]
        [ResourceIdentifier]
        public IRef<TeamsNotification> teamsNotificationRef;

        [ETag]
        [JsonIgnore]
        public string eTag;

        #endregion

        public const string RouteFiltersPropertyName = "route_filters";
        [ApiProperty(PropertyName = RouteFiltersPropertyName)]
        [JsonProperty(PropertyName = RouteFiltersPropertyName)]
        [Storage]
        public string[] routeFilters;

        public const string ResponseFiltersPropertyName = "response_filters";
        [ApiProperty(PropertyName = ResponseFiltersPropertyName)]
        [JsonProperty(PropertyName = ResponseFiltersPropertyName)]
        [Storage]
        public int[] responseFilters;

        public const string MethodFiltersPropertyName = "method_filters";
        [ApiProperty(PropertyName = MethodFiltersPropertyName)]
        [JsonProperty(PropertyName = MethodFiltersPropertyName)]
        [Storage]
        public string[] methodFilters;

        public const string CollectionPropertyName = "collection";
        [ApiProperty(PropertyName = CollectionPropertyName)]
        [JsonProperty(PropertyName = CollectionPropertyName)]
        [Storage]
        [ResourceTitle]
        public string folder;

        #endregion

        #region Http Methods

        #region GET

        [WorkflowStep(
            FlowName = Workflows.MonitoringFlow.FlowName,
            Version = Workflows.MonitoringFlow.Version,
            Step = 1.0,
            StepName = "List Notifications")]
        [SuperAdminClaim]
        [HttpGet]
        public static IHttpResponse GetAll(
            RequestMessage<TeamsNotification> teamsNotifications,
            MultipartAsyncResponse<TeamsNotification> onFound)
        {
            return teamsNotifications
                .StorageGet()
                .Select(
                    n =>
                    {
                        return n;
                    })
                .HttpResponse(onFound);
        }

        #endregion

        #region POST

        [WorkflowStep(
            FlowName = Workflows.MonitoringFlow.FlowName,
            Version = Workflows.MonitoringFlow.Version,
            Step = 2.0,
            StepName = "Create Notification")]
        [HttpPost]
        [SuperAdminClaim]
        public static Task<IHttpResponse> CreateAsync(
                [EastFive.Api.Meta.Flows.WorkflowNewId]
                [WorkflowVariable(Workflows.MonitoringFlow.Variables.CreatedNotification, IdPropertyName)]
                [UpdateId]
                IRef<TeamsNotification> teamsNotificationRef,

                [PropertyOptional(Name = RouteFiltersPropertyName)]
                [WorkflowArrayObjectParameter(Value0 = "/api/*" )]
                string routeFilters,

                [PropertyOptional(Name = CollectionPropertyName)]
                [WorkflowParameter(Value = "Collection1", Disabled = true)]
                string collection,

                [Resource] TeamsNotification storyBoard,

            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists,
            UnauthorizedResponse onUnauthorized)
        {
            return storyBoard.HttpPostAsync(
                onCreated,
                onAlreadyExists);
        }

        #endregion

        #region PATCH

        [WorkflowStep(
            FlowName = Workflows.MonitoringFlow.FlowName,
            Version = Workflows.MonitoringFlow.Version,
            Step = 3.0,
            StepName = "Modify Notification")]
        [HttpPatch]
        [SuperAdminClaim]
        public static Task<IHttpResponse> UpdateAsync(
                [WorkflowParameter(Value = "{{TeamsNotification}}")]
                [UpdateId] IRef<TeamsNotification> teamsNotificationRef,
                [MutateResource]MutateResource<TeamsNotification> updated,
            ContentTypeResponse<TeamsNotification> onUpdated,
            NotFoundResponse onNotFound,
            UnauthorizedResponse onUnauthorized)
        {
            return teamsNotificationRef
                .HttpPatchAsync(updated,
                    onUpdated,
                    onNotFound);
        }

        #endregion

        #region DELETE

        [WorkflowStep(
            FlowName = Workflows.MonitoringFlow.FlowName,
            Version = Workflows.MonitoringFlow.Version,
            Step = 6.0,
            StepName = "Delete Notification")]
        [SuperAdminClaim]
        [HttpDelete]
        public static Task<IHttpResponse> DeleteByIdAsync(
                [WorkflowParameter(Value = "{{TeamsNotification_}}")]
                [UpdateId(Name = IdPropertyName)] IRef<TeamsNotification> teamsNotificationId,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound)
        {
            return teamsNotificationId.HttpDeleteAsync(
                onDeleted,
                onNotFound);
        }

        [WorkflowStep(
            FlowName = Workflows.MonitoringFlow.FlowName,
            Version = Workflows.MonitoringFlow.Version,
            Step = 7.0,
            StepName = "Clear Notifications")]
        [HttpDelete]
        [SuperAdminClaim]
        public static IHttpResponse ClearAsync(
                [WorkflowParameter(Value = "true")]
                [QueryParameter(Name = "clear")]
                bool clear,

                RequestMessage<TeamsNotification> teamsNotifications,
                EastFive.Api.Security security,
            MultipartAsyncResponse<TeamsNotification> onDeleted)
        {
            return teamsNotifications
                .StorageGet()
                .StorageDeleteBatch(
                    tr =>
                    {
                        return (tr.Result as IWrapTableEntity<TeamsNotification>).Entity;
                    })
                .HttpResponse(onDeleted);
        }

        #endregion

        #region Action

        [WorkflowStep(
            FlowName = Workflows.MonitoringFlow.FlowName,
            Version = Workflows.MonitoringFlow.Version,
            Step = 4.0,
            StepName = "Load and Activate Notifications")]
        [HttpAction("Load")]
        [SuperAdminClaim]
        public static async Task<IHttpResponse> LoadAsync(
            RequestMessage<TeamsNotification> teamsNotificationsStorage,
            ContentTypeResponse<TeamsNotification[]> onFound)
        {
            teamsNotifications = await teamsNotificationsStorage
                .StorageGet()
                .ToArrayAsync();

            return onFound(teamsNotifications);
        }

        [WorkflowStep(
            FlowName = Workflows.MonitoringFlow.FlowName,
            Version = Workflows.MonitoringFlow.Version,
            Step = 5.0,
            StepName = "List Active Notifications")]
        [HttpAction("Active")]
        [SuperAdminClaim]
        public static IHttpResponse Active(
            ContentTypeResponse<TeamsNotification[]> onFound)
        {
            return onFound(teamsNotifications);
        }

        #endregion

        #endregion

        private static TeamsNotification[] teamsNotifications;

        internal static bool IsMatch(IHttpRequest request, IHttpResponse response, out string collectionFolder)
        {
            bool matched;
            (matched, collectionFolder) = teamsNotifications
                .NullToEmpty()
                .Select(
                    tn =>
                    {
                        if (tn.routeFilters.Any())
                            if (!IsRouteMatch())
                                return (false, default(string));

                        if(tn.methodFilters.Any())
                            if (!IsMethodMatch())
                                return (false, default(string));

                        if (tn.responseFilters.Any())
                            if(!IsResponseMatch())
                                return (false, default(string));

                        return (true, tn.folder);

                        bool IsRouteMatch() => tn.routeFilters
                            .TryWhere(
                                (string rf, out (string, string)[] matches) =>
                                    request.RequestUri.PathAndQuery.TryMatchRegex(rf, out matches,
                                        optionsMaybe: System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            .Any();

                        bool IsMethodMatch() => tn.methodFilters
                            .Where(mf =>request.Method.Method.Contains(
                                mf, StringComparison.OrdinalIgnoreCase))
                           .Any();

                        bool IsResponseMatch() => tn.responseFilters
                            .Where(rfTpl => ((int)response.StatusCode) == rfTpl)
                            .Any();

                    })
                .Where(tpl => tpl.Item1)
                .First(
                    (tpl, next) => tpl,
                    () => (false, default(string)));
            return matched;
        }
    }
}


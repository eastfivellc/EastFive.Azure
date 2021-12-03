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
        public Guid? Collection;

        #endregion

        #region Http Methods

        #region GET

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

        [HttpPost]
        public static Task<IHttpResponse> CreateAsync(
                [UpdateId] IRef<TeamsNotification> teamsNotificationRef,
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

        [HttpPatch]
        public static Task<IHttpResponse> UpdateAsync(
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

        [HttpDelete]
        public static Task<IHttpResponse> DeleteByIdAsync(
                [UpdateId(Name = IdPropertyName)] IRef<TeamsNotification> teamsNotificationId,
            NoContentResponse onDeleted,
            NotFoundResponse onNotFound)
        {
            return teamsNotificationId.HttpDeleteAsync(
                onDeleted,
                onNotFound);
        }

        [HttpDelete]
        public static IHttpResponse ClearAsync(
                [QueryParameter(Name = "clear")] bool clear,
                RequestMessage<TeamsNotification> teamsNotifications,
                EastFive.Api.Security security,
            MultipartAsyncResponse<TeamsNotification> onDeleted)
        {
            return teamsNotifications
                .StorageGet()
                .StorageDeleteBatch(
                    tr =>
                    {
                        return (TeamsNotification)tr.Result;
                    })
                .HttpResponse(onDeleted);
        }

        #endregion

        #region Action

        [HttpAction("Load")]
        public static async Task<IHttpResponse> LoadAsync(
            RequestMessage<TeamsNotification> teamsNotificationsStorage,
            ContentTypeResponse<TeamsNotification[]> onFound)
        {
            teamsNotifications = await teamsNotificationsStorage
                .StorageGet()
                .ToArrayAsync();

            return onFound(teamsNotifications);
        }

        [HttpAction("Active")]
        public static IHttpResponse Active(
            ContentTypeResponse<TeamsNotification[]> onFound)
        {
            return onFound(teamsNotifications);
        }

        #endregion

        #endregion

        private static TeamsNotification[] teamsNotifications;

        internal static bool IsMatch(IHttpRequest request, IHttpResponse response, out Guid? collectionIdMaybe)
        {
            bool matched;
            (matched, collectionIdMaybe) = teamsNotifications
                .NullToEmpty()
                .Select(
                    tn =>
                    {
                        if (tn.routeFilters.Any())
                            if (!IsRouteMatch())
                                return (false, default(Guid?));

                        if(tn.methodFilters.Any())
                            if (!IsMethodMatch())
                                return (false, default(Guid?));

                        if (tn.responseFilters.Any())
                            if(!IsResponseMatch())
                                return (false, default(Guid?));

                        return (false, tn.Collection);

                        bool IsRouteMatch() => tn.routeFilters
                           .TryWhere(
                               (string rf, out (string, string)[] matches) =>
                                   request.RequestUri.PathAndQuery.TryMatchRegex(rf, out matches))
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
                    () => (false, default(Guid?)));
            return matched;
        }
    }
}


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
        public string[] responseFilters;

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

        #region PUT

        [HttpPut]
        public static Task<IHttpResponse> UpdateAsync(
                [UpdateId] IRef<TeamsNotification> teamsNotificationRef,
                MutateResource<TeamsNotification> updated,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists,
            UnauthorizedResponse onUnauthorized)
        {
            return teamsNotificationRef
                .HttpPostAsync(
                    onCreated,
                    onAlreadyExists);
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
                .StorageDeleteBatch(
                    tr => (TeamsNotification)tr.Result)
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

        #endregion

        #endregion

        private static TeamsNotification[] teamsNotifications;

        internal static bool IsMatch(IHttpRequest request)
        {
            return teamsNotifications
                .NullToEmpty()
                .Where(
                    tn =>
                    {
                        var routeMatch = tn.routeFilters
                            .TryWhere(
                                (string rf, out (string, string)[] matches) =>
                                    request.RequestUri.PathAndQuery.TryMatchRegex(rf, out matches))
                            .Any();
                        if (routeMatch)
                            return true;

                        return false;
                    })
                .Any();
        }
    }
}


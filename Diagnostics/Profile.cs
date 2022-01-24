using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EastFive.Api.Serialization.Json;

using EastFive;
using EastFive.Azure;
using EastFive.Persistence;
using EastFive.Azure.Persistence;
using EastFive.Azure.StorageTables;
using EastFive.Persistence.Azure;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;

namespace EastFive.Api.Diagnositcs
{
	[FunctionViewController(Route = "StoryBoardMedia")]
	[StorageTable]
	[CastSerialization]
	public class Profile : IReferenceable
	{
        #region Properties

        #region Base

        public Guid id => profileRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [RowKey]
        [StandardParititionKey]
        [ResourceIdentifier]
        public IRef<Profile> profileRef;

        [ETag]
        public string eTag;

        #endregion

        public const string EventsPropertyName = "events";
        [ApiProperty(PropertyName = EventsPropertyName)]
        [Storage]
        public IDictionary<TimeSpan, string> Events;

        public const string WhenPropertyName = "when";
        [ApiProperty(PropertyName = WhenPropertyName)]
        [Storage]
        [ScopeDateTime("partitionScope", SpanUnits = TimeSpanUnits.days)]
        [DateTimeLookup(Partition = TimeSpanUnits.days, Row = TimeSpanUnits.hours)]
        public DateTime when;

        public const string UrlPropertyName = "url";
        [ApiProperty(PropertyName = UrlPropertyName)]
        [Storage]
        public Uri url;

        public const string ResourcePropertyName = "resource";
        [ApiProperty(PropertyName = ResourcePropertyName)]
        [Storage]
        [ScopedLookup("rowScope", "partitionScope")]
        [ScopeString("rowScope")]
        public string resource;

        #endregion

        #region HTTP Methods

        #region GET

        [HttpGet]
        public static Task<IHttpResponse> QueryByIdAsync(
                [QueryId(Name = IdPropertyName)] IRef<Profile> profileRef,
            ContentTypeResponse<Profile> onFound,
            NotFoundResponse onNotFound)
        {
            return profileRef.HttpGetAsync(onFound, onNotFound);
        }

        [HttpGet]
        public static IHttpResponse QueryByResourceAsync(
                [QueryParameter(Name = ResourcePropertyName)] string resource,
                [QueryParameter(Name = WhenPropertyName)] DateTime when,
            MultipartAsyncResponse<Profile> onFound)
        {
            return resource
                .StorageGetBy(
                    (Profile profile) => profile.resource,
                    profile => profile.when == when)
                .HttpResponse(onFound);
        }

        #endregion

        #endregion
    }
}


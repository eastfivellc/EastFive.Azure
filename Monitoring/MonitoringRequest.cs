using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Linq.Expressions;
using System.Linq;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Azure.Persistence.Blobs;

namespace EastFive.Api.Azure.Monitoring
{
    [FunctionViewController(
        Namespace = "meta",
        Route = "MonitoringRequest",
        ContentType = "x-application/meta-montioring-request",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    public class MonitoringRequest : IReferenceable
    {
        [JsonIgnore]
        public Guid id => monitoringRequestRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        public IRef<MonitoringRequest> monitoringRequestRef;

        [PartitionByDay]
        [Storage]
        public DateTime when;

        [ETag]
        [JsonIgnore]
        public string eTag;

        public const string UrlPropertyName = "url";
        [ApiProperty(PropertyName = UrlPropertyName)]
        [JsonProperty(PropertyName = UrlPropertyName)]
        [Storage]
        public Uri url;

        public const string MethodPropertyName = "method";
        [ApiProperty(PropertyName = MethodPropertyName)]
        [JsonProperty(PropertyName = MethodPropertyName)]
        [Storage]
        public string method;

        [Storage]
        public Header[] headers;

        public struct Header
        {
            [Storage]
            public string key;

            [Storage]
            public string value;

            [Storage]
            public string type;
        }

        [Storage]
        public IBlobRef body;

        [Storage]
        public FormData[] formData;

        [Storage]
        public FormFileData[] formDataFiles;

        public struct FormData
        {
            [Storage]
            public string key;

            [Storage]
            public string [] contents;
        }

        public struct FormFileData
        {
            [Storage]
            public string name;
            [Storage]
            public string fileName;
            [Storage]
            public string contentDisposition;
            [Storage]
            public string contentType;

            [Storage]
            public IBlobRef contents;

            [Storage]
            public Header[] headers;

            [Storage]
            public long length;
        }

        public static async Task<MonitoringRequest> CreateAsync(IHttpRequest request)
        {
            var doc = new MonitoringRequest();
            doc.monitoringRequestRef = Ref<MonitoringRequest>.NewRef();
            doc.when = DateTime.UtcNow;
            doc.url = request.RequestUri;
            doc.method = request.Method.Method;
            doc.headers = request.Headers
                .Where(kvp => kvp.Value.AnyNullSafe())
                .Select(
                    kvp => new Header()
                    {
                        key = kvp.Key,
                        value = kvp.Value.First(),
                    })
                .ToArray();

            if (request.HasFormContentType)
            {
                doc.formData = request.Form
                    .Select(
                        formInfo =>
                        {
                            return new FormData
                            {
                                key = formInfo.Key,
                                contents = formInfo.Value.ToArray(),
                            };
                        })
                    .ToArray();

                doc.formDataFiles = await request.Form.Files
                    .Select(
                        async file =>
                        {
                            var data = await file.OpenReadStream().ToBytesAsync();
                            var contentRef = await data.CreateBlobRefAsync(
                                (FormFileData ffd) => ffd.contents,
                                file.ContentType);
                            return new FormFileData
                            {
                                contents = contentRef,
                                name = file.Name,
                                fileName = file.FileName,
                                contentDisposition = file.ContentDisposition,
                                contentType = file.ContentType,
                                headers = file.Headers
                                    .Select(hdr => new Header() { key = hdr.Key, value = hdr.Value })
                                    .ToArray(),
                                length = file.Length,
                            };
                        })
                    .AsyncEnumerable()
                    .ToArrayAsync();
            }
            else
            {
                var bytes = await request.ReadContentAsync();
                doc.body = await bytes.CreateBlobRefAsync(
                    (MonitoringRequest mr) => mr.body,
                    contentType: request.GetMediaType());
            }

            return await doc.StorageCreateAsync((discard) => doc);
        }

        #region HttpMethods

        #region GET

        [HttpGet]
        public static Task<IHttpResponse> GetByIdAsync(
                [QueryId]IRef<MonitoringRequest> monitoringRequestRef,
                Security security,
            ContentTypeResponse<MonitoringRequest> onContent,
            UnauthorizedResponse onBadToken)
        {
            return onBadToken().AsTask();
        }

        #endregion

        #endregion

    }
}

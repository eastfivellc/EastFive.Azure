using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Linq;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Text;
using EastFive.Serialization;
using EastFive.Linq;
using EastFive.Linq.Expressions;
using EastFive.Linq.Async;
using EastFive.Api.Meta.Postman.Resources.Collection;
using EastFive.Reflection;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence;

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
        #region Properties

        #region Base

        [JsonIgnore]
        public Guid id => monitoringRequestRef.id;

        public const string IdPropertyName = "id";
        [ApiProperty(PropertyName = IdPropertyName)]
        [JsonProperty(PropertyName = IdPropertyName)]
        [RowKey]
        public IRef<MonitoringRequest> monitoringRequestRef;

        public const string WhenPropertyName = "when";
        [ApiProperty(PropertyName = WhenPropertyName)]
        [JsonProperty(PropertyName = WhenPropertyName)]
        [PartitionByDay]
        [Storage]
        public DateTime when;

        [ETag]
        [JsonIgnore]
        public string eTag;

        #endregion

        public const string CollectionPropertyName = "collection";
        [ApiProperty(PropertyName = CollectionPropertyName)]
        [JsonProperty(PropertyName = CollectionPropertyName)]
        [IdHashXX32Lookup]
        [Storage]
        public Guid? Collection;

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

        #endregion

        #region HttpMethods

        #region GET

        [HttpGet]
        public static Task<IHttpResponse> GetByIdAsync(
                [QueryId]IRef<MonitoringRequest> monitoringRequestRef,
                [QueryParameter(Name = WhenPropertyName)]DateTime when,
                // Security security,
            ContentTypeResponse<MonitoringRequest> onContent,
            NotFoundResponse onNotFound)
        {
            return monitoringRequestRef
                .StorageGetAsync(
                    additionalProperties: (query) => query.Where(item => item.when == when),
                    onFound:mr => onContent(mr),
                    onDoesNotExists:() => onNotFound());
        }

        #endregion

        #region ACTION

        [HttpAction("Postman")]
        public static async Task<IHttpResponse> SendToPostman(
                [QueryId] IRef<MonitoringRequest> monitoringRequestRef,
                [QueryParameter(Name = WhenPropertyName)] DateTime when,
                [QueryParameter(Name = CollectionPropertyName)] IRef<Collection> collectionRef,
            // Security security,
            ContentTypeResponse<Meta.Postman.Resources.Collection.Item> onContent,
            NotFoundResponse onNotFound,
            GeneralFailureResponse onFailure)
        {
            return await await monitoringRequestRef
                .StorageGetAsync(
                    additionalProperties: (query) => query.Where(item => item.when == when),
                    onFound: async mr =>
                    {
                        return await PostMonitoringRequestAsync(mr, collectionRef,
                            (postmanItem) =>
                            {
                                return onContent(postmanItem);
                            },
                            (why) => onFailure(why));
                    },
                    onDoesNotExists: () => onNotFound().AsTask());
        }

        #endregion

        #endregion

        public static async Task<MonitoringRequest> CreateAsync(IHttpRequest request, Guid? collectionIdMaybe)
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
            doc.Collection = collectionIdMaybe;

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

        public static async Task<TResult> PostMonitoringRequestAsync<TResult>(
                MonitoringRequest itemToCreateOrUpdate, IRef<Collection> collectionRef,
            Func<Api.Meta.Postman.Resources.Collection.Item, TResult> onCreatedOrUpdated,
            Func<string, TResult> onFailure)
        {
            var postmanItem = await itemToCreateOrUpdate.ConvertToPostmanItemAsync();
            return await await EastFive.Api.Meta.Postman.Resources.Collection.Collection.GetAsync(collectionRef,
                collection =>
                {
                    var collectionWithItem = new Collection
                    {
                        info = collection.info,
                        item = collection.item.Append(postmanItem).ToArray(),
                        variable = collection.variable,
                    };
                    return collectionWithItem.UpdateAsync<TResult>(
                        (updatedCollection) =>
                        {
                            return onCreatedOrUpdated(postmanItem);
                        });
                },
                () =>
                {
                    var collection = new Collection()
                    {
                        info = new Info
                        {
                            name = $"MonitoringRequest - {itemToCreateOrUpdate.when}",
                            schema = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
                            // _postman_id = collectionRef.id,
                        },
                        variable = new Variable[]
                        {
                            new Variable
                            {
                                id = "baseUrl",
                                key = "baseUrl",
                                value = itemToCreateOrUpdate.url.BaseUri().OriginalString,
                                type = "string",
                            }
                        },
                        item = new Item[] { postmanItem }
                    };
                    return collection.CreateAsync(
                        (createdCollection) =>
                        {
                            return onCreatedOrUpdated(postmanItem);
                        });
                });
            
        }

        private async Task<Item> ConvertToPostmanItemAsync()
        {
            var item = new Item
            {
                name = this.url.OriginalString,
                request = new Request
                {
                    url = new Url
                    {
                        host = new string[] { "{localUrl}" },
                        path = this.url.ParsePath(),
                        raw = this.url.OriginalString,
                        query = this.url.ParseQuery()
                            .Select(
                                kvp => new QueryItem
                                {
                                    key = kvp.Key,
                                    value = kvp.Value
                                })
                            .ToArray(),
                    },
                    description = $"{this.method}:{this.url}",
                    header = this.headers
                        .Select(
                            header => new Meta.Postman.Resources.Collection.Header
                            {
                                key = header.key,
                                type = header.type,
                                value = header.value,
                            })
                        .ToArray(),
                    method = this.method,
                    body = await GetPostmanBodyAsync(),
                }
            };
            return item;
        }

        async Task<Body> GetPostmanBodyAsync()
        {
            return await await this.body.LoadAsync(
                (id, data, contentType, fileName) =>
                {
                    return new Body
                    {
                        mode = "raw",
                        raw = data.GetString(System.Text.Encoding.UTF8),
                    }.AsTask();
                },
                onNotFound: async () =>
                {
                     var formDataBody = new Body()
                     {
                         mode = "formdata",
                     };

                    var postFormData = this.formData
                        .Where(fd => fd.contents.IsSingle())
                        .Select(
                            fd =>
                            {
                               return new Meta.Postman.Resources.Collection.FormData
                               {
                                   key = fd.key,
                                   value = fd.contents.First(),
                               };
                            })
                        .ToArray();

                    var postmanFormDataFiles = await this.formDataFiles
                        .Select(
                            fd =>
                            {
                                return fd.contents.LoadAsync(
                                    (id, data, contentType, fileName) =>
                                    {
                                        return (true, new Meta.Postman.Resources.Collection.FormData
                                        {
                                            key = fd.name,
                                            value = fd.contentDisposition,
                                            src = fileName,
                                        });
                                    },
                                    () => (false, default(Meta.Postman.Resources.Collection.FormData)));
                            })
                        .AsyncEnumerable()
                        .SelectWhere()
                        .ToArrayAsync();

                    formDataBody.formdata = postFormData
                        .Concat(postmanFormDataFiles)
                        .ToArray();

                    return formDataBody;
                });
        }
    }
}

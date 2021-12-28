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
using EastFive.Azure.Persistence.Blobs;
using EastFive.Web.Configuration;
using System.Net.Http.Headers;

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

        public const string TitlePropertyName = "title";
        [ApiProperty(PropertyName = TitlePropertyName)]
        [JsonProperty(PropertyName = TitlePropertyName)]
        [Storage]
        public string title;

        public const string FolderNamePropertyName = "folder_name";
        [ApiProperty(PropertyName = FolderNamePropertyName)]
        [JsonProperty(PropertyName = FolderNamePropertyName)]
        [StringLookupHashXX32(IgnoreNullWhiteSpace = true)]
        [Storage]
        public string folderName;

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

        public const string PostmanAction = "Postman";
        [HttpAction(PostmanAction)]
        public static async Task<IHttpResponse> SendToPostman(
                [QueryId] IRef<MonitoringRequest> monitoringRequestRef,
                [QueryParameter(Name = WhenPropertyName)] DateTime when,
            Security security,
            ContentTypeResponse<Meta.Postman.Resources.Collection.Item> onContent,
            NotFoundResponse onNotFound,
            GeneralFailureResponse onFailure)
        {
            return await await monitoringRequestRef
                .StorageGetAsync(
                    additionalProperties: (query) => query.Where(item => item.when == when),
                    onFound: async mr =>
                    {
                        return await PostMonitoringRequestAsync(mr, mr.folderName,
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

        public static async Task<MonitoringRequest> CreateAsync(
            Type controllerType, IInvokeResource resourceInvoker,
            IApplication httpApp, IHttpRequest request, string folderName)
        {
            var doc = new MonitoringRequest();
            doc.title = $"{request.Method.Method} {resourceInvoker.Namespace} {resourceInvoker.Route}";
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
            doc.folderName = folderName;

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
                MonitoringRequest itemToCreateOrUpdate, string collectionFolder,
            Func<Api.Meta.Postman.Resources.Collection.Item, TResult> onCreatedOrUpdated,
            Func<string, TResult> onFailure)
        {
            var postmanItem = await itemToCreateOrUpdate.ConvertToPostmanItemAsync();
            return await EastFive.Api.AppSettings.Postman.MonitoringCollectionId.ConfigurationString(
                async collectionId =>
                {
                    return await await EastFive.Api.Meta.Postman.Resources.Collection.Collection.GetAsync(collectionId,
                        collection =>
                        {
                            var collectionWithItem = collectionFolder.HasBlackSpace() ?
                                MutateCollectionWithFolderName(collectionFolder)
                                :
                                MutateCollection();
                            return collectionWithItem.UpdateAsync<TResult>(
                                (updatedCollection) =>
                                {
                                    return onCreatedOrUpdated(postmanItem);
                                },
                                onFailure:onFailure);

                            Collection MutateCollection()
                            {
                                return new Collection
                                {
                                    info = collection.info,
                                    item = collection.item.Append(postmanItem).ToArray(),
                                    variable = collection.variable,
                                };
                            }

                            Collection MutateCollectionWithFolderName(string folderName)
                            {
                                return collection.item
                                    .NullToEmpty()
                                    .Where(item => collectionFolder.Equals(item.name))
                                    .First(
                                        (folderItem, next) =>
                                        {
                                            folderItem.item = folderItem.item
                                                .Where(item => !postmanItem.name.Equals(item.name, StringComparison.CurrentCultureIgnoreCase))
                                                .Append(postmanItem)
                                                .ToArray();

                                            var collectionItems = collection.item
                                                .Where(item => !collectionFolder.Equals(item.name, StringComparison.CurrentCultureIgnoreCase))
                                                .Append(folderItem)
                                                .ToArray();
                                            return new Collection
                                            {
                                                info = collection.info,
                                                item = collectionItems,
                                                variable = collection.variable,
                                            };
                                        },
                                        () =>
                                        {
                                            var folderItem = new Item
                                            {
                                                name = folderName,
                                                item = postmanItem.AsArray(),
                                            };
                                            return new Collection
                                            {
                                                info = collection.info,
                                                item = collection.item
                                                    .NullToEmpty()
                                                    .Append(folderItem)
                                                    .ToArray(),
                                                variable = collection.variable,
                                            };
                                        });
                            }
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
                                        id = Url.VariableHostName,
                                        key = Url.VariableHostName,
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
                },
                onUnspecified: onFailure.AsAsyncFunc());

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
                        host = new string[] { Url.VariableHostName },
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
                                disabled = Meta.Postman.Resources.Collection.Header.IsGeneratedHeader(header.key),
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
                (id, data, mediaType, contentDisposition) =>
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
                                    (id, data, contentType, disposition) =>
                                    {
                                        var fileName = ContentDispositionHeaderValue.TryParse(fd.contentDisposition,
                                                out ContentDispositionHeaderValue disHV) ?
                                            disHV.FileName
                                            :
                                            disposition.FileName;
                                        return (true, new Meta.Postman.Resources.Collection.FormData
                                        {
                                            key = fd.name,
                                            value = fd.contentDisposition,
                                            src = disposition.FileName,
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

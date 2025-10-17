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
using EastFive.Azure.Monitoring;
using EastFive.Api.Meta.Postman.Resources;
using EastFive.Api.Meta.Flows;
using EastFive.Azure.Auth;
using EastFive.Api.Serialization.Json;
using System.IO;
using EastFive.Web;

namespace EastFive.Api.Azure.Monitoring
{
    [FunctionViewController(
        Namespace = "meta",
        Route = "MonitoringRequest",
        ContentType = "x-application/meta-montioring-request",
        ContentTypeVersion = "0.1")]
    [StorageTable]
    [CastSerialization]
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

        public const string NameSpacePropertyName = "ns";
        [ApiProperty(PropertyName = NameSpacePropertyName)]
        [JsonProperty(PropertyName = NameSpacePropertyName)]
        [Storage]
        public string ns;

        public const string RoutePropertyName = "route";
        [ApiProperty(PropertyName = RoutePropertyName)]
        [JsonProperty(PropertyName = RoutePropertyName)]
        [Storage]
        public string route;

        public const string ActionPropertyName = "action";
        [ApiProperty(PropertyName = ActionPropertyName)]
        [JsonProperty(PropertyName = ActionPropertyName)]
        [Storage]
        public string action;

        public const string ResourceIdPropertyName = "resource_id";
        [ApiProperty(PropertyName = ResourceIdPropertyName)]
        [JsonProperty(PropertyName = ResourceIdPropertyName)]
        [Storage]
        public Guid resourceId;

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

        [ApiProperty]
        [JsonProperty]
        [Storage]
        public int status;

        [ApiProperty]
        [JsonProperty]
        [Storage]
        public string reason;

        #endregion

        #region HttpMethods

        #region OPTIONS

        [WorkflowStep(
            FlowName = EastFive.Azure.Workflows.MonitoringFlow.FlowName,
            Version = EastFive.Azure.Workflows.MonitoringFlow.Version,
            Step = 10.0,
            StepName = "Available Folders")]
        [HttpOptions]
        [SuperAdminClaim]
        public static IHttpResponse PossibleFolderNames(
                [WorkflowParameter(Value = "2022-02-07")]
                [QueryParameter(Name = WhenPropertyName)]
                DateTime when,
                Security security,
            [WorkflowVariableArrayIndexedValue(Index = 0,
                VariableName = EastFive.Azure.Workflows.MonitoringFlow.Variables.FolderName)]
            MultipartAsyncResponse<string> onList)
        {
            return when
                .StorageGetBy((MonitoringRequest mr) => mr.when)
                .Select(
                    mr =>
                    {
                        return mr.folderName;
                    })
                .Distinct()
                .HttpResponse(onList);
        }

        #endregion

        #region GET

        [HttpGet]
        [SuperAdminClaim]
        public static Task<IHttpResponse> GetByIdAsync(
                [QueryId]IRef<MonitoringRequest> monitoringRequestRef,
                [QueryParameter(Name = WhenPropertyName)]DateTime when,
                Security security,
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
                    onFound: async itemToCreateOrUpdate =>
                    {
                        var postmanItem = await itemToCreateOrUpdate.ConvertToPostmanItemAsync();
                        return await Collection.CreateOrUpdateMonitoringCollectionAsync(
                            $"MonitoringRequest - {itemToCreateOrUpdate.when}", itemToCreateOrUpdate.url,
                                collectionToModify =>
                                {
                                    return collectionToModify
                                        .AppendItem(postmanItem, folderName: itemToCreateOrUpdate.folderName);
                                },
                            onCreatedOrUpdated:(discard) => onContent(postmanItem),
                            onFailure: why => onFailure(why));
                    },
                    onDoesNotExists: () => onNotFound().AsTask());
        }

        public const string PostmanCollectionAction = "PostmanFolder";
        [WorkflowStep(
            FlowName = EastFive.Azure.Workflows.MonitoringFlow.FlowName,
            Version = EastFive.Azure.Workflows.MonitoringFlow.Version,
            Step = 12.0,
            StepName = "Send Folder To Postman")]
        [HttpAction(PostmanCollectionAction)]
        public static async Task<IHttpResponse> SendCollectionToPostman(
                [WorkflowParameterFromVariable(Value =
                    EastFive.Azure.Workflows.MonitoringFlow.Variables.FolderName)]
                [QueryParameter(Name = FolderNamePropertyName)]string folderName,

                [OptionalQueryParameter(Name = WhenPropertyName)] DateTime? whenMaybe,
                IHttpRequest httpRequest,
            Security security,
            ContentTypeResponse<CollectionSummary> onPosted,
            GeneralFailureResponse onFailure)
        {
            //var postmanItems = await when
            //    .StorageGetBy((MonitoringRequest mr) => mr.when)
            //    .Where(mr => String.Equals(mr.folderName, folderName, StringComparison.CurrentCultureIgnoreCase))
            //    .Select(mr => mr.ConvertToPostmanItemAsync())
            //    .Await()
            //    .ToArrayAsync();

            var postmanItems = await folderName
                .StorageGetBy((MonitoringRequest mr) => mr.folderName)
                .Where(mr => whenMaybe.HasValue? whenMaybe.EqualToDay(mr.when) : true)
                .Select(mr => mr.ConvertToPostmanItemAsync())
                .Await()
                .ToArrayAsync();

            return await Collection.CreateOrUpdateMonitoringCollectionAsync(
                    $"MonitoringRequest - {folderName}", httpRequest.RequestUri,
                    collectionToModify =>
                    {
                        return collectionToModify
                            .AppendItems(postmanItems, folderName: folderName);
                    },
                onCreatedOrUpdated: (collection) => onPosted(collection),
                why => onFailure(why));
        }

        public const string ClearPostmanAction = "PostmanClear";
        [WorkflowStep(
            FlowName = EastFive.Azure.Workflows.MonitoringFlow.FlowName,
            Version = EastFive.Azure.Workflows.MonitoringFlow.Version,
            Step = 13.0,
            StepName = "Clear Items")]
        [HttpAction(ClearPostmanAction)]
        public static async Task<IHttpResponse> ClearPostmanAsync(
                [WorkflowParameterFromVariable(
                    Value = EastFive.Azure.Workflows.MonitoringFlow.Variables.FolderName,
                    Disabled = true)]
                [OptionalQueryParameter(Name = TeamsNotification.CollectionPropertyName)]
                string collectionFolder,
            Security security,
            ContentTypeResponse<CollectionSummary> onCleared,
            NotFoundResponse onNotFound,
            GeneralFailureResponse onFailure)
        {
            return await EastFive.Api.AppSettings.Postman.MonitoringCollectionId.ConfigurationString(
                async collectionId =>
                {
                    return await await EastFive.Api.Meta.Postman.Resources.Collection.Collection.GetAsync(collectionId,
                        collection =>
                        {
                            var collectionCleared = collectionFolder.HasBlackSpace() ?
                                MutateCollectionWithFolderName(collectionFolder)
                                :
                                MutateCollection();
                            return collectionCleared.UpdateAsync<IHttpResponse>(
                                (updatedCollection) =>
                                {
                                    return onCleared(updatedCollection);
                                },
                                onFailure:(why) => onFailure(why));

                            Collection MutateCollection()
                            {
                                return new Collection
                                {
                                    info = collection.info,
                                    item = new Item[] { },
                                    variable = collection.variable,
                                };
                            }

                            Collection MutateCollectionWithFolderName(string folderName)
                            {
                                collection.item = collection.item
                                    .NullToEmpty()
                                    .Where(item => !collectionFolder.Equals(item.name))
                                    .ToArray();
                                return collection;
                            }
                        },
                        () => onNotFound().AsTask(),
                        onFailure:why => onFailure(why).AsTask());
                },
                onUnspecified:(why) => onFailure("Postman Monitoring not setup").AsTask());
        }

        #endregion

        #endregion

        public static async Task<MonitoringRequest> CreateAsync(
            Type controllerType, IInvokeResource resourceInvoker,
            IApplication httpApp, IHttpRequest request, string folderName, IHttpResponse response)
        {
            string segmentAfter(string[] toSkip)
            {
                return request.RequestUri.Segments
                    .Where(x => x.HasBlackSpace())
                    .Where(x => !toSkip.Where(y => y.HasBlackSpace()).Contains(x, StringComparison.OrdinalIgnoreCase))
                    .First((x, next) => x, () => default(string));
            }

            var doc = new MonitoringRequest();
            doc.title = $"{request.Method.Method} {resourceInvoker.Namespace} {resourceInvoker.Route}";
            doc.monitoringRequestRef = Ref<MonitoringRequest>.NewRef();
            doc.when = DateTime.UtcNow;
            doc.url = request.RequestUri;
            doc.method = request.Method.Method;
            doc.ns = resourceInvoker.Namespace;
            doc.route = resourceInvoker.Route;
            var actionSegment = segmentAfter(new[] { resourceInvoker.Namespace, resourceInvoker.Route });
            if (actionSegment.HasBlackSpace())
            {
                if (Guid.TryParse(actionSegment, out Guid resourceId))
                    doc.resourceId = resourceId;
                else
                {
                    doc.action = actionSegment;
                    var idSegment = segmentAfter(new[] { resourceInvoker.Namespace, resourceInvoker.Route, actionSegment });
                    if (idSegment.HasBlackSpace() && Guid.TryParse(idSegment, out resourceId))
                    {
                        doc.resourceId = resourceId;
                    }
                }
            }
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
                if (bytes.Length > 0)
                {
                    doc.body = await bytes.CreateBlobRefAsync(
                        (MonitoringRequest mr) => mr.body,
                        contentType: request.GetMediaType());
                }
            }
            doc.status = (int)response.StatusCode;

            if (response.ReasonPhrase.HasBlackSpace())
                doc.reason = response.ReasonPhrase;

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
                            return collection
                                .AppendItem(postmanItem, folderName: collectionFolder)
                                .UpdateAsync<TResult>(
                                    (updatedCollection) =>
                                    {
                                        return onCreatedOrUpdated(postmanItem);
                                    },
                                    onFailure:onFailure);

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
                        },
                        onFailure:onFailure.AsAsyncFunc());
                },
                onUnspecified: onFailure.AsAsyncFunc());
        }

        private async Task<Item> ConvertToPostmanItemAsync()
        {
            var item = new Item
            {
                name = GetName(),
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
                            header =>
                            {
                                var isGenerated = Meta.Postman.Resources.Collection.Header.IsGeneratedHeader(header.key);
                                return new Meta.Postman.Resources.Collection.Header
                                {
                                    key = header.key,
                                    type = header.type,
                                    value = header.value,
                                    disabled = isGenerated,
                                };
                            })
                        .ToArray(),
                    method = this.method,
                    body = await GetPostmanBodyAsync(),
                }
            };
            return item;

            string GetName()
            {
                if (this.ns.HasBlackSpace() && this.route.HasBlackSpace())
                {
                    if ("api".Equals(this.ns, StringComparison.InvariantCultureIgnoreCase))
                        return $"{this.method} {this.route}";
                    return $"{this.method} {this.route}[{this.ns}]";
                }

                if (this.title.HasBlackSpace())
                    return this.title;

                return $"{this.method} {this.url.AbsolutePath}";
            }
        }

        Task<Body> GetPostmanBodyAsync()
        {
            return GetRawBody(
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
                                return fd.contents.LoadBytesAsync(
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

            async Task<Body> GetRawBody(
                Func<Task<Body>> onNotFound)
            {
                if (this.body.IsDefaultOrNull())
                    return await onNotFound();
                return await await this.body.LoadBytesAsync(
                    (id, data, mediaType, contentDisposition) =>
                    {
                        return new Body
                        {
                            mode = "raw",
                            raw = data.GetString(System.Text.Encoding.UTF8),
                        }.AsTask();
                    },
                    onNotFound: () =>
                    {
                        return onNotFound();
                    });
            }
        }

        public struct ActivityLog : IComparable<ActivityLog>
        {
            public string method;
            public string route;
            public int status;
            public DateTime timestamp;
            public string url;
            public Guid id;
            public Guid actorId;
            public KeyValuePair<string, string>[] formdata;
            public string blob;

            public ActivityLog(MonitoringRequest request, Guid actorId)
            {
                KeyValuePair<string,string>[] getFormData() =>
                    request.formData
                    .NullToEmpty()
                    .Select(x => x.key.PairWithValue(x.contents.NullToEmpty().Join(", ")))
                    .ToArray();

                KeyValuePair<string, string>[] getFormDataFiles() =>
                    request.formDataFiles
                    .NullToEmpty()
                    .Select(x => x.fileName.PairWithValue($"disposition:{x.contentDisposition}, mime:{x.contentType}, bytes:{x.length}, blobId:{(x.contents != null ? x.contents.Id : string.Empty)}"))
                    .ToArray();

                method = request.method;
                route = request.route;
                status = request.status;
                timestamp = request.when;
                url = request.url.AbsoluteUri;
                id = request.id;
                this.actorId = actorId;
                formdata = getFormData()
                    .Concat(getFormDataFiles())
                    .ToArray();
                blob = default(string);
            }

            public static async Task<ActivityLog> ConvertAsync(KeyValuePair<Guid,MonitoringRequest> kvp)
            {
                var actorId = kvp.Key;
                var request = kvp.Value;
                var log = new ActivityLog(request, actorId);
                if (request.body != null)
                {
                    try
                    {
                        log.blob = await request.body.LoadStreamAsync(
                        (id, stream, mediaType, disposition) =>
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                var json = reader.ReadToEnd();
                                return json;
                            }
                        },
                        () => default(string));
                    }
                    catch (Exception ex)
                    {
                        // Suppress errors reading log content - log entry will be returned without detailed message
                        System.Diagnostics.Debug.WriteLine($"Failed to read activity log content: {ex.Message}");
                    }
                }
                return log;
            }

            public int CompareTo(ActivityLog other)
            {
                // ascending
                return timestamp.CompareTo(other.timestamp);
            }
        }

        public static IEnumerableAsync<ActivityLog> GetActivityLog(Guid? actorIdMaybe, string tryRoute, string tryMethod, DateTime when)
        {

            return when
                .StorageGetBy((MonitoringRequest mr) => mr.when,
                    readAhead: 25)
                .Select(
                    (mr) =>
                    {
                        var tryAuth = mr.headers
                            .NullToEmpty()
                            .First(
                                (x, next) =>
                                {
                                    if (x.key == "Authorization")
                                        return x.value;

                                    return next();
                                },
                                () => default(string));
                        if (string.IsNullOrWhiteSpace(tryAuth))
                            return default(KeyValuePair<Guid, MonitoringRequest>?);

                        var jwtActorIdMaybe = tryAuth.GetClaimsJwtString(
                            (claims) => claims.GetActorId(x => x, () => default(Guid?)),
                            (why) => default(Guid?));

                        if (jwtActorIdMaybe.IsDefault())
                            return default(KeyValuePair<Guid, MonitoringRequest>?);

                        return jwtActorIdMaybe.Value.PairWithValue(mr);
                    })
                .SelectWhereHasValue()
                .Where(
                    (kvp) =>
                    {
                        if (!actorIdMaybe.IsDefault() && actorIdMaybe.Value != kvp.Key)
                            return false;

                        if (tryRoute.HasBlackSpace() && !kvp.Value.route.Equals(tryRoute, StringComparison.OrdinalIgnoreCase))
                            return false;

                        if (tryMethod.HasBlackSpace() && !kvp.Value.method.Equals(tryMethod, StringComparison.OrdinalIgnoreCase))
                            return false;

                        return true;
                    })
                .Select(ActivityLog.ConvertAsync)
                .Await(readAhead: 25);
        }
    }
}

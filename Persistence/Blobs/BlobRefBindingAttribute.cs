using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Net.Http;

using Microsoft.AspNetCore.Http;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization;
using EastFive.Reflection;
using System.Net.Mime;

namespace EastFive.Azure.Persistence.Blobs
{
    internal interface IApiBoundBlobRef : IBlobRef
    {
        new string ContainerName { set; }

        void Write(JsonWriter writer, JsonSerializer serializer,
            IHttpRequest httpRequest, IAzureApplication application);
    }

    public interface IProvideBlobAccess
    {
        public string Container { get; }
        public bool UseCDN { get; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public class BlobAccessorAttribute : Attribute, IProvideBlobAccess
    {
        public string Container { get; set; }
        public bool UseCDN { get; set; }
    }

    public class BlobRefBindingAttribute : Attribute,
        IBindParameter<string>, IBindApiParameter<string>, IBindApiPropertyOrField<string>,
        IBindParameter<Microsoft.AspNetCore.Http.IFormFile>, IBindApiParameter<Microsoft.AspNetCore.Http.IFormFile>,
        IBindParameter<JToken>, IBindApiParameter<JToken>,
        IConvertJson, ICastJsonProperty
    {
        #region Bind

        #region String

        public TResult Bind<TResult>(ParameterInfo parameter, string content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            return Bind(parameter.ParameterType, content,
                application: application,
                onParsed: (blobRefStorageAsObj) =>
                {
                    return ContainerizeBlob(parameter, blobRefStorageAsObj,
                        onParsed: onParsed,
                        onDidNotBind: onDidNotBind);
                },
                onDidNotBind: onDidNotBind,
                onBindingFailure: onBindingFailure);
        }

        public TResult Bind<TResult>(MemberInfo propertyOrField, string content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            return Bind(propertyOrField.GetPropertyOrFieldType(), content,
                application: application,
                onParsed: (blobRefStorageAsObj) =>
                {
                    var containerName = propertyOrField.BlobContainerName();
                    var blobRefStorage = (BlobRefStorage)blobRefStorageAsObj;
                    blobRefStorage.ContainerName = containerName;
                    return onParsed(blobRefStorage);
                },
                onDidNotBind: onDidNotBind,
                onBindingFailure: onBindingFailure);
        }

        public TResult Bind<TResult>(Type type, string content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            if (!typeof(IBlobRef).IsAssignableFrom(type))
                return onDidNotBind($"{nameof(BlobRefBindingAttribute)} only binds {nameof(IBlobRef)}");

            if(Guid.TryParse(content, out Guid blobId))
            {
                var guidBlobRefValue = new BlobRefStorage()
                {
                    Id = blobId.AsBlobName(),
                };
                return onParsed(guidBlobRefValue);
            }

            if (Uri.TryCreate(content, UriKind.Absolute, out Uri urlContent))
            {
                var urlBlobRef = new BlobRefUrl(urlContent);
                return onParsed(urlBlobRef);
            }

            var value = new BlobRefStorage()
            {
                Id = content,
            };
            return onParsed(value);
        }

        #endregion

        #region IFormFile

        public TResult Bind<TResult>(ParameterInfo parameter, Microsoft.AspNetCore.Http.IFormFile content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            if (!parameter.TryGetBlobContainerName(out string containerName))
                return onDidNotBind("Could not connect parameter with property");

            var type = parameter.ParameterType;
            if (type.IsSubClassOfGeneric(typeof(Property<>)))
                type = type.GetGenericArguments().First();

            if (!typeof(IBlobRef).IsAssignableFrom(type))
                return onDidNotBind($"{nameof(BlobRefBindingAttribute)} only binds {nameof(IBlobRef)}");

            var value = new BlobRefFormFile(content);
            value.ContainerName = containerName;
            return onParsed(value);
        }

        public TResult Bind<TResult>(Type type, IFormFile content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            if (type.IsSubClassOfGeneric(typeof(Property<>)))
                type = type.GetGenericArguments().First();
            
            if (!typeof(IBlobRef).IsAssignableFrom(type))
                return onDidNotBind($"{nameof(BlobRefBindingAttribute)} only binds {nameof(IBlobRef)}");

            var value = new BlobRefFormFile(content);
            return onParsed(value);
        }

        #endregion

        #region JToken

        public TResult Bind<TResult>(ParameterInfo parameter, JToken content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            return Bind(parameter.ParameterType, content,
                application: application,
                onParsed: (blobRefStorageAsObj) =>
                {
                    return ContainerizeBlob(parameter, blobRefStorageAsObj,
                        onParsed: onParsed,
                        onDidNotBind: onDidNotBind);
                },
                onDidNotBind: onDidNotBind,
                onBindingFailure: onBindingFailure);
        }

        public TResult Bind<TResult>(Type type, JToken content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            if (!typeof(IBlobRef).IsAssignableFrom(type))
                return onDidNotBind($"{nameof(BlobRefBindingAttribute)} only binds {nameof(IBlobRef)}");

            if (content.Type == JTokenType.Guid)
            {
                var guidValue = content.Value<Guid>();
                var value = new BlobRefStorage()
                {
                    Id = guidValue.AsBlobName(),
                };
                return onParsed(value);
            }
            if (content.Type == JTokenType.String)
            {
                var stringValue = content.Value<string>();
                if (Uri.TryCreate(stringValue, UriKind.Absolute, out Uri urlContent))
                {
                    var urlBlobRef = new BlobRefUrl(urlContent);
                    return onParsed(urlBlobRef);
                }

                if (Guid.TryParse(stringValue, out Guid guidValue))
                {
                    var valueGuid = new BlobRefStorage()
                    {
                        Id = guidValue.AsBlobName(),
                    };
                    return onParsed(valueGuid);
                }

                var value = new BlobRefStorage()
                {
                    Id = stringValue,
                };
                return onParsed(value);
            }

            return onDidNotBind($"{nameof(BlobRefBindingAttribute)} cannot bind JToken of type `{content.Type}` to {nameof(IBlobRef)}");
        }

        #endregion

        private class BlobRefFormFile : IApiBoundBlobRef
        {
            public string Id { get; private set; }

            private string containerName = default;

            public string ContainerName 
            {
                get
                {
                    return containerName;
                }
                set
                {
                    if (value.IsNullOrWhiteSpace())
                        throw new Exception("Container names but not be empty.");
                    this.containerName = value;
                }
            }

            public IFormFile content;

            public BlobRefFormFile(IFormFile content)
            {
                Id = GetName();
                this.content = content;

                string GetName()
                {
                    if (content.FileName.HasBlackSpace())
                        return content.FileName;

                    if(content.ContentDisposition.HasBlackSpace())
                    {
                        if(ContentDispositionHeaderValue.TryParse(content.ContentDisposition,
                            out ContentDispositionHeaderValue contentDisposition))
                        {
                            if (contentDisposition.FileName.HasBlackSpace())
                                return contentDisposition.FileName;
                        }
                    }

                    return Guid.NewGuid().AsBlobName();
                }
            }

            public async Task<TResult> LoadBytesAsync<TResult>(
                Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound, 
                Func<TResult> onNotFound, Func<ExtendedErrorInformationCodes, 
                    string, TResult> onFailure = null)
            {
                using (var stream = content.OpenReadStream())
                {
                    var bytes = await stream.ToBytesAsync();
                    ContentDispositionHeaderValue.TryParse(content.ContentDisposition,
                        out ContentDispositionHeaderValue contentDisposition);
                    if (contentDisposition.IsDefaultOrNull())
                    {
                        contentDisposition = new ContentDispositionHeaderValue("inline")
                        {
                            FileName = this.Id,
                        };
                    }
                    if (!MediaTypeHeaderValue.TryParse(content.ContentType,
                            out MediaTypeHeaderValue mediaType))
                        mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);
                    
                    return onFound(this.Id, bytes, mediaType, contentDisposition);
                }
            }

            public Task<TResult> LoadStreamAsync<TResult>(
                Func<string, System.IO.Stream, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound, Func<ExtendedErrorInformationCodes,
                    string, TResult> onFailure = null)
            {
                using (var stream = content.OpenReadStream())
                {
                    ContentDispositionHeaderValue.TryParse(content.ContentDisposition,
                        out ContentDispositionHeaderValue contentDisposition);
                    if (contentDisposition.IsDefaultOrNull())
                    {
                        contentDisposition = new ContentDispositionHeaderValue("inline")
                        {
                            FileName = this.Id,
                        };
                    }
                    if (!MediaTypeHeaderValue.TryParse(content.ContentType,
                            out MediaTypeHeaderValue mediaType))
                        mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);

                    return onFound(this.Id, stream, mediaType, contentDisposition).AsTask();
                }
            }

            public async Task<TResult> LoadStreamToAsync<TResult>(System.IO.Stream streamOut,
                Func<string, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound, Func<ExtendedErrorInformationCodes,
                    string, TResult> onFailure = null)
            {
                using (var streamIn = content.OpenReadStream())
                {
                    ContentDispositionHeaderValue.TryParse(content.ContentDisposition,
                        out ContentDispositionHeaderValue contentDisposition);
                    if (contentDisposition.IsDefaultOrNull())
                    {
                        contentDisposition = new ContentDispositionHeaderValue("inline")
                        {
                            FileName = this.Id,
                        };
                    }
                    if (!MediaTypeHeaderValue.TryParse(content.ContentType,
                            out MediaTypeHeaderValue mediaType))
                        mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);

                    await streamIn.CopyToAsync(streamOut);

                    return onFound(this.Id, mediaType, contentDisposition);
                }
            }

            public void Write(JsonWriter writer, JsonSerializer serializer,
                IHttpRequest httpRequest, IAzureApplication application)
            {
                var httpContext = (httpRequest as Api.Core.CoreHttpRequest).request.HttpContext;
                var coreUrlProvider = new Api.Core.CoreUrlProvider(httpContext);
                var cdn = (application as IAzureApplication).CDN;
                var request = new RequestMessage<BlobRefBindingAttribute>(cdn);
                writer.WriteValue(Id);
            }
        }

        #endregion

        #region Cast

        #region IConvertJson

        public bool CanConvert(Type objectType, IHttpRequest httpRequest, IApplication application)
        {
            // Could be a read, wish I knew
            if (objectType == typeof(IBlobRef))
                return true;
            if (objectType.IsSubClassOfGeneric(typeof(IBlobRef)))
                return true;

            if (objectType.IsSubClassOfGeneric(typeof(IApiBoundBlobRef)))
                if (application is IAzureApplication)
                    return true;

            return false;
        }

        public void Write(JsonWriter writer, object value, JsonSerializer serializer,
            IHttpRequest httpRequest, IApplication application)
        {
            if(value.IsNull())
            {
                writer.WriteNull();
                return;
            }

            if (value is IApiBoundBlobRef)
            {
                (value as IApiBoundBlobRef).Write(writer, serializer,
                    httpRequest, (application as IAzureApplication));
                return;
            }

            var blobRef = (IBlobRef)value;
            writer.WriteValue(blobRef.Id);
        }

        public object Read(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer, IHttpRequest httpRequest, IApplication application)
        {
            var propertyValue = reader.Value;

            return default(IBlobRef);
        }

        #endregion

        #region ICastJson

        public bool CanConvert(MemberInfo member, ParameterInfo paramInfo,
            IHttpRequest httpRequest, IApplication application, IProvideApiValue apiValueProvider,
            object objectValue)
        {
            var type = member.GetPropertyOrFieldType();
            var isBlobRef = typeof(IBlobRef).IsAssignableFrom(type);
            return isBlobRef;
        }

        public Task WriteAsync(JsonWriter writer, JsonSerializer serializer,
            MemberInfo member, ParameterInfo paramInfo, IProvideApiValue apiValueProvider,
            object objectValue, object memberValue,
            IHttpRequest httpRequest, IApplication application)
        {
            if(memberValue is ICastJsonProperty)
            {
                var blobSelfSerializer = (ICastJsonProperty)memberValue;
                if (blobSelfSerializer.CanConvert(member, paramInfo, httpRequest, application, apiValueProvider, objectValue))
                {
                    return blobSelfSerializer.WriteAsync(writer, serializer, member, paramInfo, apiValueProvider,
                        objectValue: objectValue, memberValue: memberValue,
                        httpRequest: httpRequest, application: application);
                }
            }

            var blobRef = (IBlobRef)memberValue;
            var typeToSearch = member.TryGetAttributeInterface(
                    out IReferenceBlobProperty blobPropertyReference) ?
                blobPropertyReference.PropertyOrField.DeclaringType
                :
                member.DeclaringType;

            return typeToSearch
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .TryWhere(
                    (MethodInfo method, out (IProvideBlobAccess, string) attr) =>
                    {
                        if (!method.TryGetAttributeInterface(out attr.Item1))
                        {
                            attr = default;
                            return false;
                        }

                        attr.Item2 = method.GetParameters()
                            .Where(param => param.ParameterType.IsAssignableFrom(typeof(IBlobRef)))
                            .First(
                                (paramInfo, next) =>
                                {
                                    var propertyName = apiValueProvider.GetPropertyName(member);
                                    if (!paramInfo.TryGetAttributeInterface(out IBindApiValue apiBinder))
                                        return next();

                                    if (propertyName.Equals(apiBinder.GetKey(paramInfo)))
                                        return propertyName;

                                    return next();
                                },
                                () => default(string));

                        return attr.Item2.HasBlackSpace();
                    })
                .Single(
                    (tpl) =>
                    {
                        var (method, (attr, queryKey)) = tpl;
                        var buildUrlMethod = typeof(BlobRefBindingAttribute)
                            .GetMethods(BindingFlags.Static | BindingFlags.Public)
                            .Where(method => method.Name == nameof(BuildUrl))
                            .First();
                        var castBuildUrlMethod = buildUrlMethod.MakeGenericMethod(method.DeclaringType);
                        var urlToUpdate = (Uri)castBuildUrlMethod.Invoke(null,
                            new object[] { attr, application, httpRequest, method });

                        var queryValue = blobRef.Id;
                        var url = urlToUpdate.AddQueryParameter(queryKey, queryValue);

                        return writer.WriteValueAsync(url.OriginalString);
                    },
                    async () =>
                    {
                        await writer.WriteValueAsync(blobRef.Id);
                        await writer.WriteCommentAsync($"{member.DeclaringType.FullName} needs a method with {nameof(IProvideBlobAccess)} Attribute.");
                    });
        }

        public static Uri BuildUrl<TResource>(IProvideBlobAccess attr,
            IAzureApplication application, IHttpRequest httpRequest,
            MethodInfo method)
        {
            var builder = attr.UseCDN?
                new QueryableServer<TResource>(application.CDN)
                :
                new QueryableServer<TResource>(httpRequest);

            var url = method.ContainsCustomAttribute<HttpActionAttribute>() ?
                builder
                    .HttpAction(method.GetCustomAttribute<HttpActionAttribute>().Action)
                    .Location()
                :
                builder.Location();
            return url;
        }

        #endregion

        #endregion

        private TResult ContainerizeBlob<TResult>(ParameterInfo parameter,
                object blobRefStorageAsObj,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind)
        {
            if (!parameter.TryGetBlobContainerName(out string containerName))
                return onDidNotBind("Could not connect parameter with property");

            if (blobRefStorageAsObj is BlobRefStorage)
            {
                var blobRefStorage = (BlobRefStorage)blobRefStorageAsObj;
                blobRefStorage.ContainerName = containerName;
                return onParsed(blobRefStorage);
            }

            if (blobRefStorageAsObj is BlobRefUrl)
            {
                var blobRefUrl = (BlobRefUrl)blobRefStorageAsObj;
                blobRefUrl.ContainerName = containerName;
                return onParsed(blobRefUrl);
            }

            throw new Exception();
        }
    }
}

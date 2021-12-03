﻿using System;
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
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public class BlobAccessorAttribute : Attribute, IProvideBlobAccess
    {

    }

    public class BlobRefBindingAttribute : Attribute,
        IBindParameter<string>, IBindApiParameter<string>, IBindApiPropertyOrField<string>,
        IBindParameter<Microsoft.AspNetCore.Http.IFormFile>, IBindApiParameter<Microsoft.AspNetCore.Http.IFormFile>,
        IBindParameter<JToken>, IBindApiParameter<JToken>,
        IConvertJson, ICastJson
    {
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
            Func<string, TResult> onBindingFailure) => Bind(parameter.ParameterType, content,
                application: application,
                onParsed: onParsed,
                onDidNotBind: onDidNotBind,
                onBindingFailure: onBindingFailure);

        public TResult Bind<TResult>(Type type, IFormFile content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            if (type.IsSubClassOfGeneric(typeof(Property<>)))
                type = type.GetGenericArguments().First();
            
            if (!typeof(IBlobRef).IsAssignableFrom(type))
                return onDidNotBind("BlobRefBindingAttribute only binds IBlobRef");

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
                    if (containerName.HasBlackSpace())
                        return containerName;
                    throw new Exception($"{nameof(BlobRefProperty)} must be used as the Http Method Parameter decorator for IBlobRef");
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
                        var cd = new System.Net.Mime.ContentDisposition(content.ContentDisposition);
                        if (cd.FileName.HasBlackSpace())
                            return cd.FileName;
                    }

                    return Guid.NewGuid().AsBlobName();
                }
            }

            public async Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], string, string, TResult> onFound, 
                Func<TResult> onNotFound, Func<ExtendedErrorInformationCodes, 
                    string, TResult> onFailure = null)
            {
                using (var stream = content.OpenReadStream())
                {
                    var bytes = await stream.ToBytesAsync();
                    return onFound(this.Id, bytes, this.content.ContentType, this.content.FileName);
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
            var blobRef = (IBlobRef)memberValue;
            return member.DeclaringType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .TryWhere(
                    (MethodInfo method, out IProvideBlobAccess attr) => method.TryGetAttributeInterface(out attr))
                .First(
                    (tpl, next) =>
                    {
                        var (method, attr) = tpl;
                        var buildUrlMethod = typeof(BlobRefBindingAttribute)
                            .GetMethods(BindingFlags.Static | BindingFlags.Public)
                            .Where(method => method.Name == nameof(BuildUrl))
                            .First();
                        var castBuildUrlMethod = buildUrlMethod.MakeGenericMethod(method.DeclaringType);
                        var url = (Uri)castBuildUrlMethod.Invoke(null,
                            new object[] { application, httpRequest, method, blobRef });
                        return writer.WriteValueAsync(url.OriginalString);
                    },
                    async () =>
                    {
                        await writer.WriteValueAsync(blobRef.Id);
                        await writer.WriteCommentAsync($"{member.DeclaringType.FullName} needs a method with {nameof(IProvideBlobAccess)} Attribute.");
                    });
        }

        public static Uri BuildUrl<TResource>(IAzureApplication application, IHttpRequest httpRequest,
            MethodInfo method, IBlobRef blobRef)
        {
            var builder = new QueryableServer<TResource>(httpRequest);
            //var builder = new QueryableServer<TResource>(application.CDN);
            var url = method.ContainsCustomAttribute<HttpActionAttribute>() ?
                builder
                    .HttpAction(method.GetCustomAttribute<HttpActionAttribute>().Action)
                    .Location()
                :
                builder.Location();

            return method.GetParameters()
                .Where(param => param.ParameterType.IsAssignableFrom(typeof(IBlobRef)))
                .TryWhere((ParameterInfo param, out IBindApiValue apiValueBinder) =>
                    param.TryGetAttributeInterface(out apiValueBinder))
                .Aggregate(url,
                    (urlToUpdate, itemAndApiValueBinder) =>
                    {
                        var (paramInfo, apiValueBinder) = itemAndApiValueBinder;
                        var queryKey = apiValueBinder.GetKey(paramInfo);
                        var queryValue = blobRef.Id;
                        return urlToUpdate.AddQueryParameter(queryKey, queryValue);
                    });
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

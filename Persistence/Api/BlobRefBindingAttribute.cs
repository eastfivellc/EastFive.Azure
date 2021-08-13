using EastFive.Api;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using EastFive.Serialization;
using System.Net.Http;

namespace EastFive.Azure.Persistence
{
    internal interface IApiBoundBlobRef : IBlobRef
    {
        new string ContainerName { set; }
    }

    public class BlobRefBindingAttribute : Attribute, IBindApiParameter<string>,
        IBindApiParameter<Microsoft.AspNetCore.Http.IFormFile>
    {
        public TResult Bind<TResult>(Type type, string content,
                IApplication application,
            Func<object, TResult> onParsed,
            Func<string, TResult> onDidNotBind,
            Func<string, TResult> onBindingFailure)
        {
            if (!typeof(IBlobRef).IsAssignableFrom(type))
                return onDidNotBind("BlobRefBindingAttribute only binds IBlobRef");

            if (Uri.TryCreate(content, UriKind.Absolute, out Uri urlContent))
            {
                var urlBlobRef = new BlobRefUrl(urlContent);
                return onParsed(urlBlobRef);
            }

            var value = new BlobRefString(content);
            return onParsed(value);
        }

        private class BlobRefString : IApiBoundBlobRef
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

            public BlobRefString(string id)
            {
                Id = id;
            }

            public async Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], string, string, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            {
                var blobName = this.Id;
                return await AzureTableDriverDynamic
                    .FromSettings()
                    .BlobLoadBytesAsync(blobName:blobName, containerName:this.ContainerName,
                        (bytes, properties) =>
                        {
                            ContentDispositionHeaderValue.TryParse(
                                properties.ContentDisposition, out ContentDispositionHeaderValue fileName);
                            return onFound(blobName, bytes,
                                properties.ContentType, fileName.FileName);
                        },
                        onNotFound:onNotFound,
                        onFailure: onFailure);
            }
        }

        private class BlobRefUrl : IApiBoundBlobRef
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
            
            private Uri content;

            public BlobRefUrl(Uri content)
            {
                Id = Guid.NewGuid().ToString("N");
                this.content = content;
            }

            public async Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], string, string, TResult> onFound,
                Func<TResult> onNotFound, Func<ExtendedErrorInformationCodes,
                    string, TResult> onFailure = null)
            {
                using(var client = new HttpClient())
                using (var response = await client.GetAsync(this.content))
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    return onFound(this.Id, bytes, 
                        response.GetContentMediaTypeNullSafe(),
                        response.GetFileNameNullSafe());
                }
            }
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
                return onDidNotBind("BlobRefBindingAttribute only binds IBlobRef");

            var value = new BlobRefFormFile(content);
            return onParsed(value);
        }

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
                Id = Guid.NewGuid().ToString("N");
                this.content = content;
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
        }
    }
}

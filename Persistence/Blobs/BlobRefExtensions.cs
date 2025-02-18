using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Net.Mime;

using Microsoft.Azure.Cosmos.Table;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization;
using EastFive.Reflection;
using EastFive.Api;
using Newtonsoft.Json;
using EastFive.IO;

namespace EastFive.Azure.Persistence.Blobs
{
    public static class BlobRefExtensions
    {
        public static string BlobContainerName(this MemberInfo member)
        {
            return member.TryGetAttributeInterface(out IDefineBlobContainer blobContainer) ?
                blobContainer.ContainerName
                :
                GetDefault();

            string GetDefault()
            {
                var validCharacters = $"{member.DeclaringType.Name}-{member.Name}"
                    .ToLower()
                    .Where(c => char.IsLetterOrDigit(c))
                    .Take(63)
                    .Join();
                var containerName = string.Concat(validCharacters);
                return containerName;
            }
        }

        public static bool TryGetBlobContainerName(this ParameterInfo parameter, out string containerName)
        {
            if(parameter.TryGetAttributeInterface(out IDefineBlobContainer blobContainerDefinition))
            {
                containerName = blobContainerDefinition.ContainerName;
                return true;
            }

            var parameterName = parameter.TryGetAttributeInterface(out Api.IBindApiValue apiBinding) ?
                apiBinding.GetKey(parameter)
                :
                parameter.Name;

            containerName = parameter.Member.DeclaringType
                .GetPropertyAndFieldsWithAttributesInterface<Api.IProvideApiValue>()
                .Where(tpl => tpl.Item2.GetPropertyName(tpl.Item1) == parameterName)
                .First(
                    (tpl, next) =>
                    {
                        var (memberInfo, apiValueProvider) = tpl;
                        return memberInfo.BlobContainerName();
                    },
                    () => string.Empty);

            return containerName.HasBlackSpace();
        }

        public static string AsBlobName(this Guid guid)
        {
            return guid.ToString("N");
        }

        public static Task<(byte[] data, string contentType)> ReadBytesAsync(this IBlobRef blobRef) =>
            blobRef.ReadBytesAsync(
                onSuccess: (bytes, contentType) => (bytes, contentType));

        public static Task<TResult> ReadBytesAsync<TResult>(this IBlobRef blobRef,
            Func<byte[], string, TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureTableDriverDynamic.RetryDelegate onTimeout = null)
        {
            var blobName = blobRef.Id;
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadBytesAsync(blobName, blobRef.ContainerName,
                    (bytes, properties) => onSuccess(bytes, properties.ContentType),
                    onNotFound,
                    onFailure: onFailure,
                    onTimeout: onTimeout);
        }

        public static Task<(Stream, string)> ReadStreamAsync(this IBlobRef blobRef) =>
            blobRef.ReadStreamAsync(
                onSuccess: (stream, contentType) => (stream, contentType));

        public static Task<TResult> ReadStreamAsync<TResult>(this IBlobRef blobRef,
            Func<Stream, string, TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureTableDriverDynamic.RetryDelegate onTimeout = null)
        {
            var blobName = blobRef.Id;
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadStreamAsync(blobName, blobRef.ContainerName,
                    (stream, properties) => onSuccess(stream, properties.ContentType),
                    onNotFound,
                    onFailure: onFailure,
                    onTimeout: onTimeout);
        }

        public static async Task<TResult> SaveAsNewAsync<TResult>(this IBlobRef blobRef,
            Func<IBlobRef, TResult> onSaved,
            Func<TResult> onAlreadyExist,
            string newBlobId = default,
            Func<string, string> mutateBlobId = default)
        {
            return await await blobRef.LoadBytesAsync(
                async (currentBlobName, bytes, mediaType, disposition) =>
                {
                    var contentType = mediaType.MediaType;
                    if (newBlobId.IsNullOrWhiteSpace())
                        newBlobId = mutateBlobId.IsNotDefaultOrNull()?
                            mutateBlobId(currentBlobName)
                            :
                            currentBlobName;
                    return await AzureTableDriverDynamic
                        .FromSettings()
                        .BlobCreateAsync(bytes, newBlobId, blobRef.ContainerName,
                            () =>
                            {
                                var blobRefNew = (IBlobRef)new BlobRef
                                {
                                    Id = newBlobId,
                                    ContainerName = blobRef.ContainerName,
                                    ContentType = contentType,
                                    FileName = newBlobId,
                                };
                                return onSaved(blobRefNew);
                            },
                            onAlreadyExists:() =>
                            {
                                return onAlreadyExist();
                            },
                            contentType: contentType);
                },
                onNotFound: () =>
                {
                    throw new Exception("Blob could not be loaded.");
                });
            
        }

        public static async Task<TResult> SaveOrUpdateAsync<TResult>(this IBlobRef blobRef,
            Func<bool, byte[], string, string, TResult> onSaved,
            Func<TResult> onCouldNotAccess = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            return await await blobRef.LoadBytesAsync(
                async (blobName, bytes, contentType, disposition) =>
                {
                    return await AzureTableDriverDynamic
                        .FromSettings()
                        .BlobCreateOrUpdateAsync(bytes, blobRef.Id, blobRef.ContainerName,
                            () =>
                            {
                                return onSaved(false, bytes, contentType.MediaType, disposition.FileName);
                            },
                            onFailure: onFailure,
                            contentType: disposition.DispositionType);
                },
                onNotFound: onCouldNotAccess.AsAsyncFunc());
        }

        public static IBlobRef CreateUrlBlobRef<TResource>(
            this Uri blobData,
            Expression<Func<TResource, IBlobRef>> selectProperty,
            Guid? blobId = default, string blobName = default)
        {
            selectProperty.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            var newBlobId = GetBlobName();

            return new BlobRefUrl(blobData)
            {
                ContainerName = containerName,
                Id = newBlobId,
            };

            string GetBlobName()
            {
                if (blobName.HasBlackSpace())
                    return blobName;

                if (blobId.HasValue)
                    return blobId.Value.AsBlobName();

                var paths = blobData.ParsePath();
                if(paths.Any())
                    return paths.Last();

                return Guid.NewGuid().AsBlobName();
            }
        }

        public static Task<TResult> CreateOrUpdateStorageBlobRefAsync<TResource, TResult>(
                this Uri blobData,
                Expression<Func<TResource, IBlobRef>> selectProperty,
            Func<IBlobRef, TResult> onSaved,
            Func<TResult> onFailure,
                Guid? blobId = default, string blobName = default)
        {
            var blob = blobData.CreateUrlBlobRef(selectProperty, blobId: blobId, blobName: blobName);
            return blob.CreateOrUpdateStorageBlobRefAsync(
                onSaved: onSaved, onFailure: onFailure);
        }

        public static async Task<TResult> CreateStorageBlobRefAsync<TResource, TResult>(
            this IBlobRef from,
            Expression<Func<TResource, IBlobRef>> selectProperty,
            Func<IBlobRef, TResult> onSaved,
            Func<TResult> onFailure,
                Guid? blobId = default, string blobName = default)
        {
            return await await from.LoadStreamAsync(
                async (id, streamSource, mediaType, cd) =>
                {
                    var blobRef = await selectProperty.CreateBlobRefFromStreamAsync(
                        streamWrite => streamSource.CopyToAsync(streamWrite),
                        contentType: mediaType.MediaType, fileName: cd.FileName,
                        blobId: blobId, blobName: blobName);
                    return onSaved(blobRef);
                },
                onFailure.AsAsyncFunc());
        }

        public static async Task<TResult> CreateOrUpdateStorageBlobRefAsync<TResult>(
            this IBlobRef blobToStore,
            Func<IBlobRef, TResult> onSaved,
            Func<TResult> onFailure)
        {
            return await await blobToStore.LoadBytesAsync(
                async (id, streamIn, mediaType, contentDisposition) =>
                {
                    var cdStr = contentDisposition.ToString();
                    return await AzureTableDriverDynamic
                        .FromSettings()
                        .BlobCreateOrUpdateAsync(blobToStore.Id, blobToStore.ContainerName,
                                writeStreamAsync: async (streamOut) =>
                                {
                                    await streamOut.WriteAsync(streamIn, 0, streamIn.Length);
                                    return;
                                },
                            onSuccess: (blobInfo) =>
                            {
                                var storageBlob = new BlobRefStorage()
                                {
                                    Id = blobToStore.Id,
                                    ContainerName = blobToStore.ContainerName,
                                };
                                return onSaved(storageBlob);
                            },
                            contentTypeString: mediaType.MediaType,
                            contentDispositionString: cdStr);
                },
                onNotFound: onFailure.AsAsyncFunc());

            // Can't use streams because they get disposed

            //return await await blobToStore.LoadStreamAsync(
            //    async (id, streamIn, mediaType, contentDisposition) =>
            //    {
            //        var cdStr = contentDisposition.ToString();
            //        return await AzureTableDriverDynamic
            //            .FromSettings()
            //            .BlobCreateOrUpdateAsync(blobToStore.Id, blobToStore.ContainerName,
            //                writeStreamAsync: async (streamOut) =>
            //                {
            //                    await streamIn.CopyToAsync(streamOut);
            //                    return;
            //                },
            //                onSuccess:() =>
            //                {
            //                    var storageBlob = new BlobRefStorage()
            //                    {
            //                        Id = blobToStore.Id,
            //                        ContainerName = blobToStore.ContainerName,
            //                    };
            //                    return onSaved(storageBlob);
            //                },
            //                contentType: mediaType.MediaType,
            //                contentDisposition: cdStr);
            //    },
            //    onNotFound: onFailure.AsAsyncFunc());
        }

        public static async Task<TResult> CreateOrUpdateStorageBlobRefAsync<TResult>(
            this IBlobRef blobToStore,
            Func<IBlobRef, byte [], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onSaved,
            Func<TResult> onFailure)
        {
            return await await blobToStore.LoadBytesAsync(
                async (id, streamIn, mediaType, contentDisposition) =>
                {
                    var cdStr = contentDisposition.ToString();
                    return await AzureTableDriverDynamic
                        .FromSettings()
                        .BlobCreateOrUpdateAsync(blobToStore.Id, blobToStore.ContainerName,
                            writeStreamAsync: (streamOut) => streamOut.WriteAsync(streamIn, 0, streamIn.Length),
                            onSuccess: (blobInfo) =>
                            {
                                var storageBlob = new BlobRefStorage()
                                {
                                    Id = blobToStore.Id,
                                    ContainerName = blobToStore.ContainerName,
                                };
                                return onSaved(storageBlob, streamIn, mediaType, contentDisposition);
                            },
                            contentTypeString: mediaType.MediaType,
                            contentDispositionString: cdStr);
                },
                onNotFound: onFailure.AsAsyncFunc());
        }

        public static async Task<IBlobRef> CreateBlobRefAsync<TResource>(
            this byte[] blobData,
            Expression<Func<TResource, IBlobRef>> selectProperty,
            string contentType = default, string fileName = default,
            Guid? blobId = default, string blobName = default)
        {
            selectProperty.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            var newBlobId = blobName.HasBlackSpace()?
                blobName
                :
                blobId.HasValue?
                    blobId.Value.AsBlobName()
                    :
                    Guid.NewGuid().AsBlobName();
            return await AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(blobData, newBlobId, containerName,
                    () =>
                    {
                        return (IBlobRef)new BlobRef
                        {
                            bytes = blobData,
                            Id = newBlobId,
                            ContainerName = containerName,
                            ContentType = contentType,
                            FileName = fileName.HasBlackSpace()? fileName : newBlobId,
                        };
                    },
                    contentType: contentType,
                    fileName: fileName);
        }

        public static async Task<TResult> CreateBlobRefAsync<TResult, TResource>(
            this byte[] blobData,
            Expression<Func<TResource, IBlobRef>> selectProperty,
            Func<IBlobRef, TResult> onCreated,
            Func<IBlobRef, TResult> onAlreadyExists = default,
            string contentType = default,
            string fileName = default,
            Guid? blobId = default, string blobName = default)
        {
            selectProperty.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            var newBlobId = blobName.HasBlackSpace() ?
                blobName
                :
                blobId.HasValue ?
                    blobId.Value.AsBlobName()
                    :
                    Guid.NewGuid().AsBlobName();
            return await AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(blobData, newBlobId, containerName,
                    () =>
                    {
                        var blobRefNew = new BlobRef
                        {
                            bytes = blobData,
                            Id = newBlobId,
                            ContainerName = containerName,
                            ContentType = contentType,
                            FileName = fileName.HasBlackSpace() ? fileName : newBlobId,
                        };
                        return onCreated(blobRefNew);
                    },
                    onAlreadyExists:() =>
                    {
                        var blobRef = new BlobRefStorage()
                        {
                            Id = newBlobId,
                            ContainerName = containerName,
                        };
                        return onAlreadyExists(blobRef);
                    },
                    contentType: contentType,
                    fileName:fileName);
        }

        public static async Task<IBlobRef> CreateBlobRefFromStreamAsync<TResource>(
            this Expression<Func<TResource, IBlobRef>> selectProperty,
            Func<Stream, Task> writeBlobData,
            string contentType = default, string fileName = default,
            Guid? blobId = default, string blobName = default)
        {
            selectProperty.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            var newBlobId = blobName.HasBlackSpace() ?
                blobName
                :
                blobId.HasValue ?
                    blobId.Value.AsBlobName()
                    :
                    Guid.NewGuid().AsBlobName();
            return await AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(blobName:newBlobId, containerName: containerName,
                        writeAsync: writeBlobData,
                    onSuccess:() =>
                    {
                        return (IBlobRef)new BlobRef
                        {
                            Id = newBlobId,
                            ContainerName = containerName,
                            ContentType = contentType,
                            FileName = fileName.HasBlackSpace() ? fileName : newBlobId,
                        };
                    },
                    contentType: contentType,
                    fileName: fileName);
        }

        public static async Task<IBlobRef> CreateBlobRefAsync<TResource>(
            this System.Net.Http.HttpContent blobData,
            Expression<Func<TResource, IBlobRef>> selectProperty,
            Guid? blobId = default, string blobName = default)
        {
            selectProperty.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            var newBlobId = blobName.HasBlackSpace() ?
                blobName
                :
                blobId.HasValue ?
                    blobId.Value.AsBlobName()
                    :
                    Guid.NewGuid().AsBlobName();
            var mediaHeader = blobData.GetContentMediaTypeHeaderNullSafe();
            var contentType = mediaHeader.MediaType;
            var dispositionHeaderValue = blobData.GetContentDispositionNullSafe();
            var fileName = dispositionHeaderValue.FileName;
            return await AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(blobName: newBlobId, containerName: containerName,
                        writeAsync: async (stream) =>
                        {
                            var blobStream = await blobData.ReadAsStreamAsync();
                            await blobStream.CopyToAsync(stream);
                        },
                    onSuccess: () =>
                    {
                        return (IBlobRef)new BlobRef
                        {
                            Id = newBlobId,
                            ContainerName = containerName,
                            ContentType = contentType,
                            FileName = fileName.HasBlackSpace() ? fileName : newBlobId,
                        };
                    },
                    contentType: contentType,
                    dispositionString: dispositionHeaderValue.ToString());
        }

        public static Task<IBlobRef> CreateBlobStorageRefAsync<TResource>(
            this string blobName,
            Expression<Func<TResource, IBlobRef>> asPropertyOf,
            Func<Stream, Task> writeBlobData,
            string contentType = default, string fileName = default)
        {
            asPropertyOf.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();

            return AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(blobName: blobName, containerName: containerName,
                        writeAsync: writeBlobData,
                    onSuccess: () =>
                    {
                        return (IBlobRef)new BlobRefStorage
                        {
                            Id = blobName,
                            ContainerName = containerName,
                            //ContentType = contentType,
                            //FileName = fileName.HasBlackSpace() ? fileName : blobName,
                            //bytes = ?
                        };
                    },
                    contentType: contentType);
        }

        public static Task<IBlobRef> CreateOrReplaceBlobStorageRefAsync<TResource>(
            this string blobName,
            Expression<Func<TResource, IBlobRef>> asPropertyOf,
            Func<Stream, Task> writeBlobData,
            ContentType contentType = default, ContentDisposition contentDisposition = default)
        {
            asPropertyOf.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();

            return AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateOrUpdateAsync(blobName: blobName, containerName: containerName,
                        writeStreamAsync: writeBlobData,
                    onSuccess: (blobInfo) =>
                    {
                        return (IBlobRef)new BlobRefStorage
                        {
                            Id = blobName,
                            ContainerName = containerName,
                        };
                    },
                    contentType: contentType,
                    contentDisposition: contentDisposition);
        }

        public static IBlobRef AsBlobStorageRef<TResource>(
            this string blobName,
            Expression<Func<TResource, IBlobRef>> asPropertyOf)
        {
            asPropertyOf.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            return new BlobRefStorage()
            {
                Id = blobName,
                ContainerName = containerName,
            };
        }

        public static IBlobRef AsBlobUploadRef<TResource>(
            this string blobName,
            Expression<Func<TResource, IBlobRef>> asPropertyOf,
            TimeSpan? validFor = default)
        {
            asPropertyOf.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            return new SerializableBlobRef
            {
                Id = blobName,
                ContainerName = containerName,
                ValidFor = validFor,
            };
        }

        public static IBlobRef CastBlobRef<TResource>(
            this IBlobRef from,
            Expression<Func<TResource, IBlobRef>> to)
        {
            to.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            var newBlobId = from.Id;
            return new BlobCastRef
            {
                from = from,
                Id = newBlobId,
                ContainerName = containerName,
            };
        }

        public static IBlobRef CastStorageBlobRef<TResource>(
            this IBlobRef from,
            Expression<Func<TResource, IBlobRef>> to)
        {
            to.TryParseMemberComparison(out MemberInfo memberInfo);
            var containerName = memberInfo.BlobContainerName();
            var newBlobId = from.Id;
            return new BlobRefStorage()
            {
                Id = newBlobId,
                ContainerName = containerName,
            };
        }

        private class BlobRef : IBlobRef
        {
            public byte[] bytes;

            public string ContainerName { get; set; }

            public string Id { get; set; }

            public string ContentType { get; set; }

            public string FileName { get; set; }

            public Task<TResult> LoadBytesAsync<TResult>(
                Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            {
                if (!MediaTypeHeaderValue.TryParse(ContentType,
                            out MediaTypeHeaderValue mediaType))
                    mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);
                return onFound(Id, bytes,
                    mediaType,
                    new ContentDispositionHeaderValue("attachment")
                    {
                        FileName = FileName,
                    }).AsTask();
            }

            public Task<TResult> LoadStreamAsync<TResult>(
                Func<string, Stream, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            {
                if (!MediaTypeHeaderValue.TryParse(ContentType,
                            out MediaTypeHeaderValue mediaType))
                    mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);
                return onFound(Id, new MemoryStream(bytes),
                    mediaType,
                    new ContentDispositionHeaderValue("attachment")
                    {
                        FileName = FileName,
                    }).AsTask();
            }

            public async Task<TResult> LoadStreamToAsync<TResult>(Stream stream,
                Func<string, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            {
                if (!MediaTypeHeaderValue.TryParse(ContentType,
                            out MediaTypeHeaderValue mediaType))
                    mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);
                await stream.WriteAsync(bytes, 0, bytes.Length);
                return onFound(Id,
                    mediaType,
                    new ContentDispositionHeaderValue("attachment")
                    {
                        FileName = FileName,
                    });
            }
        }

        private class BlobCastRef : IBlobRef
        {
            public IBlobRef from;

            public string ContainerName { get; set; }

            public string Id { get; set; }

            public Task<TResult> LoadBytesAsync<TResult>(
                Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
                 => from.LoadBytesAsync(onFound: onFound, onNotFound: onNotFound, onFailure: onFailure);

            public Task<TResult> LoadStreamAsync<TResult>(
                Func<string, Stream, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
                 => from.LoadStreamAsync(onFound: onFound, onNotFound: onNotFound, onFailure: onFailure);

            public Task<TResult> LoadStreamToAsync<TResult>(Stream stream,
                Func<string, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
                 => from.LoadStreamToAsync(stream, onFound: onFound, onNotFound: onNotFound, onFailure: onFailure);
        }

        private class SerializableBlobRef : IBlobRef, ICastJsonProperty
        {
            public string ContainerName { get; set; }

            public string Id { get; set; }

            internal TimeSpan? ValidFor;

            public bool CanConvert(MemberInfo member, ParameterInfo paramInfo,
                IHttpRequest httpRequest, IApplication application,
                IProvideApiValue apiValueProvider, object objectValue)
            {
                var type = member.GetPropertyOrFieldType();
                var isBlobRef = typeof(IBlobRef).IsAssignableFrom(type);
                return isBlobRef;
            }

            public Task<TResult> LoadBytesAsync<TResult>(
                Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
                 => throw new NotImplementedException();

            public Task<TResult> LoadStreamAsync<TResult>(
                    Func<string, Stream, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                    Func<TResult> onNotFound,
                    Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default) =>
                throw new NotImplementedException();

            public Task<TResult> LoadStreamToAsync<TResult>(Stream stream,
                Func<string, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default) =>
                    throw new NotImplementedException();

            public Task WriteAsync(JsonWriter writer, JsonSerializer serializer,
                MemberInfo member, ParameterInfo paramInfo, IProvideApiValue apiValueProvider,
                object objectValue, object memberValue,
                IHttpRequest httpRequest, IApplication application)
            {
                var url = this.ContainerName.GenerateBlobFileSasLink(this.Id, lifespan: ValidFor);
                return writer.WriteValueAsync(url.OriginalString);
            }
        }
    }
}


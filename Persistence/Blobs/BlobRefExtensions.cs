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
            var parameterName = parameter.TryGetAttributeInterface(out Api.IBindApiValue apiBinding) ?
                        apiBinding.GetKey(parameter)
                        :
                        parameter.Name;

            containerName = parameter.Member.DeclaringType
                .GetPropertyAndFieldsWithAttributesInterface<Api.IProvideApiValue>()
                .Where(tpl => tpl.Item2.PropertyName == parameterName)
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

        public static Task<(byte[], string)> ReadBytesAsync(this IBlobRef blobRef) =>
            blobRef.ReadBytesAsync(
                onSuccess: (bytes, contentType) => (bytes, contentType));

        public static Task<TResult> ReadBytesAsync<TResult>(this IBlobRef blobRef,
            Func<byte[], string, TResult> onSuccess,
            Func<TResult> onNotFound = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            AzureStorageDriver.RetryDelegate onTimeout = null)
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
            AzureStorageDriver.RetryDelegate onTimeout = null)
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

        public static async Task<IBlobRef> SaveAsNewAsync(this IBlobRef blobRef,
            string newBlobId = default)
        {
            if (newBlobId.IsNullOrWhiteSpace())
                newBlobId = Guid.NewGuid().AsBlobName();
            var (bytes, contentType) = await blobRef.ReadBytesAsync();
            return await AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(bytes, newBlobId, blobRef.ContainerName,
                    () =>
                    {
                        return (IBlobRef)new BlobRef
                        {
                            Id = newBlobId,
                            ContainerName = blobRef.ContainerName,
                            ContentType = contentType,
                            FileName = newBlobId,
                        };
                    },
                    contentType: contentType);
        }

        public static async Task<TResult> SaveOrUpdateAsync<TResult>(this IBlobRef blobRef,
            Func<bool, byte[], string, string, TResult> onSaved,
            Func<TResult> onCouldNotAccess = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            return await await blobRef.LoadAsync(
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
                    contentType: contentType);
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
                    contentType: contentType);
        }

        private class BlobRef : IBlobRef
        {
            public byte[] bytes;

            public string ContainerName { get; set; }

            public string Id { get; set; }

            public string ContentType { get; set; }

            public string FileName { get; set; }

            public Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            {
                return onFound(Id, bytes,
                    new MediaTypeHeaderValue(ContentType),
                    new ContentDispositionHeaderValue("attachment")
                    {
                        FileName = FileName,
                    }).AsTask();
            }
        }
    }
}


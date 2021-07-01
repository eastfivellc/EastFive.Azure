using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    [BlobRefSerializer]
    public interface IBlobRef
    {
        string ContainerName { get; }

        string Id { get; }

        Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], string, string, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default);
    }

    public interface IDefineBlobContainer
    {
        string ContainerName { get; }
    }

    public class BlobContainerAttribute 
        : System.Attribute, IDefineBlobContainer
    {
        public string ContainerName { get; set; }
    }

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

        public static Task<(byte[], string)> ReadBytesAsync(this IBlobRef blobRef) =>
            blobRef.ReadBytesAsync(
                onSuccess:(bytes, contentType) => (bytes, contentType));

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
            if(newBlobId.IsNullOrWhiteSpace())
                newBlobId = Guid.NewGuid().ToString("N");
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
            Func<bool, byte[], string , string, TResult> onSaved,
            Func<TResult> onCouldNotAccess = default,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            return await await blobRef.LoadAsync(
                async (blobName, bytes, contentType, fileName) =>
                {
                    return await AzureTableDriverDynamic
                        .FromSettings()
                        .BlobCreateOrUpdateAsync(bytes, blobRef.Id, blobRef.ContainerName,
                            () =>
                            {
                                return onSaved(false, bytes, contentType, fileName);
                            },
                            onFailure: onFailure,
                            contentType: contentType);
                },
                onNotFound: onCouldNotAccess.AsAsyncFunc());
        }

        private class BlobRef : IBlobRef
        {
            public string ContainerName { get; set; }

            public string Id { get; set; }

            public string ContentType { get; set; }

            public string FileName { get; set; }

            public Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], string, string, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            {
                throw new NotImplementedException();
            }
        }
    }

    public class BlobRefSerializerAttribute :
        Attribute, ISerialize<IDictionary<string, EntityProperty>>
    {
        private class BlobRef : IBlobRef
        {
            public string ContainerName { get; set; }

            public string Id { get; set; }

            public Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], string, string, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
            {
                var blobName = this.Id;
                return AzureTableDriverDynamic
                    .FromSettings()
                    .BlobLoadBytesAsync(blobName: blobName, containerName: this.ContainerName,
                        (bytes, properties) =>
                        {
                            ContentDispositionHeaderValue.TryParse(
                                properties.ContentDisposition, out ContentDispositionHeaderValue fileName);
                            return onFound(blobName, bytes,
                                properties.ContentType, fileName.FileName);
                        },
                        onNotFound: onNotFound,
                        onFailure: onFailure);
            }
        }

        public TResult Bind<TResult>(IDictionary<string, EntityProperty> value,
                Type type, string path, MemberInfo member,
            Func<object, TResult> onBound, 
            Func<TResult> onFailedToBind)
        {
            var containerName = member.BlobContainerName();

            var id = GetId();
            var blobRef = new BlobRef
            {
                Id = id,
                ContainerName = containerName,
            };
            return onBound(blobRef);

            string GetId()
            {
                if (!value.ContainsKey(path))
                    return default;
                var epValue = value[path];
                if (epValue.PropertyType == EdmType.String)
                    return epValue.StringValue;
                if (epValue.PropertyType == EdmType.Guid)
                {
                    var guidValue = epValue.GuidValue;
                    if(guidValue.HasValue)
                        return guidValue.Value.ToString("N");
                }
                return default;
            }
            
        }

        public TResult Cast<TResult>(object value, 
                Type valueType, string path, MemberInfo member,
            Func<IDictionary<string, EntityProperty>, TResult> onValue, 
            Func<TResult> onNoCast)
        {
            if (value.IsDefaultOrNull())
                return onNoCast();
            if(!(value is IBlobRef))
                return onNoCast();
            var blobRef = value as IBlobRef;
            var ep = new EntityProperty(blobRef.Id);
            var dict = ep.PairWithKey(path).AsArray().ToDictionary();
            return onValue(dict);
        }
    }
}

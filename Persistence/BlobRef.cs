using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    [BlobRefSerializer]
    public interface IBlobRef
    {
        public string ContainerName { get; }
        public string Id { get; } 
    }

    public static class BlobRefExtensions
    {
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

        public static async Task<IBlobRef> SaveAsNewAsync(this IBlobRef blobRef)
        {
            var blobId = Guid.NewGuid().ToString("N");
            var (bytes, contentType) = await blobRef.ReadBytesAsync();
            return await AzureTableDriverDynamic
                .FromSettings()
                .BlobCreateAsync(bytes, blobId, blobRef.ContainerName,
                    () =>
                    {
                        return (IBlobRef)new BlobRef
                        {
                            Id = blobId,
                            ContainerName = blobRef.ContainerName,
                        };
                    },
                    contentType: contentType);
        }

        private class BlobRef : IBlobRef
        {
            public string ContainerName { get; set; }

            public string Id { get; set; }
        }
    }

    public class BlobRefSerializerAttribute :
        Attribute, ISerialize<IDictionary<string, EntityProperty>>
    {
        private class BlobRef : IBlobRef
        {
            public string ContainerName { get; set; }

            public string Id { get; set; }
        }

        public TResult Bind<TResult>(IDictionary<string, EntityProperty> value,
                Type type, string path, MemberInfo member,
            Func<object, TResult> onBound, 
            Func<TResult> onFailedToBind)
        {
            var validCharacters = $"{member.DeclaringType.Name}-{member.Name}"
                .ToLower()
                .Where(c => char.IsLetterOrDigit(c))
                .Take(63)
                .Join();
            var containerName = string.Concat(validCharacters);

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

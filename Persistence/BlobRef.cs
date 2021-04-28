using EastFive.Collections.Generic;
using EastFive.Extensions;
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
        public static Task<byte[]> ReadBytesAsync(this IBlobRef blobRef)
        {
            throw new NotImplementedException();
        }

        public static Task<IBlobRef> WriteBytesAsync(byte[] bytes)
        {
            throw new NotImplementedException();
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

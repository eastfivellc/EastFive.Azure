﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using Newtonsoft.Json;

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

namespace EastFive.Azure.Persistence.Blobs
{
    public class BlobRefSerializerAttribute :
        Attribute, ISerialize<IDictionary<string, EntityProperty>>,
        IDeserializeFromBody<JsonReader>
    {
        public TResult Bind<TResult>(IDictionary<string, EntityProperty> value,
                Type type, string pathStart, MemberInfo member,
            Func<object, TResult> onBound, 
            Func<TResult> onFailedToBind)
        {
            var containerName = member.BlobContainerName();

            var id = GetId(pathStart);
            var blobRef = new BlobRefStorage
            {
                Id = id,
                ContainerName = containerName,
            };
            return onBound(blobRef);

            string GetId(string path)
            {
                if (!value.TryGetValue(path, out EntityProperty epValue))
                {
                    if(!member.TryGetAttributeInterface(out IMigrateBlobIdAttribute migrateBlobId))
                        return default;

                    // terminate recursion
                    if (migrateBlobId.IdName.Equals(path, StringComparison.Ordinal))
                        return default;

                    return GetId(migrateBlobId.IdName);
                }

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

        public object UpdateInstance(string propertyKey,
            JsonReader reader, object instance, ParameterInfo parameterInfo, MemberInfo memberInfo,
            IApplication httpApp, IHttpRequest request)
        {
            var containerName = memberInfo.BlobContainerName();
            var blobRef = GetBlobRef();
            memberInfo.SetPropertyOrFieldValue(instance, blobRef);
            return instance;

            IBlobRef GetBlobRef()
            {
                if (reader.TokenType == JsonToken.Bytes)
                {
                    var bytes = reader.ReadAsBytes();
                    var blobRefRaw = new BlobRefRawData(containerName, bytes);
                    return blobRefRaw;
                }

                if (reader.TokenType != JsonToken.String)
                    throw new ArgumentException($"Cannot parse token of type {reader.TokenType} into BlobRef");

                var blobRefRawStr = (string)reader.Value;

                if (Guid.TryParse(blobRefRawStr, out Guid guidValue))
                {
                    var idGuidStr = guidValue.AsBlobName();
                    return new BlobRefStorage
                    {
                        Id = idGuidStr,
                        ContainerName = containerName,
                    };
                }

                if (Uri.TryCreate(blobRefRawStr, UriKind.Absolute, out Uri rawUri))
                    return new BlobRefUrl(rawUri);

                throw new Exception($"Cannot parse {blobRefRawStr} into {nameof(IBlobRef)}.");
            }
        }
    }
}

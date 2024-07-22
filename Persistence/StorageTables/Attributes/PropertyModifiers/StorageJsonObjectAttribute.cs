using System;
using System.Collections.Generic;
using EastFive.Azure.Auth;
using EastFive.Extensions;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Serialization.Json;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables
{
    // this works for type, nullable type, and array of type
    public class StorageJsonObjectAttribute : StorageAttribute
    {
        public override TResult GetMemberValue<TResult>(Type type, string propertyName,
                IDictionary<string, EntityProperty> allValues,
            Func<object, TResult> onBound,
            Func<TResult> onFailureToBind)
        {
            if (!allValues.TryGetValue(propertyName, out var entityPropertyValue) ||
                entityPropertyValue.StringValue.IsNullOrWhiteSpace())
            {
                if (!type.IsArray)
                {
                    var v = Activator.CreateInstance(type);
                    return onBound(v);
                }

                var elementType = type.GetElementType();
                var val = Array.CreateInstance(elementType, 0);
                return onBound(val);
            }

            var converter =  new EastFive.Serialization.Json.Converter();
            return entityPropertyValue.StringValue.JsonParseObject(type,
                (object v) => onBound(v),
                onFailureToParse: (why) => onFailureToBind(),
                onException: (ex) => onFailureToBind(),
                converters:converter.AsArray());
        }

        public override KeyValuePair<string, EntityProperty>[] CastValue(Type typeOfValue, object values, string propertyName)
        {
            return values.JsonSerialize(
                jsonValue =>
                {
                    return new KeyValuePair<string, EntityProperty>[]
                    {
                        propertyName.PairWithValue(
                            EntityProperty.GeneratePropertyForString(jsonValue))
                    };
                });
        }
    }
}

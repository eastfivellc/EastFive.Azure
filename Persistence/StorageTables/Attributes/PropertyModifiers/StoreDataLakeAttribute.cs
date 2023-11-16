using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Persistence;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Reflection;

using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence.StorageTables
{
    public class StoreDataLakeAttribute : StorageAttribute
    {
        public override TResult GetMemberValue<TResult>(Type type, string propertyName,
                IDictionary<string, EntityProperty> allValues,
            Func<object, TResult> onBound,
            Func<TResult> onFailureToBind)
        {
            if (!type.IsArray)
                return base.GetMemberValue(type, propertyName, allValues, onBound, onFailureToBind);

            if (allValues.TryGetValue(propertyName, out var value))
                return base.GetMemberValue(type, propertyName, allValues, onBound, onFailureToBind);

            var elementType = type.GetElementType();
            var (discard, valuesToBind) = Enumerable
                .Range(0, allValues.Count)
                .Aggregate(
                    (true, new object[] { }),
                    (isActiveValues, index) =>
                    {
                        var (isActive, values) = isActiveValues;
                        if (!isActive)
                            return isActiveValues;

                        var indexKey = GetIndexKey(propertyName, index);
                        if (!allValues.TryGetValue(indexKey, out var entityPropertyValue))
                            return (false, values);

                        return entityPropertyValue.Bind(elementType,
                            onBound: (value) =>
                            {
                                var updatedValues = values.Append(value).ToArray();
                                return (true, updatedValues);
                            },
                            onFailedToBind: () =>
                            {
                                return isActiveValues;
                            });
                    });

            var valuesCasted = valuesToBind.CastArray(elementType);
            return onBound(valuesCasted);
        }

        public string GetIndexKey(string propertyName, int index) =>
            $"{propertyName}_{index}_DL";

        public override KeyValuePair<string, EntityProperty>[] CastValue(Type typeOfValue, object values, string propertyName)
        {
            if (!typeOfValue.IsArray)
                return base.CastValue(typeOfValue, values, propertyName);

            if (values == null)
                return new KeyValuePair<string, EntityProperty>[] { };

            var elementType = typeOfValue.GetElementType();
            return ((IEnumerable)values)
                .Cast<object>()
                .Select(
                    (value, index) =>
                    {
                        var newPropName = GetIndexKey(propertyName, index);
                        var kvps = base.CastValue(elementType, value, newPropName);
                        return kvps;
                    })
                .SelectMany()
                .ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Persistence;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Persistence
{
	public static class StorageExtensions
	{
        public static bool AreStoragePropertiesModified<T>(this T obj, T objectToCompare, bool skipNullAndDefault = false)
        {
            return !obj
                .IsEqualTObjectPropertiesWithAttributeInterface<T, IPersistInAzureStorageTables>(
                    objectToCompare,
                    (memberInfo, attr, val1, val2) =>
                    {
                        var ep1 = attr.ConvertValue<T>(memberInfo, obj, default);
                        var ep2 = attr.ConvertValue<T>(memberInfo, objectToCompare, default);
                        var areEqual = ep1.IsEqualToKeyValuePairArray(ep2, (v1, v2) =>
                            AreEntityPropertiesEqual(v1, v2));
                        return areEqual;
                    },
                    skipNullAndDefault: skipNullAndDefault, inherit:true);
        }

        public static T CopyStoragePropertiesTo<T>(this T obj, T objectToUpdate, bool skipNullAndDefault = false)
        {
            objectToUpdate = obj
                .CloneObjectPropertiesWithAttributeInterface(
                    objectToUpdate, typeof(IPersistInAzureStorageTables),
                    skipNullAndDefault, inherit: true);
            return objectToUpdate;
        }

        public static bool AreEntityPropertiesEqual(EntityProperty property1, EntityProperty property2)
        {
            // Check if both are null
            if (property1 == null && property2 == null)
                return true;
            
            // Check if only one is null
            if (property1 == null || property2 == null)
                return false;
            
            // Check if property types are different
            if (property1.PropertyType != property2.PropertyType)
                return false;
            
            // Compare values based on the property type
            switch (property1.PropertyType)
            {
                case EdmType.Binary:
                    return property1.BinaryValue.SequenceEqual(property2.BinaryValue ?? Array.Empty<byte>());
                    
                case EdmType.Boolean:
                    return property1.BooleanValue == property2.BooleanValue;
                    
                case EdmType.DateTime:
                    return property1.DateTime == property2.DateTime;
                    
                case EdmType.Double:
                    return property1.DoubleValue == property2.DoubleValue;
                    
                case EdmType.Guid:
                    return property1.GuidValue == property2.GuidValue;
                    
                case EdmType.Int32:
                    return property1.Int32Value == property2.Int32Value;
                    
                case EdmType.Int64:
                    return property1.Int64Value == property2.Int64Value;
                    
                case EdmType.String:
                    return string.Equals(property1.StringValue, property2.StringValue, StringComparison.Ordinal);
                    
                default:
                    // For any unknown types, compare using the PropertyAsObject
                    return Equals(property1.PropertyAsObject, property2.PropertyAsObject);
            }
        }
	}
}


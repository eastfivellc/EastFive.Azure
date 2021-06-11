using EastFive.Persistence;
using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Serialization;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Linq;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class StoreSimpleStringArrayAttribute : StorageAttribute
    {
        private bool IsCorrectType(Type type)
        {
            if (!type.IsArray)
                return false;

            return type.GetElementType() == typeof(string);
        }

        public override bool IsMultiProperty(Type type)
        {
            if (IsCorrectType(type))
            {
                return true;
            }
            return false;
        }

        protected override TResult BindEmptyEntityProperty<TResult>(Type type,
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            if (IsCorrectType(type))
            {
                return onBound(new string[] { });
            }
            return base.BindEmptyEntityProperty(type, onBound, onFailedToBind);
        }

        protected override TResult BindEntityProperties<TResult>(string propertyName, Type type, 
            IDictionary<string, EntityProperty> allValues, 
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            if (!IsCorrectType(type))
                return base.BindEntityProperties(propertyName, type, allValues, onBound, onFailedToBind);
            
            if(!allValues.ContainsKey(propertyName))
                return onBound(new string[] { });

            var stringArrayEp = allValues[propertyName];
            if (!stringArrayEp.BinaryValue.AnyNullSafe())
                return onBound(new string[] { });

            var strings = stringArrayEp.BinaryValue.ToStringsFromUTF8ByteArray();

            return onBound(strings);

        }

        public override KeyValuePair<string, EntityProperty>[] CastValue(Type typeOfValue, object value, string propertyName)
        {
            if (!IsCorrectType(typeOfValue))
                return base.CastValue(typeOfValue, value, propertyName);
            
            var valueRect = (string[])value;

            var stringBytes = valueRect.ToUTF8ByteArrayOfStrings();
            var ep = new EntityProperty(stringBytes);
            return ep.PairWithKey(propertyName).AsArray();
        }
    }
}

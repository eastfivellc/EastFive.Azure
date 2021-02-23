using EastFive.Persistence;
using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Linq;

namespace EastFive.Azure.Persistence.StorageTables
{
    public class StoreGraphicsAttribute : StorageAttribute
    {
        public override bool IsMultiProperty(Type type)
        {
            if (type == typeof(RectangleF))
            {
                return true;
            }
            return false;
        }

        protected override TResult BindEmptyEntityProperty<TResult>(Type type,
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            if(type == typeof(RectangleF))
            {
                return onBound(default(RectangleF));
            }
            return base.BindEmptyEntityProperty(type, onBound, onFailedToBind);
        }

        protected override TResult BindEntityProperties<TResult>(string propertyName, Type type, 
            IDictionary<string, EntityProperty> allValues, 
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            if (type == typeof(RectangleF))
            {
                if(!allValues.ContainsKey(propertyName))
                    return onBound(default(RectangleF));
                var rectEp = allValues[propertyName];
                if (!rectEp.BinaryValue.AnyNullSafe())
                    return onBound(default(RectangleF));
                if(rectEp.BinaryValue.Length != sizeof(float) * 4)
                    return onBound(default(RectangleF));
                var x = BitConverter.ToSingle(rectEp.BinaryValue, sizeof(float) * 0);
                var y = BitConverter.ToSingle(rectEp.BinaryValue, sizeof(float) * 1);
                var w = BitConverter.ToSingle(rectEp.BinaryValue, sizeof(float) * 2);
                var h = BitConverter.ToSingle(rectEp.BinaryValue, sizeof(float) * 3);
                var rect = new RectangleF(x, y, w, h);
                return onBound(rect);
            }

            return base.BindEntityProperties(propertyName, type, allValues, onBound, onFailedToBind);
        }

        public override KeyValuePair<string, EntityProperty>[] CastValue(Type typeOfValue, object value, string propertyName)
        {
            if (typeOfValue == typeof(RectangleF))
            {
                var valueRect = (RectangleF)value;
                var xBytes = BitConverter.GetBytes(valueRect.X);
                var yBytes = BitConverter.GetBytes(valueRect.Y);
                var wBytes = BitConverter.GetBytes(valueRect.Width);
                var hBytes = BitConverter.GetBytes(valueRect.Height);

                var rectBytes = xBytes
                    .Concat(yBytes)
                    .Concat(wBytes)
                    .Concat(hBytes)
                    .ToArray();
                var ep = new EntityProperty(rectBytes);
                return ep.PairWithKey(propertyName).AsArray();
            }

            return base.CastValue(typeOfValue, value, propertyName);
        }
    }
}

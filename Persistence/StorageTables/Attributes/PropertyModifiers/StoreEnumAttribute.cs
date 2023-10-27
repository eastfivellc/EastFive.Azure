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
    public class StoreEnumAttribute : StorageAttribute
    {
        public string DefaultValue { get; set; }

        protected override TResult BindEmptyEntityProperty<TResult>(Type type,
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            if(type.IsEnum)
            {
                if (DefaultValue.HasBlackSpace())
                {
                    if (Enum.TryParse(type, DefaultValue, out var value))
                        return onBound(value);
                    throw new ArgumentException($"`{DefaultValue}` is not a valid value for enum of type {type.FullName}");
                }
            }
            return base.BindEmptyEntityProperty(type, onBound, onFailedToBind);
        }
    }
}

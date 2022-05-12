using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Linq.Expressions;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive.Linq;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Serialization;

namespace EastFive.Persistence
{
    public class StorageDateTimeTicksAttribute : StorageAttribute,
        IPersistInAzureStorageTables
    {
        public override KeyValuePair<string, EntityProperty>[] ConvertValue<EntityType>(MemberInfo memberInfo,
            object value, IWrapTableEntity<EntityType> tableEntityWrapper)
        {
            var propertyName = this.GetTablePropertyName(memberInfo);

            var ep = GetEp();
            
            return new KeyValuePair<string, EntityProperty>[]
            {
                propertyName.PairWithValue(ep)
            };

            EntityProperty GetEp()
            {
                if (value == null)
                    return new EntityProperty(default(long?));

                if (value is long)
                    return new EntityProperty((long)value);
                if (value is int)
                    return new EntityProperty((long)((int)value));
                if (value is DateTime)
                    return new EntityProperty(((DateTime)value).Ticks);
                if (value is DateTime?)
                {
                    var dtMaybe = value as DateTime?;
                    if(dtMaybe.HasValue)
                        return new EntityProperty(dtMaybe.Value.Ticks);
                    return new EntityProperty(default(long?));
                }
                throw new ArgumentException(
                    $"{typeof(StorageDateTimeTicksAttribute).FullName} cannot convert {value.GetType().FullName} to an EntityProperty");
            }
        }

        public override object GetMemberValue(MemberInfo memberInfo, IDictionary<string, EntityProperty> values)
        {
            var propertyName = this.GetTablePropertyName(memberInfo);
            var valueType = memberInfo.GetPropertyOrFieldType();
            if (valueType.IsAssignableFrom(typeof(DateTime)))
                return GetValue(default(DateTime));

            if (valueType.IsAssignableFrom(typeof(DateTime?)))
                return GetValue(default(DateTime?));

            object GetValue(object defaultValue)
            {
                if (!values.ContainsKey(propertyName))
                    return defaultValue;
                var ep = values[propertyName];
                if(ep.PropertyType == EdmType.Int64)
                {
                    if (!ep.Int64Value.HasValue)
                        return defaultValue;
                    return new DateTime(ep.Int64Value.Value, DateTimeKind.Utc);
                }
                if (ep.PropertyType == EdmType.Int32)
                {
                    if (!ep.Int32Value.HasValue)
                        return defaultValue;
                    return new DateTime(ep.Int32Value.Value, DateTimeKind.Utc);
                }
                return defaultValue;
            }

            return base.GetMemberValue(memberInfo, values);
        }
    }

}

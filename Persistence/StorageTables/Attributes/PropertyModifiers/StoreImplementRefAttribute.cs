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
    public class StoreImplementRefAttribute : StorageAttribute,
        IPersistInAzureStorageTables
    {
        public Type DefaultType { get; set; }

        private const string TypeKeyAppendix = "_ImplementRefType";

        public override KeyValuePair<string, EntityProperty>[] ConvertValue<EntityType>(MemberInfo memberInfo,
            object value, IWrapTableEntity<EntityType> tableEntityWrapper)
        {
            var propertyName = this.GetTablePropertyName(memberInfo);
            var eps = GetEps();
            return eps;

            KeyValuePair<string, EntityProperty>[] GetEps()
            {
                if (value is null)
                    return new KeyValuePair<string, EntityProperty>[] { };

                if (!value.GetType().IsSubClassOfGeneric(typeof(IImplementRef<>)))
                    throw new ArgumentException(
                    $"{typeof(StoreImplementRefAttribute).FullName} cannot convert {value.GetType().FullName} to an {nameof(EntityProperty)}");

                var type = (Type)memberInfo
                    .GetPropertyOrFieldType()
                    .GetProperty(nameof(IImplementRef<IReferenceable>.type), BindingFlags.Public | BindingFlags.Instance)
                    .GetValue(value);
                var typeEp = new EntityProperty(type.FullName)
                    .PairWithKey(GetTypePropertyName(propertyName));

                var refValue = (IReferenceable)value;
                var id = refValue.id;
                var idEp = new EntityProperty(id)
                    .PairWithKey(propertyName);

                return new[] { idEp, typeEp };
            }
        }

        public override object GetMemberValue(MemberInfo memberInfo,
            IDictionary<string, EntityProperty> values, out bool shouldSkip, Func<object> getDefaultValue = default)
        {
            shouldSkip = this.WriteToStorageOnly;
            var propertyName = this.GetTablePropertyName(memberInfo);
            var valueType = memberInfo.GetPropertyOrFieldType();
            
            var idMaybe = GetIdValue();
            if (!idMaybe.HasValue)
                return null;
            var id = idMaybe.Value;

            if (!TryGetTypeValue(out var typeValue))
            {
                if (this.DefaultType.IsDefaultOrNull())
                    return null;
                typeValue = this.DefaultType;
            }

            var interfaceTypeAndTypeType = valueType.GenericTypeArguments.Append(typeValue).ToArray();
            var implementRefValue = typeof(RefExtensions)
                .GetMethod(nameof(RefExtensions.AsImplementRef), BindingFlags.Static | BindingFlags.Public, typeof(Guid).AsArray())
                .MakeGenericMethod(interfaceTypeAndTypeType)
                .Invoke(null, id.AsArray<object>());
            return implementRefValue;

            Guid? GetIdValue()
            {
                if (!values.ContainsKey(propertyName))
                    return default;

                var epId = values[propertyName];

                if (epId.PropertyType == EdmType.Guid)
                {
                    if (!epId.GuidValue.HasValue)
                        return default;
                    return epId.GuidValue.Value;
                }

                if (epId.PropertyType == EdmType.String)
                {
                    if (epId.StringValue.IsNullOrWhiteSpace())
                        return default;

                    if (Guid.TryParse(epId.StringValue, out Guid id))
                        return id;

                    return default;
                }

                return default;
            }


            bool TryGetTypeValue(out Type typeValue)
            {
                var propertyNameType = GetTypePropertyName(propertyName);
                if (!values.ContainsKey(propertyNameType))
                {
                    typeValue = default;
                    return false;
                }

                var epType = values[propertyNameType];

                if(epType.StringValue.IsNullOrWhiteSpace())
                {
                    typeValue = default;
                    return false;
                }

                try
                {
                    typeValue = Type.GetType(epType.StringValue);
                    if (typeValue.IsDefaultOrNull())
                        return false;
                    return true;
                }
                catch (Exception)
                {
                    typeValue = default;
                    return false;
                }
            }
        }

        private string GetTypePropertyName(string propertyName)
        {
            return $"{propertyName}{TypeKeyAppendix}";
        }
    }

    public class StoreImplementRefTypeAttribute : StorageAttribute,
        IPersistInAzureStorageTables
    {
        public StoreImplementRefTypeAttribute(Type childType)
        {
            this.ChildType = childType;
        }

        public Type ChildType { get; set; }

        public override KeyValuePair<string, EntityProperty>[] ConvertValue<EntityType>(MemberInfo memberInfo,
            object value, IWrapTableEntity<EntityType> tableEntityWrapper)
        {
            var propertyName = this.GetTablePropertyName(memberInfo);
            var ep = GetEp();
            return propertyName.PairWithValue(ep).AsArray();

            EntityProperty GetEp()
            {
                if (value == null)
                    return new EntityProperty(default(Guid?));

                if (value is IReferenceable)
                {
                    var refValue = (IReferenceable)value;
                    return new EntityProperty(refValue.id);
                }
                throw new ArgumentException(
                    $"{typeof(StoreImplementRefTypeAttribute).FullName} cannot convert {value.GetType().FullName} to an {nameof(EntityProperty)}");
            }
        }

        public override object GetMemberValue(MemberInfo memberInfo,
            IDictionary<string, EntityProperty> values, out bool shouldSkip, Func<object> getDefaultValue = default)
        {
            shouldSkip = this.WriteToStorageOnly;
            var propertyName = this.GetTablePropertyName(memberInfo);
            var valueType = memberInfo.GetPropertyOrFieldType();
            var valueMaybe = GetValue();

            if (!valueMaybe.HasValue)
                return null;

            var interfaceTypeAndTypeType = valueType.GenericTypeArguments.Append(this.ChildType).ToArray();
            var implementRefValue = typeof(RefExtensions)
                .GetMethod(nameof(RefExtensions.AsImplementRef), BindingFlags.Static | BindingFlags.Public)
                .MakeGenericMethod(interfaceTypeAndTypeType)
                .Invoke(null, valueMaybe.Value.AsArray<object>());
            return implementRefValue;

            Guid? GetValue()
            {
                if (!values.ContainsKey(propertyName))
                    return default;

                var ep = values[propertyName];

                if (ep.PropertyType == EdmType.Guid)
                {
                    if (!ep.GuidValue.HasValue)
                        return default;
                    return ep.GuidValue.Value;
                }

                if (ep.PropertyType == EdmType.String)
                {
                    if (!Guid.TryParse(ep.StringValue, out Guid result))
                        return default;

                    return result;
                }

                return default;
            }

        }
    }

}

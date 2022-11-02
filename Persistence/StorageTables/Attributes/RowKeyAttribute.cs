using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Extensions;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class RowKeyAttribute : Attribute,
        IModifyAzureStorageTableRowKey, IComputeAzureStorageTableRowKey, IGenerateAzureStorageTableRowKeyIndex
    {
        public bool StoresFullValue
        {
            get; set;
        } = true;

        public virtual string ComputeRowKey(object memberValue, MemberInfo memberInfo)
        {
            if (memberValue.IsDefaultOrNull())
                return null;
            var propertyValueType = memberValue.GetType(); // memberInfo.GetMemberType();
            if (typeof(Guid).IsAssignableFrom(propertyValueType))
            {
                var guidValue = (Guid)memberValue;
                return guidValue.AsRowKey();
            }
            if (typeof(IReferenceable).IsAssignableFrom(propertyValueType))
            {
                var refValue = (IReferenceable)memberValue;
                return refValue.id.AsRowKey();
            }
            if (typeof(IReferenceableOptional).IsAssignableFrom(propertyValueType))
            {
                var refValue = (IReferenceableOptional)memberValue;
                if (!refValue.HasValue)
                    return null;
                return refValue.id.Value.AsRowKey();
            }
            if (typeof(string).IsAssignableFrom(propertyValueType))
            {
                var stringValue = (string)memberValue;
                var rowKeyValidatedString = stringValue.AsAzureStorageTablesSafeKey();
                return rowKeyValidatedString;
            }
            var message = $"`{this.GetType().FullName}` Cannot determine row key from type `{propertyValueType.FullName}` on `{memberInfo.DeclaringType.FullName}..{memberInfo.Name}`";
            throw new NotImplementedException(message);
        }

        public string GenerateRowKeyIndex(MemberInfo member)
            => GenerateRowKeyIndexEx(member, this.GetType());

        public static string GenerateRowKeyIndexEx(MemberInfo member, Type callingAttrType)
        {
            var propertyValueType = member.GetPropertyOrFieldType();

            if (typeof(Guid).IsAssignableTo(propertyValueType))
                return Guid.NewGuid().AsRowKey();

            if (typeof(IReferenceable).IsAssignableFrom(propertyValueType))
                return Guid.NewGuid().AsRowKey();

            if (typeof(IReferenceableOptional).IsAssignableFrom(propertyValueType))
                return Guid.NewGuid().AsRowKey();

            if (typeof(string).IsAssignableTo(propertyValueType))
                return System.IO.Path
                    .GetRandomFileName()
                    .Replace(".", "");

            var message = $"`{callingAttrType.FullName}` Cannot generate index of type `{propertyValueType.FullName}` on `{member.DeclaringType.FullName}..{member.Name}`";
            throw new NotImplementedException(message);
        }

        public virtual string GenerateRowKey(object value, MemberInfo memberInfo)
        {
            var memberValue = memberInfo.GetValue(value);
            return ComputeRowKey(memberValue, memberInfo);
        }

        EntityType IModifyAzureStorageTableRowKey.ParseRowKey<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            if (!StoresFullValue)
                return entity;

            var memberType = memberInfo.GetMemberType();
            if (typeof(Guid).IsAssignableFrom(memberType))
            {
                if (Guid.TryParse(value, out Guid guidValue))
                {
                    memberInfo.SetValue(ref entity, guidValue);
                    return entity;
                }
            }
            if (memberType.IsSubClassOfGeneric(typeof(IRef<>)))
            {
                if (Guid.TryParse(value, out Guid guidValue))
                {
                    var refdType = memberType.GenericTypeArguments.First();
                    var genericType = typeof(Ref<>).MakeGenericType(refdType);
                    var refValue = Activator.CreateInstance(genericType, new object[] { guidValue });
                    memberInfo.SetValue(ref entity, refValue);
                    return entity;
                }

                // don't want to throw an exception for bad data, just don't use the bad data.
                return entity;
            }
            if (memberType.IsAssignableFrom(typeof(string)))
            {
                memberInfo.SetValue(ref entity, value);
                return entity;
            }
            var message = $"`{this.GetType().FullName}` Cannot determine row key from type `{memberType.FullName}` on `{memberInfo.DeclaringType.FullName}..{memberInfo.Name}`";
            throw new NotImplementedException(message);
        }
    }
}

using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Linq.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class ETagAttribute : Attribute,
        IModifyAzureStorageTableETag
    {
        public string GenerateETag(object value, MemberInfo memberInfo)
        {
            var eTagValue = memberInfo.GetValue(value);
            var eTagType = memberInfo.GetMemberType();
            if (typeof(string).IsAssignableFrom(eTagType))
            {
                var stringValue = (string)eTagValue;
                return stringValue;
            }
            var message = $"`{this.GetType().FullName}` Cannot determine eTag from type `{eTagType.FullName}` on `{memberInfo.DeclaringType.FullName}..{memberInfo.Name}`";
            throw new NotImplementedException(message);
        }

        public EntityType ParseETag<EntityType>(EntityType entity, string value, MemberInfo memberInfo)
        {
            var memberType = memberInfo.GetMemberType();
            if (memberType.IsAssignableFrom(typeof(string)))
            {
                memberInfo.SetValue(ref entity, value);
                return entity;
            }
            var message = $"`{this.GetType().FullName}` Cannot determine eTag from type `{memberType.FullName}` on `{memberInfo.DeclaringType.FullName}..{memberInfo.Name}`";
            throw new NotImplementedException(message);
        }
    }
}

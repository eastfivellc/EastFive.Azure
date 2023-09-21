using EastFive.Extensions;
using EastFive.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class ScopeTypeAttribute : ScopeStringAttribute
    {
        public ScopeTypeAttribute(string scope) : base(scope)
        {
        }

        protected override string GetValue(MemberInfo memberInfo, object memberValue)
        {
            var propertyValueType = memberInfo.GetMemberType();
            if (typeof(Type).IsAssignableFrom(propertyValueType))
            {
                var typeValue = (Type)memberValue;
                return typeValue.FullName;
            }

            var exMsg = $"{memberInfo.DeclaringType.FullName}..{memberInfo.Name} is scoping on a Type,"
                + " but is of type `{propertyValueType.FullName}`.";
            throw new NotImplementedException(exMsg);
        }
    
    }
}

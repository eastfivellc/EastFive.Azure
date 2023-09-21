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
    public class ScopeIdAttribute : Attribute, IScopeKeys
    {
        public string Scope { get; private set; }

        public double Order { get; set; } = 0.0;

        public bool IgnoreNullOrDefault { get; set; } = false;

        public string Separator { get; set; } = "";

        public string MutateKey(string currentKey, MemberInfo key, object value, out bool ignore)
        {
            ignore = false;
            var idValue = IdLookupAttribute.RowKey(this.GetType(), key.GetPropertyOrFieldType(), value);
            if (idValue.IsNullOrWhiteSpace())
                ignore = true;
            if(Separator.IsNotDefaultOrNull())
                return $"{currentKey}{Separator}{idValue}";
            return $"{currentKey}{idValue}";
        }

        public ScopeIdAttribute(string scope)
        {
            this.Scope = scope;
        }
    }
}

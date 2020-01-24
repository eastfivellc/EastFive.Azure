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
    public class ScopeIdAttribute : Attribute, IScopeKeys
    {
        public string Scope { get; private set; }

        public double Order { get; set; } = 0.0;

        public string MutateKey(string currentKey, MemberInfo key, object value, out bool ignore)
        {
            ignore = false;
            var idValue = IdLookupAttribute.RowKey(this.GetType(), key.GetPropertyOrFieldType(), value);
            return $"{currentKey}{idValue}";
        }

        public ScopeIdAttribute(string scope)
        {
            this.Scope = scope;
        }
    }

    public class ScopeId1Attribute : ScopeIdAttribute
    {
        public ScopeId1Attribute(string scope) : base(scope)
        {
        }
    }
}

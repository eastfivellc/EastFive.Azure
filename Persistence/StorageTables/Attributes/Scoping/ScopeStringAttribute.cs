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
    public class ScopeStringAttribute : Attribute, IScopeKeys
    {
        public string Scope { get; private set; }

        public double Order { get; set; } = 0.0;

        public bool IgnoreScopeIfNull { get; set; } = true;

        public bool KeyFilter { get; set; } = false;

        public string MutateKey(string currentKey, MemberInfo key, object value, out bool ignore)
        {
            if (value == null)
            {
                if (IgnoreScopeIfNull)
                {
                    ignore = true;
                    return currentKey;
                }
            }
            ignore = false;
            var stringValue = StringLookupAttribute.GetStringValue(key, value, this.GetType());

            if (KeyFilter)
                stringValue = stringValue
                    .Replace('/', '_')
                    .Replace('\\', '_')
                    .Replace('#', '_')
                    .Replace('?', '_')
                    .Replace('\t', '_')
                    .Replace('\n', '_')
                    .Replace('\r', '_');

            return $"{currentKey}{stringValue}";
        }

        public ScopeStringAttribute(string scope)
        {
            this.Scope = scope;
        }
    }

    public class ScopeString1Attribute : ScopeStringAttribute
    {
        public ScopeString1Attribute(string scope) : base(scope)
        {
        }
    }
}

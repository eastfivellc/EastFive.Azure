using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace EastFive.Persistence.Azure.StorageTables
{
    internal static class InternalExtensions
    {
        public static readonly Regex DisallowedCharsInTableKeys = new Regex(@"[\\\\#%+/?\u0000-\u001F\u007F-\u009F]");

        internal static void SetFieldOrProperty(this object document, bool value, FieldInfo fieldInfo, PropertyInfo propertyInfo)
        {
            if (fieldInfo != null) fieldInfo.SetValue(document, false);
            else propertyInfo?.SetValue(document, false);
        }

        internal static string AsAzureStorageTablesSafeKey(this string keyValueRow)
        {
            string sanitizedKey = DisallowedCharsInTableKeys.Replace(keyValueRow, "_");
            return sanitizedKey;
        }
    }
}

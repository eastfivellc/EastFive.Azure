using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using EastFive.Azure.Persistence;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Extensions;
using EastFive.Linq.Expressions;
using EastFive.Serialization;

namespace EastFive.Persistence.Azure.StorageTables
{
    public abstract class UrlLookupAttribute : StorageLookupAttribute, IGenerateLookupKeys
    {
        public UriComponents Components { get; set; }

        public bool ShouldHashRowKey { get; set; }

        public override IEnumerable<IRefAst> GetLookupKeys(MemberInfo decoratedMember,
            IEnumerable<KeyValuePair<MemberInfo, object>> lookupValues)
        {
            if (lookupValues.Count() != 1)
                throw new ArgumentException("IdLookupAttribute only supports operations on a single member.", "lookupValues");

            var lookupValue = lookupValues.Single();
            var rowKeyValue = lookupValue.Value;
            var rowKey = GetStringValue(decoratedMember, rowKeyValue, 
                this.GetType(), this.Components, this.ShouldHashRowKey);
            if (rowKey.IsNullOrWhiteSpace())
                return Enumerable.Empty<IRefAst>();
            var partitionKey = GetPartitionKey(rowKey);
            return new RefAst(rowKey, partitionKey).AsEnumerable();
        }

        internal static string GetStringValue(MemberInfo memberInfo, object memberValue, 
            Type thisAttributeType, UriComponents components, bool shouldHasRowKey)
        {
            var propertyValueType = memberInfo.GetMemberType();
            if (typeof(Uri).IsAssignableFrom(propertyValueType))
            {
                var url = (Uri)memberValue;
                if (url.IsDefaultOrNull())
                    return null;
                var rowKeyUnsanitized = GetUnsanitized(url);
                var rowKeyBytes = rowKeyUnsanitized.GetBytes(System.Text.Encoding.UTF8);
                if (shouldHasRowKey)
                    return rowKeyBytes.MD5HashGuid().ToString("N");
                var stringValue = Convert
                    .ToBase64String(rowKeyBytes, Base64FormattingOptions.None)
                    .Replace('/', '_');
                return stringValue;
            }
            var exMsg = $"{thisAttributeType.GetType().Name} is not implemented for type `{propertyValueType.FullName}`. " +
                $"Please override GetRowKeys on `{thisAttributeType.GetType().FullName}`.";
            throw new NotImplementedException(exMsg);

            string GetUnsanitized(Uri url)
            {
                if (url.IsAbsoluteUri)
                    return url.GetComponents(components, UriFormat.Unescaped);

                return url.OriginalString;
                //var sb = new StringBuilder();
                //if ((components | UriComponents.Path) != 0)
                //    sb.Append(url.LocalPath);
                //if ((components | UriComponents.Query) != 0)
                //    sb.Append(url.Query);
                //if ((components | UriComponents.Fragment) != 0)
                //    sb.Append(url.Fragment);

                //return sb.ToString();
            }
        }

        protected override PropertyLookupInformation GetInfo(StorageLookupTable slt)
        {
            var propInfo = base.GetInfo(slt);
            var rowKeyBytes = Convert.FromBase64String(slt.rowKey.Replace('_', '/'));
            var stringValue = rowKeyBytes.GetString(System.Text.Encoding.UTF8);
            propInfo.value = stringValue;
            return propInfo;
        }

        public abstract string GetPartitionKey(string rowKey);
    }

    public class UrlMD5LookupAttribute : UrlLookupAttribute
    {
        private uint? charactersMaybe;
        public uint Characters
        {
            get
            {
                if (!charactersMaybe.HasValue)
                    return 2;
                return charactersMaybe.Value;
            }
            set
            {
                charactersMaybe = value;
            }
        }

        public override string GetPartitionKey(string rowKey)
        {
            var hash = rowKey.MD5HashHex();
            return RowKeyPrefixAttribute.GetValue(hash, this.Characters);
        }

    }


}

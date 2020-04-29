using BlackBarLabs.Persistence.Azure;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Serialization;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Linq;

namespace EastFive.Persistence
{
    public class StorageOverflowAttribute : StorageAttribute,
        IPersistInAzureStorageTables
    {
        private const string overflowToken = "8d40521b-7d71-47b3-92c5-46e4a804e7de";
        private const string overflowTokenString = "9a9a2e13d0ed44d7aa39c2549aff176a";

        public override KeyValuePair<string, EntityProperty>[] ConvertValue(object value, MemberInfo memberInfo)
        {
            var propertyName = this.GetTablePropertyName(memberInfo);
            var valueType = memberInfo.GetPropertyOrFieldType();

            return CastValue(valueType, value, propertyName)
                .SelectMany(
                    propNameStorageValueKvp =>
                    {
                        var propName = propNameStorageValueKvp.Key;
                        var storageValue = propNameStorageValueKvp.Value;
                        if(storageValue.PropertyType == EdmType.Binary)
                        {
                            if (storageValue.BinaryValue.Length <= 0x10000)
                                return propNameStorageValueKvp.AsArray();

                            var overflowTokenBytes = new Guid(overflowToken).ToByteArray();
                            return storageValue.BinaryValue
                                .Split(x => 0x10000)
                                .Select(
                                    (bytes, index) =>
                                    {
                                        var bytesArr = bytes.ToArray();
                                        var storageValueSized = new EntityProperty(bytesArr);
                                        var propNameIndexed = $"{propName}_overflow_{index}";
                                        return propNameIndexed.PairWithValue(storageValueSized);
                                    })
                                .Append(propName.PairWithValue(new EntityProperty(overflowTokenBytes)));
                        }
                        if (storageValue.PropertyType == EdmType.String)
                        {
                            if (storageValue.StringValue.Length <= 0x8000)
                                return propNameStorageValueKvp.AsArray();

                            return storageValue.StringValue
                                .Split(x => 0x8000)
                                .Select(
                                    (chars, index) =>
                                    {
                                        var stringValue = new String(chars.ToArray());
                                        var storageValueSized = new EntityProperty(stringValue);
                                        var propNameIndexed = $"{propName}_overflow_{index}";
                                        return propNameIndexed.PairWithValue(storageValueSized);
                                    })
                                .Append(propName.PairWithValue(new EntityProperty(overflowTokenString)));
                        }

                        return propNameStorageValueKvp.AsArray();
                    })
                .ToArray();

        }

        public override object GetMemberValue(MemberInfo memberInfo, IDictionary<string, EntityProperty> values)
        {
            var propertyName = this.GetTablePropertyName(memberInfo);

            var compactedValues = values
                .Select(
                    propNameStorageValueKvp =>
                    {
                        var propName = propNameStorageValueKvp.Key;
                        var storageValue = propNameStorageValueKvp.Value;

                        if (storageValue.PropertyType == EdmType.Binary)
                        {
                            var overflowTokenBytes = new Guid(overflowToken).ToByteArray();

                            if (!storageValue.BinaryValue.SequenceEqual(overflowTokenBytes))
                                return propNameStorageValueKvp;

                            var compactedBytes = values
                                .Where(value => value.Value.PropertyType == EdmType.Binary)
                                .Where(value => value.Key.StartsWith($"{propName}_overflow_"))
                                .OrderBy(value => value.Key)
                                .Aggregate(new byte[] { },
                                    (bytes, valueKvp) => bytes.Concat(valueKvp.Value.BinaryValue).ToArray());
                            return propName.PairWithValue(new EntityProperty(compactedBytes));
                        }
                        if (storageValue.PropertyType == EdmType.String)
                        {
                            if (storageValue.StringValue != overflowTokenString)
                                return propNameStorageValueKvp;

                            var stringBuilder = values
                                .Where(value => value.Value.PropertyType == EdmType.String)
                                .Where(value => value.Key.StartsWith($"{propName}_overflow_"))
                                .OrderBy(value => value.Key)
                                .Aggregate(new StringBuilder(),
                                    (sb, valueKvp) => sb.Append(valueKvp.Value.StringValue));
                            return propName.PairWithValue(new EntityProperty(stringBuilder.ToString()));
                        }
                        return propNameStorageValueKvp;
                    })
                .ToDictionary();

            return base.GetMemberValue(memberInfo, compactedValues);
        }
    }

}

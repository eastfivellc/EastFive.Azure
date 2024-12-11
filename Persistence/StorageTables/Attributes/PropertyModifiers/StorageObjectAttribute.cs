using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Serialization;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables;

namespace EastFive.Persistence
{
    public class StorageObjectAttribute : StorageAttribute,
        IPersistInAzureStorageTables
    {
        public override KeyValuePair<string, EntityProperty>[] ConvertValue<EntityType>(MemberInfo memberInfo,
            object value, IWrapTableEntity<EntityType> tableEntityWrapper)
        {
            var propertyName = this.GetTablePropertyName(memberInfo);

            var valueType = memberInfo.GetPropertyOrFieldType();
            return CastValue(valueType, value, propertyName);
        }

        /// <summary>
        /// Will this type be stored in a single EntityProperty or across multiple entity properties.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public override bool IsMultiProperty(Type type)
        {
            if (base.IsMultiProperty(type))
                return true;

            if (type.IsClass)
                return true;

            return false;
        }

        public override EntityProperty CastEntityPropertyEmpty(Type valueType)
        {
            if (valueType.IsAssignableFrom(typeof(string)))
                return new EntityProperty(default(string));
            if (valueType.IsAssignableFrom(typeof(bool)))
                return new EntityProperty(default(bool));
            if (valueType.IsAssignableFrom(typeof(Guid)))
                return new EntityProperty(default(Guid));
            if (valueType.IsAssignableFrom(typeof(Type)))
                return new EntityProperty(default(string));
            if (valueType.IsAssignableFrom(typeof(IReferenceable)))
                return new EntityProperty(default(Guid));
            if (valueType.IsAssignableFrom(typeof(IReferenceableOptional)))
                return new EntityProperty(default(Guid?));
            return new EntityProperty(default(byte[]));
        }

        public override object GetMemberValue(MemberInfo memberInfo,
            IDictionary<string, EntityProperty> values, out bool shouldSkip, Func<object> getDefaultValue = default)
        {
            shouldSkip = this.WriteToStorageOnly;
            var propertyName = this.GetTablePropertyName(memberInfo);

            var type = memberInfo.GetPropertyOrFieldType();

            return GetMemberValue(type, propertyName, values,
                (convertedValue) => convertedValue,
                    () =>
                    {
                        var exceptionText = $"Could not deserialize value for {memberInfo.DeclaringType.FullName}..{memberInfo.Name}[{type.FullName}]" +
                            $"Please override StoragePropertyAttribute's BindEntityProperties for type:{type.FullName}";
                        throw new Exception(exceptionText);
                    });
        }

        public override TResult GetMemberValue<TResult>(Type type, string propertyName, IDictionary<string, EntityProperty> values,
            Func<object, TResult> onBound,
            Func<TResult> onFailureToBind)
        {
            if (IsMultiProperty(type))
                return BindEntityProperties(propertyName, type, values,
                    onBound,
                    onFailureToBind);

            if (!values.ContainsKey(propertyName))
                return BindEmptyEntityProperty(type,
                    onBound,
                    onFailureToBind);

            var value = values[propertyName];
            return value.Bind(type,
                onBound,
                onFailureToBind);

        }

        #region Multi-entity serialization

        protected override TResult BindEntityProperties<TResult>(string propertyName, Type type,
                IDictionary<string, EntityProperty> allValues,
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            if (type.IsSubClassOfGeneric(typeof(IDictionary<,>)))
            {
                // TODO: Actually map values
                var keyType = type.GenericTypeArguments[0];
                var valueType = type.GenericTypeArguments[1];
                var instantiatableType = typeof(Dictionary<,>)
                    .MakeGenericType(keyType, valueType);

                var keysPropertyName = $"{propertyName}__keys";
                var valuesPropertyName = $"{propertyName}__values";

                var refOpt = (IDictionary)Activator.CreateInstance(instantiatableType, new object[] { });

                bool ContainsKeys()
                {
                    if (!allValues.ContainsKey(keysPropertyName))
                        return false;
                    if (!allValues.ContainsKey(valuesPropertyName))
                        return false;
                    return true;
                }
                if (!ContainsKeys())
                {
                    // return empty set
                    return onBound(refOpt);
                }

                var keyArrayType = Array.CreateInstance(keyType, 0).GetType();
                var valueArrayType = Array.CreateInstance(valueType, 0).GetType();
                return allValues[keysPropertyName].Bind(keyArrayType,
                    (keyValues) => allValues[valuesPropertyName].Bind(valueArrayType,
                        (propertyValues) =>
                        {
                            var keyEnumerable = keyValues as System.Collections.IEnumerable;
                            var keyEnumerator = keyEnumerable.GetEnumerator();
                            var propertyEnumerable = propertyValues as System.Collections.IEnumerable;
                            var propertyEnumerator = propertyEnumerable.GetEnumerator();

                            //IDictionary<int, string> x;
                            //x.Add(1, "");
                            //var addMethod = typeof(Dictionary<,>)
                            //    .GetMethods()
                            //    .Where(method => method.Name == "Add")
                            //    .Where(method => method.GetParameters().Length == 2)
                            //    .Single();

                            while (keyEnumerator.MoveNext())
                            {
                                if (!propertyEnumerator.MoveNext())
                                    return onBound(refOpt);
                                var keyValue = keyEnumerator.Current;
                                var propertyValue = propertyEnumerator.Current;
                                refOpt.Add(keyValue, propertyValue);
                            }
                            return onBound(refOpt);

                        },
                        onFailedToBind),
                    onFailedToBind);
            }

            if (type.IsSubClassOfGeneric(typeof(KeyValuePair<,>)))
            {
                var keyType = type.GenericTypeArguments[0];
                var valueType = type.GenericTypeArguments[1];
                var instantiatableType = typeof(KeyValuePair<,>)
                    .MakeGenericType(keyType, valueType);

                var keyPropertyName = $"{propertyName}__key";
                var valuePropertyName = $"{propertyName}__value";

                bool ContainsKeys()
                {
                    if (!allValues.ContainsKey(keyPropertyName))
                        return false;
                    if (!allValues.ContainsKey(valuePropertyName))
                        return false;
                    return true;
                }
                if (!ContainsKeys())
                {
                    // return empty set
                    return onBound(instantiatableType.GetDefault());
                }

                return allValues[keyPropertyName].Bind(keyType,
                    (keyValue) =>
                    {
                        return allValues[valuePropertyName].Bind(keyType,
                            (valueValue) =>
                            {
                                var refOpt = Activator.CreateInstance(instantiatableType, new object[] { keyValue, valueValue });
                                return onBound(refOpt);
                            },
                            onFailedToBind);
                    },
                    onFailedToBind);
            }

            if (type.IsArray)
            {
                var arrayType = type.GetElementType();
                var storageMembersArray = arrayType.GetPersistenceAttributes();
                if (storageMembersArray.Any())
                {
                    var arrayEps = BindArrayEntityProperties(propertyName, arrayType,
                        storageMembersArray, allValues);
                    return onBound(arrayEps);
                }
                if(arrayType.IsSubClassOfGeneric(typeof(KeyValuePair<,>)))
                {
                    return BindArrayKvpEntityProperties(propertyName, arrayType, allValues,
                        onBound,
                        onFailedToBind);
                }
            }

            var storageMembers = type.GetPersistenceAttributes();
            if (storageMembers.Any())
            {
                var value = Activator.CreateInstance(type);
                foreach (var storageMemberKvp in storageMembers)
                {
                    var attr = storageMemberKvp.Value.First();
                    var member = storageMemberKvp.Key;
                    var objPropName = attr.GetTablePropertyName(member);
                    var propName = $"{propertyName}__{objPropName}";
                    if (!allValues.ContainsKey(propName))
                        continue;

                    var entityProperties = allValues[propName].PairWithKey(objPropName)
                        .AsArray()
                        .ToDictionary();
                    var propertyValue = attr.GetMemberValue(member, entityProperties, out var shouldSkip);
                    if(!shouldSkip)
                        member.SetValue(ref value, propertyValue);
                }

                return onBound(value);
            }

            return onFailedToBind();
        }

        public override TResult CastEntityProperties<TResult>(object value, Type valueType,
            Func<KeyValuePair<string, EntityProperty>[], TResult> onValues,
            Func<TResult> onNoCast)
        {
            if (valueType.IsSubClassOfGeneric(typeof(IDictionary<,>)))
            {
                var keysType = valueType.GenericTypeArguments[0];
                var valuesType = valueType.GenericTypeArguments[1];
                var kvps = value
                    .DictionaryKeyValuePairs();
                var keyValues = kvps
                    .SelectKeys()
                    .CastArray(keysType);
                var valueValues = kvps
                    .SelectValues()
                    .CastArray(valuesType);

                var keysArrayType = keysType.MakeArrayType();
                var keyEntityProperties = CastValue(keysArrayType, keyValues, "keys");
                var valuesArrayType = valuesType.MakeArrayType();
                var valueEntityProperties = CastValue(valuesArrayType, valueValues, "values");

                var entityProperties = keyEntityProperties.Concat(valueEntityProperties).ToArray();
                return onValues(entityProperties);
            }

            if (valueType.IsSubClassOfGeneric(typeof(KeyValuePair<,>)))
            {
                var kvpKeyType = valueType.GenericTypeArguments[0];
                var kvpValueType = valueType.GenericTypeArguments[1];
                var keyValue = valueType.GetProperty("Key").GetValue(value);
                var valueValue = valueType.GetProperty("Value").GetValue(value);

                var keyEntityProperties = CastValue(kvpKeyType, keyValue, "key");
                var valueEntityProperties = CastValue(kvpKeyType, keyValue, "value");

                var entityProperties = keyEntityProperties
                    .Concat(valueEntityProperties)
                    .ToArray();
                return onValues(entityProperties);
            }

            if (valueType.IsArray)
            {
                var arrayType = valueType.GetElementType();
                var peristenceAttrs = arrayType.GetPersistenceAttributes();
                if (peristenceAttrs.Any())
                {
                    var epsArray = CastArrayEntityProperties(value, peristenceAttrs);
                    return onValues(epsArray);
                }
                if (arrayType.IsSubClassOfGeneric(typeof(KeyValuePair<,>)))
                {
                    return CastArrayKvpEntityProperties(value, arrayType,
                        onValues,
                        onNoCast);
                }
            }

            var storageMembers = valueType.GetPersistenceAttributes();
            if (storageMembers.Any())
            {
                var storageArrays = storageMembers
                    .Select(
                        storageMemberKvp =>
                        {
                            var attr = storageMemberKvp.Value.First();
                            var member = storageMemberKvp.Key;
                            var propName = attr.GetTablePropertyName(member);
                            var memberType = member.GetPropertyOrFieldType();

                            var v = member.GetValue(value);
                            var epValue = v.CastEntityProperty(memberType,
                                ep => ep,
                                () => new EntityProperty(new byte[] { }));

                            return epValue.PairWithKey(propName);
                        })
                    .ToArray();
                return onValues(storageArrays);
            }

            return onNoCast();
        }

        #endregion

        protected override TResult BindEmptyEntityProperty<TResult>(Type type,
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            return base.BindEmptyEntityProperty(type,
                onBound,
                () =>
                {
                    if (type.IsClass)
                        return onBound(null);

                    return onFailedToBind();
                });
        }

    }

}

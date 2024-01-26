using EastFive.Azure.Persistence.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;
using EastFive.Serialization;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public static class EntityPropertyExtensions
    {
        private static Dictionary<EdmType, byte> typeBytes = new Dictionary<EdmType, byte>
            {
                { EdmType.Binary, 1 },
                { EdmType.Boolean, 2 },
                { EdmType.DateTime, 3 },
                { EdmType.Double, 4 },
                { EdmType.Guid, 5 },
                { EdmType.Int32, 6 },
                { EdmType.Int64, 7},
                { EdmType.String, 8 },
            };

        public static byte[] ToByteArrayOfEntityProperties(this IEnumerable<EntityProperty> entityProperties)
        {
            return entityProperties
                .ToByteArray(
                    ep =>
                    {
                        var epBytes = ToByteArrayOfEntityProperty(ep);
                        var typeByte = typeBytes[ep.PropertyType];
                        var compositeBytes = typeByte.AsArray().Concat(epBytes).ToArray();
                        return compositeBytes;
                    });
        }

        public static byte[] ToByteArrayOfEntityProperty(this EntityProperty ep)
        {
            switch (ep.PropertyType)
            {
                case EdmType.Binary:
                    {
                        if (ep.BinaryValue.IsDefaultOrNull())
                            return new byte[] { };
                        return ep.BinaryValue;
                    }
                case EdmType.Boolean:
                    {
                        if (!ep.BooleanValue.HasValue)
                            return new byte[] { };
                        var epValue = ep.BooleanValue.Value;
                        var boolByte = epValue ? (byte)1 : (byte)0;
                        return boolByte.AsArray();
                    }
                case EdmType.DateTime:
                    {
                        if (!ep.DateTime.HasValue)
                            return new byte[] { };
                        var dtValue = ep.DateTime.Value;
                        return BitConverter.GetBytes(dtValue.Ticks);
                    }
                case EdmType.Double:
                    {
                        if (!ep.DoubleValue.HasValue)
                            return new byte[] { };
                        var epValue = ep.DoubleValue.Value;
                        return BitConverter.GetBytes(epValue);
                    }
                case EdmType.Guid:
                    {
                        if (!ep.GuidValue.HasValue)
                            return new byte[] { };
                        var epValue = ep.GuidValue.Value;
                        return epValue.ToByteArray();
                    }
                case EdmType.Int32:
                    {
                        if (!ep.Int32Value.HasValue)
                            return new byte[] { };
                        var epValue = ep.Int32Value.Value;
                        return BitConverter.GetBytes(epValue);
                    }
                case EdmType.Int64:
                    {
                        if (!ep.Int64Value.HasValue)
                            return new byte[] { };
                        var epValue = ep.Int64Value.Value;
                        return BitConverter.GetBytes(epValue);
                    }
                case EdmType.String:
                    {
                        if (null == ep.StringValue)
                            return new byte[] { 1 };
                        if (string.Empty == ep.StringValue)
                            return new byte[] { 2 };

                        return (new byte[] { 0 }).Concat(Encoding.UTF8.GetBytes(ep.StringValue)).ToArray();
                    }
            }
            throw new Exception($"Unrecognized EdmType {ep.PropertyType}");
        }

        public static object[] FromEdmTypedByteArray(this byte[] binaryValue, Type typeForDefaultFromMissingValues)
        {
            var typeFromByte = typeBytes
                .Select(kvp => kvp.Key.PairWithKey(kvp.Value))
                .ToDictionary();
            var arrOfObj = binaryValue
                .FromByteArray()
                .Select(
                    typeWithBytes =>
                    {
                        if (!typeWithBytes.Any())
                            return typeForDefaultFromMissingValues.GetDefault();
                        var typeByte = typeWithBytes[0];
                        if(!typeFromByte.ContainsKey(typeByte))
                            return typeForDefaultFromMissingValues.GetDefault();
                        var edmType = typeFromByte[typeByte];
                        var valueBytes = typeWithBytes.Skip(1).ToArray();
                        var valueObj = edmType.ToObjectFromEdmTypeByteArray(valueBytes);
                        if(null == valueObj)
                        {
                            if (typeForDefaultFromMissingValues.IsClass)
                                return valueObj;
                            return typeForDefaultFromMissingValues.GetDefault();
                        }
                        if (!typeForDefaultFromMissingValues.IsAssignableFrom(valueObj.GetType()))
                            return typeForDefaultFromMissingValues.GetDefault(); // TODO: Data corruption?
                        return valueObj;
                    })
                .ToArray();
            return arrOfObj;
        }

        public static EntityProperty[] FromEdmTypedByteArrayToEntityProperties(this byte[] binaryValue, Type typeForDefaultFromMissingValues)
        {
            var typeFromByte = typeBytes
                .Select(kvp => kvp.Key.PairWithKey(kvp.Value))
                .ToDictionary();
            var arrOfObj = binaryValue
                .FromByteArray()
                .Select(
                    typeWithBytes =>
                    {
                        if (!typeWithBytes.Any())
                            return NullValue();
                        var typeByte = typeWithBytes[0];
                        if (!typeFromByte.ContainsKey(typeByte))
                            return NullValue();
                        var edmType = typeFromByte[typeByte];
                        var valueBytes = typeWithBytes.Skip(1).ToArray();
                        var valueObj = edmType.ToObjectFromEdmTypeByteArray(valueBytes);
                        if (null == valueObj)
                        {
                            return NullValue();
                        }

                        if (typeof(byte[]).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((byte[])valueObj);
                        if (typeof(string).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((string)valueObj);
                        if (typeof(bool).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((bool)valueObj);
                        if (typeof(DateTime).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((DateTime)valueObj);
                        if (typeof(DateTimeOffset).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((DateTimeOffset)valueObj);
                        if (typeof(double).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((double)valueObj);
                        if (typeof(Guid).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((Guid)valueObj);
                        if (typeof(int).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((int)valueObj);
                        if (typeof(long).IsAssignableFrom(valueObj.GetType()))
                            return new EntityProperty((long)valueObj);

                        return NullValue();

                        EntityProperty NullValue() =>new EntityProperty(new byte[] { });
                    })
                .ToArray();
            return arrOfObj;
        }

        public static TResult CastEntityProperty<TResult>(this object value, Type valueType,
            Func<EntityProperty, TResult> onValue,
            Func<TResult> onNoCast,
            bool amDesperate = false)
        {
            if (valueType.TryGetAttributeInterface<ICastEntityProperty>(out var attributeInterface, inherit: true))
            {
                return attributeInterface.CastEntityProperty(value: value, valueType,
                    onValue: onValue,
                    onNoCast: onNoCast);
            }

            if (valueType.IsArray)
            {
                var arrayType = valueType.GetElementType();
                return value.CastSingleValueToArray(arrayType,
                    onValue,
                    onNoCast);
            }

            if (valueType.IsSubClassOfGeneric(typeof(IDictionary<,>)))
            {
                var kvpKeyType = valueType.GenericTypeArguments[0];
                var kvpValueType = valueType.GenericTypeArguments[1];
                var kvps = value
                    .DictionaryKeyValuePairs()
                    .ToArray();
                var keyValues = kvps
                    .Select(kvp => kvp.Key)
                    .CastArray(kvpKeyType);
                return keyValues.CastSingleValueToArray(kvpKeyType,
                    epKeys =>
                    {
                        var valueValues = kvps.Select(kvp => kvp.Value).CastArray(kvpValueType);
                        return valueValues.CastSingleValueToArray(kvpValueType,
                            epValues =>
                            {
                                var bytess = (new[] { epKeys.BinaryValue, epValues.BinaryValue })
                                    .ToByteArray();
                                var ep = new EntityProperty(bytess);
                                return onValue(ep);
                            },
                            () => throw new NotImplementedException());
                    },
                    () => throw new NotImplementedException());
            }

            //if (valueType.IsSubClassOfGeneric(typeof(KeyValuePair<,>)))
            //{
            //    var propBindings = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            //    return valueType
            //        .GetProperty("Key", propBindings)
            //        .GetValue(value)
            //        .CastEntityProperty(valueType.GenericTypeArguments.First(),
            //            keyEp =>
            //            {
            //                return valueType
            //                    .GetProperty("Value", propBindings)
            //                    .GetValue(value)
            //                    .CastEntityProperty(valueType.GenericTypeArguments.First(),
            //                        valueEp =>
            //                        {
            //                            var bytess = (new[] { keyEp, valueEp })
            //                                .ToByteArrayOfEntityProperties();
            //                            var ep = new EntityProperty(bytess);
            //                            return onValue(ep);
            //                        },
            //                        onNoCast: onNoCast);
            //            },
            //            onNoCast: onNoCast);
            //}

            #region Basic values

            if (typeof(string).IsInstanceOfType(value))
            {
                var stringValue = value as string;
                var ep = new EntityProperty(stringValue);
                return onValue(ep);
            }
            if (typeof(bool).IsInstanceOfType(value))
            {
                var boolValue = (bool)value;
                var ep = new EntityProperty(boolValue);
                return onValue(ep);
            }
            if (typeof(float).IsInstanceOfType(value))
            {
                var floatValue = (float)value;
                var ep = new EntityProperty(floatValue);
                return onValue(ep);
            }
            if (typeof(double).IsInstanceOfType(value))
            {
                var floatValue = (double)value;
                var ep = new EntityProperty(floatValue);
                return onValue(ep);
            }
            if (typeof(decimal).IsInstanceOfType(value))
            {
                var decimalValue = (decimal)value;
                var ep = new EntityProperty((double)decimalValue);
                return onValue(ep);
            }
            if (typeof(int).IsInstanceOfType(value))
            {
                var intValue = (int)value;
                var ep = new EntityProperty(intValue);
                return onValue(ep);
            }
            if (typeof(long).IsInstanceOfType(value))
            {
                var longValue = (long)value;
                var ep = new EntityProperty(longValue);
                return onValue(ep);
            }
            if (typeof(DateTime).IsInstanceOfType(value))
            {
                var dateTimeValue = (DateTime)value;
                var ep = new EntityProperty(dateTimeValue);
                return onValue(ep);
            }
            if (typeof(TimeSpan).IsInstanceOfType(value))
            {
                var timeSpanValue = (TimeSpan)value;
                var ep = new EntityProperty(timeSpanValue.TotalSeconds);
                return onValue(ep);
            }
            if (typeof(Guid).IsInstanceOfType(value))
            {
                var guidValue = (Guid)value;
                var ep = new EntityProperty(guidValue);
                return onValue(ep);
            }
            if (typeof(Uri).IsInstanceOfType(value))
            {
                var uriValue = (Uri)value;
                var ep = new EntityProperty(uriValue.OriginalString);
                return onValue(ep);
            }
            if (typeof(Type).IsInstanceOfType(value))
            {
                var typeValue = (value as Type);
                var typeString = typeValue.AssemblyQualifiedName;
                var ep = new EntityProperty(typeString);
                return onValue(ep);
            }
            if (valueType.IsEnum)
            {
                var enumNameString = Enum.GetName(valueType, value);
                var ep = new EntityProperty(enumNameString);
                return onValue(ep);
            }

            #region Refs

            if (typeof(IReferenceable).IsAssignableFrom(valueType))
            {
                var refValue = value as IReferenceable;
                var guidValue = refValue.id;
                var ep = new EntityProperty(guidValue);
                return onValue(ep);
            }
            if (typeof(IReferenceableOptional).IsAssignableFrom(valueType))
            {
                var refValue = value as IReferenceableOptional;
                var guidValueMaybe = refValue.IsDefaultOrNull() ? default(Guid?) : refValue.id;
                var ep = new EntityProperty(guidValueMaybe);
                return onValue(ep);
            }
            if (typeof(IReferences).IsAssignableFrom(valueType))
            {
                var refValue = value as IReferences;
                var guidValues = refValue.ids;
                var ep = new EntityProperty(guidValues.ToByteArrayOfGuids());
                return onValue(ep);
            }

            #endregion

            #endregion

            return valueType.IsNullable(
                nullableType =>
                {
                    if (!value.NullableHasValue())
                        return onValue(new EntityProperty(default(int?))); // best way to rep null

                    var valueFromNullable = value.GetNullableValue();
                    return CastEntityProperty(valueFromNullable, nullableType,
                        onValue,
                        onNoCast);
                },
                () =>
                {
                    if (typeof(object) == valueType)
                    {
                        if (null == value)
                        {
                            var nullGuidKey = new Guid(EDMExtensions.NullGuidKey);
                            var ep = new EntityProperty(nullGuidKey);
                            return onValue(ep);
                        }
                        var valueTypeOfInstance = value.GetType();
                        if (typeof(object) == valueTypeOfInstance) // Prevent stack overflow recursion
                        {
                            var ep = new EntityProperty(new byte[] { });
                            return onValue(ep);
                        }
                        return CastEntityProperty(value, valueTypeOfInstance, onValue, onNoCast);
                    }

                    if (amDesperate)
                    {
                        var dict = valueType
                            .GetPropertyOrFieldMembers()
                            .Select(field => (field.Name, field.GetPropertyOrFieldValue(value)))
                            .Where(tpl => tpl.Item2.IsNotDefaultOrNull())
                            .Select(tpl => tpl.Item1.PairWithValue(tpl.Item2))
                            .ToDictionary();

                        if (dict.Any())
                            return dict.CastEntityProperty(dict.GetType(), onValue: onValue, onNoCast: onNoCast);
                    }

                    return onNoCast();
                });


        }

        public static TResult Bind<TResult>(this EntityProperty value, Type type, 
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            if (type.TryGetAttributeInterface<IBindEntityProperty>(out var attributeInterface, inherit: true))
            {
                return attributeInterface.BindEntityProperty(value: value, type:type,
                    onBound: onBound,
                    onFailedToBind: onFailedToBind);
            }

            if (type.IsArray)
            {
                var arrayType = type.GetElementType();
                return value.BindSingleValueToArray(arrayType,
                    onBound,
                    onFailedToBind);
            }

            if (type.IsSubClassOfGeneric(typeof(IDictionary<,>)))
            {
                var keyType = type.GenericTypeArguments[0];
                var valueType = type.GenericTypeArguments[1];

                if (value.PropertyType != EdmType.Binary)
                    return onBound(GetDefaultDictValue());

                var epKeysEpValues = value.BinaryValue.FromByteArray().ToArray();
                if (epKeysEpValues.Length != 2)
                    return onBound(GetDefaultDictValue());
                var epKeys = epKeysEpValues[0];
                var epValues = epKeysEpValues[1];

                return new EntityProperty(epKeys).BindSingleValueToArray(keyType,
                    keys =>
                    {
                        return new EntityProperty(epValues).BindSingleValueToArray(valueType,
                            values =>
                            {
                                var defaultValue = GetDefaultDictValue();
                                var valuesEnumerator = values.ObjectToEnumerable().GetEnumerator();
                                var dict = keys
                                    .ObjectToEnumerable()
                                    .Select(
                                        (key) =>
                                        {
                                            if (!valuesEnumerator.MoveNext())
                                                return (false, default(KeyValuePair<object, object>));
                                            var kvp = key.PairWithValue(valuesEnumerator.Current);
                                            return (true, kvp);
                                        })
                                    .SelectWhere()
                                    .Select(kvp => (object)kvp)
                                    .ToArray()
                                    .KeyValuePairsToDictionary(keyType, valueType);

                                return onBound(dict);
                            },
                            onFailedToBind: onFailedToBind);
                    },
                    onFailedToBind: onFailedToBind);

                object GetDefaultDictValue()
                {
                    var nonInterfaceType = typeof(Dictionary<,>).MakeGenericType(type.GenericTypeArguments);

                    return Activator.CreateInstance(nonInterfaceType);
                }
            }

            #region Basic values

            #region Core types

            var (isFailure, isDefault, coreValue) = ParseCoreTypes(type);
            if (!isFailure)
                return onBound(coreValue);

            #endregion

            #region refs

            if (type.IsSubClassOfGeneric(typeof(IRef<>)))
            {
                if (TryGetGuidValue(out Guid guidValue))
                {
                    var instance = guidValue.BindToRef(type.GenericTypeArguments.First());
                    return onBound(instance);
                }

                bool TryGetGuidValue(out Guid guid)
                {
                    if (value.PropertyType == EdmType.Guid)
                    {
                        guid = value.GuidValue.Value;
                        return true;
                    }
                    if (value.PropertyType == EdmType.String)
                        return Guid.TryParse(value.StringValue, out guid);

                    guid = default;
                    return false;
                }
            }

            if (type.IsSubClassOfGeneric(typeof(IRefOptional<>)))
            {
                Guid? GetIdMaybe()
                {
                    if (value.PropertyType == EdmType.String)
                    {
                        if (Guid.TryParse(value.StringValue, out Guid id))
                            return id;
                        return default(Guid?);
                    }

                    if (value.PropertyType != EdmType.Guid)
                        return default(Guid?);

                    return value.GuidValue;
                }
                var guidValueMaybe = GetIdMaybe();
                var resourceType = type.GenericTypeArguments.First();
                var instantiatableType = typeof(EastFive.RefOptional<>)
                    .MakeGenericType(resourceType);
                if (!guidValueMaybe.HasValue)
                {
                    var refOpt = Activator.CreateInstance(instantiatableType, new object[] { });
                    return onBound(refOpt);
                }
                var guidValue = guidValueMaybe.Value;
                var refValue = guidValue.BindToRef(type.GenericTypeArguments.First());
                var instance = Activator.CreateInstance(instantiatableType, new object[] { refValue });
                return onBound(instance);
            }

            if (type.IsSubClassOfGeneric(typeof(IRefs<>)))
            {
                var guidValues = value.BinaryValue.ToGuidsFromByteArray();
                var resourceType = type.GenericTypeArguments.First();
                var instantiatableType = typeof(EastFive.Refs<>).MakeGenericType(resourceType);
                var instance = Activator.CreateInstance(instantiatableType, new object[] { guidValues });
                return onBound(instance);
            }

            #endregion

            if (typeof(object) == type)
            {
                switch (value.PropertyType)
                {
                    case EdmType.Binary:
                        return onBound(value.BinaryValue);
                    case EdmType.Boolean:
                        if (value.BooleanValue.HasValue)
                            return onBound(value.BooleanValue.Value);
                        break;
                    case EdmType.DateTime:
                        if (value.DateTime.HasValue)
                            return onBound(value.DateTime.Value);
                        break;
                    case EdmType.Double:
                        if (value.DoubleValue.HasValue)
                            return onBound(value.DoubleValue.Value);
                        break;
                    case EdmType.Guid:
                        if (value.GuidValue.HasValue)
                        {
                            var nullGuidKey = new Guid(EDMExtensions.NullGuidKey);
                            if (value.GuidValue.Value == nullGuidKey)
                                return onBound(null);
                            return onBound(value.GuidValue.Value);
                        }
                        break;
                    case EdmType.Int32:
                        if (value.Int32Value.HasValue)
                            return onBound(value.Int32Value.Value);
                        break;
                    case EdmType.Int64:
                        if (value.Int64Value.HasValue)
                            return onBound(value.Int64Value.Value);
                        break;
                    case EdmType.String:
                        return onBound(value.StringValue);
                }
                return onBound(value.PropertyAsObject);
            }

            #endregion

            return type.IsNullable(
                nullableType =>
                {
                    var (isFailure, isDefault, coreValue) = ParseCoreTypes(nullableType);

                    if(isFailure)
                        return onFailedToBind();

                    if (isDefault)
                    {
                        var nullableDefault = nullableType.GetNullValueForNullableType();
                        return onBound(nullableDefault);
                    }

                    return onBound(coreValue);
                },
                () => onFailedToBind());


            (bool isFailure, bool isDefault, object value) ParseCoreTypes(Type type)
            {
                if (typeof(Guid) == type)
                {
                    if (value.PropertyType == EdmType.Guid)
                    {
                        var guidValue = value.GuidValue;
                        if (guidValue.HasValue)
                            return (false, false, guidValue.Value);
                    }
                    if (value.PropertyType == EdmType.String)
                    {
                        var guidStr = value.StringValue;
                        if (Guid.TryParse(guidStr, out Guid guidValue))
                            return (false, false, guidValue);
                    }

                    // This seems to be the best move in case of data migration
                    return (false, true, default(Guid));
                }
                // TODO: Type check the rest of these like GUID
                if (typeof(long) == type)
                {
                    if (value.PropertyType == EdmType.Int64)
                    {
                        var longValue = value.Int64Value;
                        if (longValue.HasValue)
                            return (false, false, longValue.Value);
                    }
                    if (value.PropertyType == EdmType.Int32)
                    {
                        var intValue = value.Int32Value;
                        if (!intValue.HasValue)
                        {
                            var longValue = (long)intValue;
                            return (false, false, longValue);
                        }
                    }
                    if (value.PropertyType == EdmType.String)
                    {
                        var longStr = value.StringValue;
                        if (long.TryParse(longStr, out var longValue))
                            return (false, false, longValue);
                    }
                    return (false, true, default(long));
                }
                if (typeof(int) == type)
                {
                    if (value.PropertyType == EdmType.Int32
                        || value.PropertyType == EdmType.Int64
                        || value.PropertyType == EdmType.Double)
                    {
                        var intValue = value.Int32Value;
                        return (false, false, intValue);
                    }
                }
                if (typeof(float) == type)
                {
                    if (value.PropertyType == EdmType.Double)
                    {
                        var doubleValue = value.DoubleValue;
                        if (doubleValue.HasValue)
                            return (false, false, (float)doubleValue.Value);
                    }
                    if (value.PropertyType == EdmType.Int32)
                    {
                        var intValue = value.Int32Value;
                        if (intValue.HasValue)
                        {
                            var floatValue = (float)intValue;
                            return (false, false, floatValue);
                        }
                    }
                    if (value.PropertyType == EdmType.String)
                    {
                        var floatStr = value.StringValue;
                        if (float.TryParse(floatStr, out var floatValue))
                            return (false, false, floatValue);
                    }
                    return (false, true, default(float));
                }
                if (typeof(double) == type)
                {
                    if (value.PropertyType == EdmType.Double)
                    {
                        var doubleValue = value.DoubleValue;
                        if (doubleValue.HasValue)
                            return (false, false, doubleValue.Value);
                    }
                    if (value.PropertyType == EdmType.Int32)
                    {
                        var intValue = value.Int32Value;
                        if (intValue.HasValue)
                        {
                            var doubleValue = (double)intValue;
                            return (false, false, doubleValue);
                        }
                    }
                    if (value.PropertyType == EdmType.String)
                    {
                        var doubleStr = value.StringValue;
                        if (double.TryParse(doubleStr, out var doubleValue))
                            return (false, false, doubleValue);
                    }
                    return (false, true, default(double));
                }
                if (typeof(string) == type)
                {
                    if (value.PropertyType != EdmType.String)
                        return (false, true, default(string));
                    var stringValue = value.StringValue;
                    return (false, false, stringValue);
                }
                if (typeof(DateTime) == type)
                {
                    if (value.PropertyType == EdmType.Int64)
                    {
                        if (value.Int64Value.HasValue)
                        {
                            var dtValue = new DateTime(value.Int64Value.Value);
                            return (false, false, dtValue);
                        }
                    }
                    if (value.PropertyType == EdmType.DateTime)
                    {
                        if (value.DateTime.HasValue)
                        {
                            var dtValue = value.DateTime;
                            return (false, false, dtValue);
                        }
                    }
                    return (false, true, default(DateTime));
                }
                if (typeof(TimeSpan) == type)
                {
                    if (value.DoubleValue.HasValue)
                    {
                        var seconds = value.DoubleValue.Value;
                        var tsValue = TimeSpan.FromSeconds(seconds);
                        return (false, false, tsValue);
                    }
                    return (false, true, TimeSpan.FromSeconds(0));
                }
                if (typeof(TimeZoneInfo) == type)
                {
                    if (value.PropertyType != EdmType.String)
                        return (true, true, default);
                    var stringValue = value.StringValue;
                    try
                    {
                        var tzi = stringValue.FindSystemTimeZone();
                        return (false, false, tzi);
                    }
                    catch (Exception)
                    {
                        return (true, true, default);
                    }
                }
                if (typeof(Uri) == type)
                {
                    var strValue = value.StringValue;
                    if (Uri.TryCreate(strValue, UriKind.RelativeOrAbsolute, out Uri uriValue))
                        return (false, false, uriValue);
                    return (false, true, uriValue);
                }
                if (typeof(Type) == type)
                {
                    var typeValueString = value.StringValue;
                    var typeValue = Type.GetType(typeValueString);
                    return (false, typeValue == null, typeValue);
                }
                if (type.IsEnum)
                {
                    var enumNameString = value.StringValue;
                    if (enumNameString.IsNullOrWhiteSpace())
                    {
                        var defaultValue = type.GetDefault();
                        return (false, true, defaultValue);
                    }

                    if (!Enum.TryParse(type, enumNameString, out var enumValue))
                    {
                        var defaultValue = type.GetDefault();
                        return (false, true, defaultValue);
                    }

                    return (false, false, enumValue);
                }
                if (typeof(bool) == type)
                {
                    if (value.PropertyType == EdmType.Boolean)
                    {
                        var boolValue = value.BooleanValue;
                        return (false, !value.BooleanValue.HasValue, boolValue);
                    }
                    if(value.PropertyType == EdmType.String)
                    {
                        var isDefault = !value.StringValue.TryParseBool(out var boolValue);
                        return (false, isDefault, boolValue);
                    }

                    throw new Exception($"Cannot cast {value.PropertyType} to {nameof(Boolean)}");
                }

                return (true, true, null);
            }
        }

        public static object GetPropertyAsObject(this EntityProperty epValue, out bool hasValue)
        {
            if (epValue.PropertyType == EdmType.String)
            {
                hasValue = true;
                return epValue.StringValue;
            }
            if (epValue.PropertyType == EdmType.DateTime)
            {
                hasValue = epValue.DateTime.HasValue;
                return epValue.DateTime;
            }
            if (epValue.PropertyType == EdmType.Binary)
            {
                hasValue = epValue.BinaryValue.IsDefaultOrNull();
                return epValue.BinaryValue;
            }
            if (epValue.PropertyType == EdmType.Boolean)
            {
                hasValue = epValue.BooleanValue.HasValue;
                return epValue.BooleanValue;
            }
            if (epValue.PropertyType == EdmType.Double)
            {
                hasValue = epValue.DoubleValue.HasValue;
                return epValue.DoubleValue;
            }
            if (epValue.PropertyType == EdmType.Guid)
            {
                hasValue = epValue.GuidValue.HasValue;
                return epValue.GuidValue;
            }
            if (epValue.PropertyType == EdmType.Int32)
            {
                hasValue = epValue.Int32Value.HasValue;
                return epValue.Int32Value;
            }
            if (epValue.PropertyType == EdmType.Int64)
            {
                hasValue = epValue.Int64Value.HasValue;
                return epValue.Int64Value;
            }
            hasValue = true;
            return epValue.PropertyAsObject;
        }

        public static TResult CastSingleValueToArray<TResult>(this object value, Type arrayType, 
            Func<EntityProperty, TResult> onValue,
            Func<TResult> onNoCast)
        {
            #region Refs

            if (arrayType.IsSubClassOfGeneric(typeof(IReferenceable)))
            {
                var values = (IReferenceable[])value;
                var guidValues = values.Select(v => v.id).ToArray();
                var bytes = guidValues.ToByteArrayOfGuids();
                var ep = new EntityProperty(bytes);
                return onValue(ep);
            }
            if (arrayType.IsSubClassOfGeneric(typeof(IReferenceableOptional)))
            {
                var values = (IReferenceableOptional[])value;
                var guidMaybeValues = values.Select(v => v.IsDefaultOrNull() ? default(Guid?) : v.id).ToArray();
                var bytes = guidMaybeValues.ToByteArrayOfNullables(guid => guid.ToByteArray());
                var ep = new EntityProperty(bytes);
                return onValue(ep);
            }

            #endregion

            if (arrayType.IsArray)
            {
                var arrayElementType = arrayType.GetElementType();
                var valueEnumerable = (System.Collections.IEnumerable)value;
                var fullBytes = valueEnumerable
                    .Cast<object>()
                    .ToByteArray(
                        (v) =>
                        {
                            var vEnumerable = (System.Collections.IEnumerable)v;
                            var bytess = v.CastSingleValueToArray(arrayElementType,
                                ep => ep.BinaryValue,
                                () => new byte[] { });
                            return bytess;
                        })
                    .ToArray();
                //var bytess = entityProperties.ToByteArrayOfEntityProperties();
                var epArray = new EntityProperty(fullBytes);
                return onValue(epArray);
            }

            #region Basic Types

            if (arrayType == typeof(object))
            {
                var valueEnumerable = (System.Collections.IEnumerable)value;
                var entityProperties = valueEnumerable
                    .Cast<object>()
                    .Select(
                        (v) =>
                        {
                            return v.CastEntityProperty(arrayType,
                                ep => ep,
                                () =>
                                {
                                    throw new NotImplementedException(
                                        $"Serialization of {arrayType.FullName} is currently not supported on arrays.");
                                });
                        })
                    .ToArray();
                var bytess = entityProperties.ToByteArrayOfEntityProperties();
                var epArray = new EntityProperty(bytess);
                return onValue(epArray);
            }
            return arrayType.IsNullable(
                nulledType =>
                {
                    if (arrayType.IsAssignableFrom(typeof(Guid?)))
                    {
                        var values = (Guid?[])value;
                        var bytes = values.ToByteArrayOfNullableGuids(); //  values.ToByteArrayOfNullables(g => g.ToByteArray());
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }

                    if (arrayType.IsAssignableFrom(typeof(decimal?)))
                    {
                        var values = (decimal?[])value;
                        var bytes = values.ToByteArrayOfNullableDecimals(); // (d => d.ConvertToBytes());
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }

                    if (arrayType.IsAssignableFrom(typeof(DateTime?)))
                    {
                        var values = (DateTime?[])value;
                        var bytes = values.ToByteArrayOfNullableDateTimes();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }

                    return GetDefault();
                },
                () =>
                {
                    if (arrayType.IsAssignableFrom(typeof(Guid)))
                    {
                        var values = (Guid[])value;
                        var bytes = values.ToByteArrayOfGuids();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(byte)))
                    {
                        var values = (byte[])value;
                        var ep = new EntityProperty(values);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(bool)))
                    {
                        var values = (bool[])value;
                        var bytes = values
                            .Select(b => b ? (byte)1 : (byte)0)
                            .ToArray();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(DateTime)))
                    {
                        var values = (DateTime[])value;
                        var bytes = values.ToByteArrayOfDateTimes();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(double)))
                    {
                        var values = (double[])value;
                        var bytes = values.ToByteArrayOfDoubles();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(decimal)))
                    {
                        var values = (decimal[])value;
                        var bytes = values.ToByteArrayOfDecimals();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(int)))
                    {
                        var values = (int[])value;
                        var bytes = values.ToByteArrayOfInts();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(long)))
                    {
                        var values = (long[])value;
                        var bytes = values.ToByteArrayOfLongs();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(string)))
                    {
                        var values = (string[])value;
                        var bytes = values.ToUTF8ByteArrayOfStringNullOrEmptys();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsEnum)
                    {
                        var values = ((IEnumerable)value).Cast<object>();
                        var bytes = values.ToByteArrayOfEnums(arrayType);
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    if (arrayType.IsAssignableFrom(typeof(Uri)))
                    {
                        var values = (Uri[])value;
                        var bytes = values.Select(v => v.OriginalString).ToUTF8ByteArrayOfStringNullOrEmptys();
                        var ep = new EntityProperty(bytes);
                        return onValue(ep);
                    }
                    return GetDefault();
                });

            #endregion

            TResult GetDefault()
            {
                var entityCasters = arrayType
                    .GetAttributesInterface<ICast<EntityProperty>>(true)
                    .ToArray();
                var valueEnumerable = (System.Collections.IEnumerable)value;
                var valueEnumerator = valueEnumerable.GetEnumerator();
                var entityProperties = valueEnumerable
                    .Cast<object>()
                    .Select(
                        (v, index) =>
                        {
                            return entityCasters
                                .First(
                                    (epSerializer, next) =>
                                    {
                                        return epSerializer.Cast(v, arrayType, default, default,
                                            ep => ep,
                                            () =>
                                            {
                                                throw new NotImplementedException(
                                                    $"Serialization of {arrayType.FullName} is currently not supported on arrays.");
                                            });
                                    },
                                    () =>
                                    {
                                        return arrayType
                                            .GetAttributesInterface<ICast<IDictionary<string, EntityProperty>>>(true)
                                            .First(
                                                (epSerializer, next) =>
                                                {
                                                    var key = $"{index}";
                                                    return epSerializer.Cast(v, arrayType, key, default,
                                                        epKvp => epKvp[key],
                                                        () =>
                                                        {
                                                            throw new NotImplementedException(
                                                                $"Serialization of {arrayType.FullName} is currently not supported on arrays.");
                                                        });
                                                },
                                                () =>
                                                {
                                                    return v.CastEntityProperty(arrayType,
                                                        ep => ep,
                                                        () =>
                                                        {
                                                            throw new NotImplementedException(
                                                                $"Serialization of {arrayType.FullName} is currently not supported on arrays.");
                                                        },
                                                        amDesperate: true);
                                                });
                                    });
                        })
                    .ToArray();
                var bytess = entityProperties.ToByteArrayOfEntityProperties();
                var epArray = new EntityProperty(bytess);
                return onValue(epArray);
            }
        }

        public static TResult BindSingleValueToArray<TResult>(this EntityProperty value, Type arrayType, 
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            #region Refs

            object ComposeFromBase<TBase>(Type composedType, Type genericCompositionType,
                Func<Type, TBase, object> instantiate)
            {
                return value.BindSingleValueToArray(typeof(TBase),
                    objects =>
                    {
                        var guids = (TBase[])objects;
                        var resourceType = arrayType.GenericTypeArguments.First();
                        var instantiatableType = genericCompositionType.MakeGenericType(resourceType);

                        var refs = guids
                            .Select(
                                guidValue =>
                                {
                                    var instance = instantiate(instantiatableType, guidValue);
                                    // Activator.CreateInstance(instantiatableType, new object[] { guidValue });
                                    return instance;
                                })
                           .ToArray();
                        var typedRefs = refs.CastArray(arrayType);
                        return typedRefs;
                    },
                    () => throw new Exception("BindArray failed to bind to Guids?"));
            }

            if (arrayType.IsSubClassOfGeneric(typeof(IRef<>)))
            {
                var values = ComposeFromBase<Guid>(typeof(IRef<>), typeof(EastFive.Ref<>),
                    (instantiatableType, guidValue) => Activator.CreateInstance(instantiatableType, new object[] { guidValue }));
                return onBound(values);
            }

            object ComposeOptionalFromBase<TBase>(Type composedType, Type genericCompositionType, Type optionalBaseType)
            {
                var values = ComposeFromBase<Guid?>(composedType, genericCompositionType,
                    (instantiatableType, guidValueMaybe) =>
                    {
                        if (!guidValueMaybe.HasValue)
                            return Activator.CreateInstance(instantiatableType, new object[] { });
                        var guidValue = guidValueMaybe.Value;
                        var resourceType = arrayType.GenericTypeArguments.First();
                        var instantiatableRefType = optionalBaseType.MakeGenericType(resourceType);
                        var refValue = Activator.CreateInstance(instantiatableRefType, new object[] { guidValue });
                        var result = Activator.CreateInstance(instantiatableType, new object[] { refValue });
                        return result;
                    });
                return values;
            }

            if (arrayType.IsSubClassOfGeneric(typeof(IRefOptional<>)))
            {
                var values = ComposeOptionalFromBase<Guid?>(typeof(IRefOptional<>),
                    typeof(EastFive.RefOptional<>), typeof(EastFive.Ref<>));
                return onBound(values);
            }


            #endregion

            if (arrayType.IsArray)
            {
                var arrayElementType = arrayType.GetElementType();
                var values = value.BinaryValue
                    .FromByteArray()
                    .Select(
                        bytes =>
                        {
                            var ep = new EntityProperty(bytes);
                            var arrayValues = ep.BindSingleValueToArray<object>(arrayElementType,
                                v => v,
                                () => Array.CreateInstance(arrayElementType, 0));
                            // var arrayValues = bytes.FromEdmTypedByteArray(arrayElementType);
                            return arrayValues;
                        })
                    .ToArray();
                //var values = value.BinaryValue.FromEdmTypedByteArray(arrayElementType);
                return onBound(values);
            }

            if (arrayType.IsSubClassOfGeneric(typeof(IDictionary<,>)))
            {
                TResult onEmpty()
                {
                    var emptyArray = typeof(Array)
                        .GetMethod(nameof(Array.Empty), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        .MakeGenericMethod(arrayType)
                        .Invoke(null, new object[] { });
                    return onBound(emptyArray);
                }

                if (value.PropertyType != EdmType.Binary)
                    return onEmpty();

                var dictionariesBytes = value.BinaryValue.FromEdmTypedByteArray(typeof(byte[]));
                var dictionary = dictionariesBytes
                    .Select(
                        dictionaryBytes =>
                        {
                            var dict = GetDictionary(dictionaryBytes as byte[]);
                            return dict;
                        })
                    .CastArray(arrayType);
                return onBound(dictionary);

                object GetDictionary(byte [] dictionaryBytes)
                {
                    var keysAndValues = dictionaryBytes.FromByteArray().ToArray();
                    if (keysAndValues.Length != 2)
                        return onEmpty();
                    return new EntityProperty(keysAndValues[0])
                        .BindSingleValueToArray(
                                arrayType.GenericTypeArguments[0],
                            (keys) =>
                            {
                                return new EntityProperty(keysAndValues[1])
                                    .BindSingleValueToArray(
                                            arrayType.GenericTypeArguments[1],
                                        (values) =>
                                        {
                                            var dict = (keys as object[]).ArraysToDictionary(
                                                (values as object[]),
                                                arrayType.GenericTypeArguments[0],
                                                arrayType.GenericTypeArguments[1]);
                                            return dict;
                                        },
                                        () => Activator.CreateInstance(arrayType));
                            },
                            () => Activator.CreateInstance(arrayType));
                }
            }

            if (arrayType.IsSubClassOfGeneric(typeof(KeyValuePair<,>)))
            {
                var propBindings = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                var keyProp = arrayType.GetProperty("Key", propBindings);
                var valueProp = arrayType.GetProperty("Value", propBindings);
                var kvpArray = value.BinaryValue
                    .FromEdmTypedByteArray(typeof(object))
                    .Select(
                        kvpBinary =>
                        {
                            var kvp = Activator.CreateInstance(arrayType);
                            var kvpValues = (kvpBinary as byte[])
                                .FromEdmTypedByteArray(typeof(object));
                            keyProp.SetValue(kvp, kvpValues[0]);
                            valueProp.SetValue(kvp, kvpValues[1]);
                            return kvp;
                        })
                    .ToArray();

                return onBound(kvpArray);
            }

            return arrayType.IsNullable(
                nulledType =>
                {
                    if (typeof(Guid) == nulledType)
                    {
                        var values = value.BinaryValue.ToNullableGuidsFromByteArray();
                        return onBound(values);
                    }
                    if (typeof(decimal) == nulledType)
                    {
                        var values = value.BinaryValue.ToNullableDecimalsFromByteArray();
                        return onBound(values);
                    }
                    if (typeof(DateTime) == nulledType)
                    {
                        var values = value.BinaryValue.ToNullableDateTimesFromByteArray();
                        return onBound(values);
                    }
                    var arrayOfObj = value.BinaryValue.FromEdmTypedByteArray(arrayType);
                    var arrayOfType = arrayOfObj.CastArray(arrayType);
                    return onBound(arrayOfType);

                    throw new Exception($"Cannot serialize a nullable array of `{nulledType.FullName}`.");
                },
                () =>
                {
                    #region Built in types

                    if (typeof(Guid) == arrayType)
                    {
                        var values = value.BinaryValue.ToGuidsFromByteArray();
                        return onBound(values);
                    }
                    if (typeof(byte) == arrayType)
                    {
                        return onBound(value.BinaryValue);
                    }
                    if (typeof(bool) == arrayType)
                    {
                        var boolArray = value.BinaryValue
                            .Select(b => b != 0)
                            .ToArray();
                        return onBound(boolArray);
                    }
                    if (typeof(DateTime) == arrayType)
                    {
                        var values = value.BinaryValue.ToDateTimesFromByteArray();
                        return onBound(values);
                    }
                    if (typeof(double) == arrayType)
                    {
                        var values = value.BinaryValue.ToDoublesFromByteArray();
                        return onBound(values);
                    }
                    if (typeof(decimal) == arrayType)
                    {
                        var values = value.BinaryValue.ToDecimalsFromByteArray();
                        return onBound(values);
                    }
                    if (typeof(int) == arrayType)
                    {
                        var values = value.BinaryValue.ToIntsFromByteArray();
                        return onBound(values);
                    }
                    if (typeof(long) == arrayType)
                    {
                        var values = value.BinaryValue.ToLongsFromByteArray();
                        return onBound(values);
                    }
                    if (typeof(string) == arrayType)
                    {
                        var values = value.BinaryValue.ToStringNullOrEmptysFromUTF8ByteArray();
                        return onBound(values);
                    }
                    if (arrayType.IsEnum)
                    {
                        var values = value.BinaryValue.ToEnumsFromByteArray(arrayType, repair:true);
                        return onBound(values);
                    }
                    if (typeof(object) == arrayType)
                    {
                        var values = value.BinaryValue.FromEdmTypedByteArray(arrayType);
                        return onBound(values);
                    }
                    if (typeof(Uri) == arrayType)
                    {
                        var values = value.BinaryValue.ToStringNullOrEmptysFromUTF8ByteArray();
                        var urls = values
                            .Select(
                                value =>
                                {
                                    bool created = Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri url);
                                    return url;
                                })
                            .ToArray();
                        return onBound(urls);
                    }

                    #endregion

                    return arrayType
                        .GetAttributesInterface<IBind<EntityProperty>>(true)
                        .First<IBind<EntityProperty>, TResult>(
                            (epSerializer, next) =>
                            {
                                var values = value.BinaryValue.FromEdmTypedByteArrayToEntityProperties(typeof(byte[]));
                                var boundValues = values
                                    //.Where(valueObject => valueObject is byte[])
                                    .Select(
                                        valueObject =>
                                        {
                                            //var valueBytes = valueObject as byte[];
                                            var valueEp = valueObject; // new EntityProperty(valueBytes);
                                            return epSerializer.Bind(valueEp, arrayType, string.Empty, default,
                                                v => v,
                                                () => arrayType.GetDefault());
                                        })
                                    .CastArray(arrayType);
                                return onBound(boundValues);
                            },
                            () =>
                            {
                                if(value.PropertyType == EdmType.Binary)
                                {
                                    var arrayValues = value.BinaryValue
                                        .FromEdmTypedByteArrayToEntityProperties(arrayType)
                                        .Select(
                                            entityProperty =>
                                            {
                                                return entityProperty.Bind(typeof(IDictionary<string, object>),
                                                    v =>
                                                    {
                                                        var dict = (IDictionary<string, object>)v;
                                                        var emptyInstance = Activator.CreateInstance(arrayType);
                                                        var populatedInstance = arrayType
                                                            .GetPropertyOrFieldMembers()
                                                            .Aggregate(emptyInstance,
                                                                (v, memberInfo) =>
                                                                {
                                                                    if (dict.TryGetValue(memberInfo.Name, out var memberValue))
                                                                        memberInfo.SetPropertyOrFieldValue(v, memberValue);
                                                                    return v;
                                                                });
                                                        return onBound(populatedInstance);
                                                    },
                                                    onFailedToBind: () => arrayType.GetDefault());
                                            })
                                        .ToArray()
                                        .CastArray(arrayType);

                                    return onBound(arrayValues);
                                }
                                throw new Exception($"Cannot serialize array of `{arrayType.FullName}`.");
                            });
                });
        }

    }
}

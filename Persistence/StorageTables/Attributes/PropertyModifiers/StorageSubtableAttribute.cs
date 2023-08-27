using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Collections.Generic;
using EastFive.Reflection;
using EastFive.Serialization;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Persistence
{
    public class StorageSubtableAttribute : Attribute,
        IPersistInAzureStorageTables, IModifyAzureStorageTableSave, IProvideEntity
    {
        private const string overflowToken = "8d40521b-7d71-47b3-92c5-46e4a804e7de";
        private const string overflowTokenString = "9a9a2e13d0ed44d7aa39c2549aff176a";

        public string Name { get; set; }

        public string GetTablePropertyName(MemberInfo member)
        {
            var tablePropertyName = this.Name;
            if (tablePropertyName.IsNullOrWhiteSpace())
                return member.Name;
            return tablePropertyName;
        }

        public virtual KeyValuePair<string, EntityProperty>[] ConvertValue<EntityType>(MemberInfo memberInfo,
            object value, IWrapTableEntity<EntityType> tableEntityWrapper)
        {
            var key = GetTablePropertyName(memberInfo);
            var rowKey = tableEntityWrapper.RowKey;
            var partitionKey = tableEntityWrapper.PartitionKey;
            return new KeyValuePair<string, EntityProperty>[]
            {
                $"{key}__RK".PairWithValue(new EntityProperty(rowKey)),
                $"{key}__PK".PairWithValue(new EntityProperty(partitionKey)),
            };
        }

        public virtual object GetMemberValue(MemberInfo memberInfo, IDictionary<string, EntityProperty> values,
            Func<object> getDefaultValue = default)
        {
            var memberType = memberInfo.GetPropertyOrFieldType();
            if (!memberType.IsSubClassOfGeneric(typeof(Func<>)))
                throw new ArgumentException($"{memberInfo.Name} is not of type {nameof(Func<Task>)} and therefore cannot be used as a subtable");
            var taskType = memberType.GenericTypeArguments.First();
            if (!taskType.IsSubClassOfGeneric(typeof(Task<>)))
                throw new ArgumentException($"{memberInfo.Name} is not of type {nameof(Func<Task>)} and therefore cannot be used as a subtable");
            var resultType = taskType.GenericTypeArguments.First();

            var key = GetTablePropertyName(memberInfo);

            if (!values.TryGetValue($"{key}__RK", out EntityProperty epRowKey))
                return GetEmptyResult();
            var rowKey = epRowKey.StringValue;

            if (!values.TryGetValue($"{key}__PK", out EntityProperty epParitionKey))
                return GetEmptyResult();
            var partitionKey = epParitionKey.StringValue;

            var lookupDelegate = typeof(StorageSubtableAttribute)
                .GetMethod(nameof(GetLookupDelegate), BindingFlags.Public | BindingFlags.Static)
                .MakeGenericMethod(resultType)
                .Invoke(null, new object[] { memberInfo, rowKey, partitionKey, this });

            return lookupDelegate;

            object GetEmptyResult()
            {
                var method = typeof(StorageSubtableAttribute)
                    .GetMethod(nameof(GetEmptyLookupDelegate), BindingFlags.Public | BindingFlags.Static);
                var genericMethod = method.MakeGenericMethod(resultType);
                var emptyLookupDelegate = genericMethod.Invoke(null, new object[] {  });
                return emptyLookupDelegate;
            }
        }

        public static Func<Task<T>> GetEmptyLookupDelegate<T>()
        {
            return () =>
            {
                if(typeof(T).IsArray)
                {
                    var elementType = typeof(T).GetElementType();
                    var arrayItem = (T)elementType.ConstructEmptyArray();
                    return arrayItem.AsTask();
                }
                var newItem = Activator.CreateInstance<T>();
                return newItem.AsTask();
            };
        }

        public static Func<Task<T>> GetLookupDelegate<T>(MemberInfo memberInfo,
            string rowKey, string partitionKey, IProvideEntity entityProvider)
        {
            var repository = AzureTableDriverDynamic.FromSettings();
            var tableName = StorageLookupAttribute.GetMemberTableName(memberInfo);
            // var tableRef = repository.TableClient.GetTableReference(tableName);
            Func<Task<T>> lookupDelegate = () =>
                repository.FindByIdAsync<T, T>(rowKey, partitionKey,
                    onFound:(resource, tr) => resource,
                    onNotFound:() =>
                    {
                        if(typeof(T).IsArray)
                        {
                            return (T)typeof(T)
                                .GetElementType()
                                .ConstructEmptyArray();
                        }
                        return default(T);
                    },
                        tableName:tableName,
                        entityProvider:entityProvider);
            return lookupDelegate;
        }

        public async Task<TResult> ExecuteCreateAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef, TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var propertyValue = memberInfo.GetValue(value);
            if (propertyValue == default)
                return onSuccessWithRollback(() => true.AsTask());

            var newValueType = propertyValue.GetType();
            if (!newValueType.IsSubClassOfGeneric(typeof(Func<>)))
            {
                return onSuccessWithRollback(() => true.AsTask());
            }
            var taskValue = propertyValue.ExecuteFunction(out Type taskType);
            var resultValue = await taskValue.CastAsTaskObjectAsync(out Type typeToSave);
            var rawValues = Serialize(resultValue, typeToSave);

            var subtableEntity = new SubtableEntity(rowKeyRef, partitionKeyRef, rawValues);
            var tableName = StorageLookupAttribute.GetMemberTableName(memberInfo);
            var tableRef = repository.TableClient.GetTableReference(tableName);
            return await repository.CreateAsync(subtableEntity, new E5CloudTable(tableRef),
                    (discard, discard2) => onSuccessWithRollback(() => 1.AsTask()),
                    onAlreadyExists: () => onFailure());

        }

        public Task<TResult> ExecuteInsertOrReplaceAsync<TEntity, TResult>(MemberInfo memberInfo, string rowKeyRef, string partitionKeyRef, TEntity value, IDictionary<string, EntityProperty> dictionary, AzureTableDriverDynamic repository, Func<Func<Task>, TResult> onSuccessWithRollback, Func<TResult> onFailure)
        {
            throw new NotImplementedException();
        }

        IDictionary<string, EntityProperty> Serialize(object resultValue, Type typeToSave)
        {
            if (typeToSave.IsArray)
            {
                var objects = resultValue.ObjectToEnumerable().ToArray();
                var elementType = typeToSave.GetElementType();
                var rawValues = elementType
                    .GetPropertyAndFieldsWithAttributesInterface<IPersistInAzureStorageTables>()
                    .SelectMany(
                        tpl =>
                        {
                            var (member, persistor) = tpl;
                            var persistorType = persistor.GetType();
                            var convertValueMethod = persistorType.GetMethod(nameof(IPersistInAzureStorageTables.ConvertValue), BindingFlags.Public | BindingFlags.Instance);
                            var memberTypeInner = member.GetPropertyOrFieldType();
                            var convertValueMethodGeneric = convertValueMethod.MakeGenericMethod(memberTypeInner.AsArray());
                            var wrapperType = typeof(IWrapTableEntity<>).MakeGenericType(memberTypeInner.AsArray());
                            var defaultValue = wrapperType.GetDefault();
                            var valuesForMember = objects
                                .Select(
                                    obj =>
                                    {
                                        var v = member.GetPropertyOrFieldValue(obj);
                                        var x = convertValueMethodGeneric.Invoke(persistor, new object[] { member, v, defaultValue });
                                        var kvps = (KeyValuePair<string, EntityProperty>[])x;
                                        return kvps;
                                    })
                                .ToArray();
                            var keys = valuesForMember
                                .SelectMany(kvps => kvps.SelectKeys().Where(key => key.HasBlackSpace()))
                                .Distinct()
                                .SelectMany(
                                    key =>
                                    {
                                        var values = valuesForMember
                                            .Select(
                                                vfm =>
                                                {
                                                    var ep = vfm
                                                        .Where(kvp => key.Equals(kvp.Key))
                                                        .First(
                                                            (kvp, next) =>
                                                            {
                                                                return kvp.Value;
                                                            },
                                                            () => new EntityProperty(new byte[] { }));
                                                    return ep;
                                                })
                                            .ToArray()
                                            .ToByteArrayOfEntityProperties();
                                        var ep = new EntityProperty(values);
                                        var kvps = StorageOverflowAttribute.ComputeOverflowValues(key, ep);
                                        return kvps;
                                        // return key.PairWithValue(ep);
                                    })
                                .ToArray();
                            return keys;
                        })
                    .ToDictionary();

                return rawValues;
            }
            throw new NotImplementedException();
        }

        object Deserialize(IDictionary<string, EntityProperty> properties, Type typeToSave)
        {
            if (typeToSave.IsArray)
            {
                var elementType = typeToSave.GetElementType();
                var propsAndValues = elementType
                    .GetPropertyAndFieldsWithAttributesInterface<IPersistInAzureStorageTables>()
                    .Select(
                        tpl =>
                        {
                            var (member, persistor) = tpl;
                            var persistorType = persistor.GetType();
                            var getTablePropertyNameMethod = persistorType
                                .GetMethod(
                                    nameof(IPersistInAzureStorageTables.GetTablePropertyName),
                                    BindingFlags.Public | BindingFlags.Instance);
                            var memberType = member.GetPropertyOrFieldType();
                            var propertyName = (string)getTablePropertyNameMethod.Invoke(persistor, new object[] { member });
                            if (!properties.TryGetValue(propertyName, out EntityProperty entityProperty))
                                return (false, member, default(object[]));

                            // var values = entityProperty.BinaryValue.FromEdmTypedByteArray(memberType);
                            // return (true, member, values);

                            var epToParse = StorageOverflowAttribute.ParseOverflowValues(
                                propertyName, entityProperty, properties);

                            var values = epToParse.Value.BinaryValue
                                .FromEdmTypedByteArrayToEntityProperties(memberType)
                                .Select(
                                    ep =>
                                    {
                                        return persistor.GetMemberValue(member,
                                            new Dictionary<string, EntityProperty>()
                                            {
                                                {propertyName, ep}
                                            },
                                            () => memberType.GetDefault());
                                    })
                                .ToArray();

                            return (true, member, values);
                        })
                    .SelectWhere()
                    .ToArray();

                var lengths = propsAndValues.Select(tpl => tpl.Item2.Length).ToArray();
                if(lengths.None())
                    return elementType.ConstructEmptyArray();

                var min = lengths.Min();
                var max = lengths.Max();
                if (min != max)
                    return elementType.ConstructEmptyArray();

                var deserializedValue = elementType
                    .ConstructEnumerableOfType(min)
                    .Select(
                        (item, index) =>
                        {
                            foreach (var (prop, values) in propsAndValues)
                            {
                                var value = values[index];
                                var itemUpdated = prop.SetPropertyOrFieldValue(item, value);
                            }
                            return item;
                        })
                    .ToArray();
                return deserializedValue
                    .CastArray(elementType);
            }
            throw new NotImplementedException();
        }

        public async Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo,
            IAzureStorageTableEntity<TEntity> updatedEntity, IAzureStorageTableEntity<TEntity> existingEntity,
            AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var newValue = memberInfo.GetValue(updatedEntity.Entity);
            if (newValue == default)
                return onSuccessWithRollback(() => true.AsTask());

            if (newValue.GetType().IsAssignableTo(typeof(System.Delegate)))
            {
                var dele = (Delegate)newValue;
                var deleType = dele.Target.GetType();
                if (deleType.DeclaringType.GUID == typeof(StorageSubtableAttribute).GUID)
                    return onSuccessWithRollback(() => true.AsTask());
            }

            var newValueType = newValue.GetType();
            if (!newValueType.IsSubClassOfGeneric(typeof(Func<>)))
            {
                return onSuccessWithRollback(() => true.AsTask());
            }

            var rowKeyRef = updatedEntity.RowKey;
            var partitionKeyRef = updatedEntity.PartitionKey;

            var memberType = typeof(TEntity);
            var taskValue = newValue.ExecuteFunction(out Type taskType);
            var resultValue = await taskValue.CastAsTaskObjectAsync(out Type typeToSave);
            var rawValues = Serialize(resultValue, typeToSave);

            ITableEntity subtableEntity = new SubtableEntity(rowKeyRef, partitionKeyRef, rawValues);
            var tableName = StorageLookupAttribute.GetMemberTableName(memberInfo);
            var tableRef = repository.TableClient.GetTableReference(tableName);
            return await repository.InsertOrReplaceAsync(subtableEntity, new E5CloudTable(tableRef),
                (created, tr) => onSuccessWithRollback(() => 1.AsTask()));
        }

        public async Task<TResult> ExecuteDeleteAsync<TEntity, TResult>(MemberInfo memberInfo, string rowKeyRef, string partitionKeyRef, TEntity value, IDictionary<string, EntityProperty> dictionary, AzureTableDriverDynamic repository, Func<Func<Task>, TResult> onSuccessWithRollback, Func<TResult> onFailure)
        {
            var propertyValue = memberInfo.GetValue(value);
            if (propertyValue == default)
                return onSuccessWithRollback(() => true.AsTask());

            var newValueType = propertyValue.GetType();
            if (!newValueType.IsSubClassOfGeneric(typeof(Func<>)))
            {
                return onSuccessWithRollback(() => true.AsTask());
            }
            var taskValue = propertyValue.ExecuteFunction(out Type taskType);
            var resultValue = await taskValue.CastAsTaskObjectAsync(out Type typeToSave);
            var rawValues = Serialize(resultValue, typeToSave);

            ITableEntity subtableEntity = new SubtableEntity(rowKeyRef, partitionKeyRef, rawValues);
            var tableName = StorageLookupAttribute.GetMemberTableName(memberInfo);
            var tableRef = repository.TableClient.GetTableReference(tableName);
            return await repository.DeleteAsync(subtableEntity, tableRef,
                () => onSuccessWithRollback(() => 1.AsTask()),
                () => onSuccessWithRollback(() => 1.AsTask()),
                onFailure: (codes, why) => onSuccessWithRollback(() => 1.AsTask()));
        }

        public IEnumerable<IBatchModify> GetBatchCreateModifier<TEntity>(MemberInfo member, string rowKey, string partitionKey, TEntity entity, IDictionary<string, EntityProperty> serializedEntity)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IBatchModify> GetBatchDeleteModifier<TEntity>(MemberInfo member, string rowKey, string partitionKey, TEntity entity, IDictionary<string, EntityProperty> serializedEntity)
        {
            throw new NotImplementedException();
        }

        public IAzureStorageTableEntity<TEntity> GetEntity<TEntity>(TEntity entity)
        {
            throw new NotImplementedException();
        }

        public TEntity CreateEntityInstance<TEntity>(string rowKey, string partitionKey,
            IDictionary<string, EntityProperty> properties, string etag, DateTimeOffset lastUpdated)
        {
            var value = Deserialize(properties, typeof(TEntity));
            return (TEntity)value;
        }

        private class SubtableEntity : ITableEntity
        {
            IDictionary<string, EntityProperty> keyValuePairs;

            public string RowKey
            {
                get;
                set;
            }

            public string PartitionKey
            {
                get;
                set;
            }

            public string ETag
            {
                get
                {
                    return "*";
                }
                set
                {
                }
            }

            public SubtableEntity(string rowKey, string partitionKey,
                IDictionary<string , EntityProperty> keyValuePairs)
            {
                this.RowKey = rowKey;
                this.PartitionKey = partitionKey;
                this.keyValuePairs = keyValuePairs;
            }

            public DateTimeOffset Timestamp { get; set; }

            public void ReadEntity(IDictionary<string, EntityProperty> properties, OperationContext operationContext)
            {
                throw new NotImplementedException();
            }

            public IDictionary<string, EntityProperty> WriteEntity(OperationContext operationContext)
            {
                return keyValuePairs;
            }
        }

    }
}

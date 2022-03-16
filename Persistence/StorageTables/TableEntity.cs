// This needs to be depricated. It has been replaced by the StorageTableAttribute or any other
// class that implements IProvideTable and/or IProvideEntity

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;
using EastFive.Persistence.Azure.StorageTables.Driver;

namespace EastFive.Persistence.Azure.StorageTables
{
    /// <summary>
    /// This needs to be depricated. It has been replaced by the StorageTableAttribute or any other
    /// class that implements IProvideTable and/or IProvideEntity
    /// </summary>
    /// <typeparam name="EntityType"></typeparam>
    public class TableEntity<EntityType> : IWrapTableEntity<EntityType>, IAzureStorageTableEntity<EntityType>
    {
        public EntityType Entity { get; private set; }

        protected string rawRowKey;
        public string RawRowKey => rawRowKey;

        protected string rawPartitionKey;
        public string RawPartitionKey => rawPartitionKey;

        protected IDictionary<string, EntityProperty> rawProperties;
        public IDictionary<string, EntityProperty> RawProperties => rawProperties;

        public virtual string RowKey
        {
            get
            {
                var properties = typeof(EntityType)
                    .GetMembers()
                    .ToArray();

                var rowKeyModificationProperties = properties
                       .Where(propInfo => propInfo.ContainsAttributeInterface<IModifyAzureStorageTableRowKey>())
                       .Select(propInfo => propInfo.GetAttributesInterface<IModifyAzureStorageTableRowKey>().PairWithKey(propInfo))
                       .Where(propInfoKvp => propInfoKvp.Value.Any());
                if (!rowKeyModificationProperties.Any())
                    throw new Exception("Entity does not contain row key attribute");
                
                var rowKeyModificationProperty = rowKeyModificationProperties.First();
                var rowKeyProperty = rowKeyModificationProperty.Key;
                var rowKeyGenerator = rowKeyModificationProperty.Value.First();
                var rowKeyValue = rowKeyGenerator.GenerateRowKey(this.Entity, rowKeyProperty);
                return rowKeyValue;

            }
            set
            {
                rawRowKey = value;
            }
        }

        public virtual string PartitionKey
        {
            get
            {
                var partitionModificationProperties = typeof(EntityType)
                    .GetMembers()
                    .Where(propInfo => propInfo.ContainsAttributeInterface<IModifyAzureStorageTablePartitionKey>())
                    .Select(propInfo => propInfo.GetAttributesInterface<IModifyAzureStorageTablePartitionKey>().PairWithKey(propInfo))
                    .Where(propInfoKvp => propInfoKvp.Value.Any());
                if (!partitionModificationProperties.Any())
                    throw new Exception("Entity does not contain partition key attribute");

                var partitionModificationProperty = partitionModificationProperties.First();
                var partitionKeyProperty = partitionModificationProperty.Key;
                var partitionKeyGenerator = partitionModificationProperty.Value.First();

                var partitionKey = partitionKeyGenerator.GeneratePartitionKey(this.RowKey, this.Entity, partitionKeyProperty);
                return partitionKey;
            }
            set
            {
                rawPartitionKey = value;
            }
        }

        public DateTimeOffset Timestamp { get; set; }

        public virtual string ETag { get; set; }

        private IEnumerable<KeyValuePair<MemberInfo, IPersistInAzureStorageTables>> StorageProperties
        {
            get
            {
                var type = typeof(EntityType);
                return type.StorageProperties();
            }
        }

        public void ReadEntity(IDictionary<string, EntityProperty> properties, OperationContext operationContext)
        {
            this.rawProperties = properties;
            this.Entity = CreateEntityInstance(properties);
        }

        public static EntityType CreateEntityInstance(IDictionary<string, EntityProperty> properties)
        {
            var entity = Activator.CreateInstance<EntityType>();
            var storageProperties = typeof(EntityType).StorageProperties();
            foreach (var propInfoAttribute in storageProperties)
            {
                var propInfo = propInfoAttribute.Key;
                var attr = propInfoAttribute.Value;
                var value = attr.GetMemberValue(propInfo, properties);
                propInfo.SetValue(ref entity, value);
            }
            return entity;
        }

        public IDictionary<string, EntityProperty> WriteEntity(OperationContext operationContext)
        {
            var valuesToStore = StorageProperties
                .SelectMany(
                    (propInfoAttribute) =>
                    {
                        var propInfo = propInfoAttribute.Key;
                        var attr = propInfoAttribute.Value;
                        var value = propInfo.GetValue(this.Entity);
                        return attr.ConvertValue(value, propInfo);
                    })
                .ToDictionary();
            return valuesToStore;
        }

        internal static IAzureStorageTableEntity<TEntity> Create<TEntity>(TEntity entity, string etag = "*")
        {
            var creatableEntity = new TableEntity<TEntity>();
            creatableEntity.Entity = entity;
            creatableEntity.ETag = etag;
            return creatableEntity;
        }

        #region IAzureStorageTableEntity
        public Task<TResult> ExecuteCreateModifiersAsync<TResult>(AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<MemberInfo[], TResult> onFailure)
        {
            var hasModifiers = typeof(EntityType)
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<IModifyAzureStorageTableSave>())
                .Any();
            if (hasModifiers)
                throw new NotImplementedException("Please use the non-depricated StorageTableAttribute with modifier classes");

            return onSuccessWithRollback(
                () => 1.AsTask()).AsTask();
        }

        public Task<TResult> ExecuteInsertOrReplaceModifiersAsync<TResult>(AzureTableDriverDynamic repository, 
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<MemberInfo[], TResult> onFailure)
        {
            var hasModifiers = typeof(EntityType)
                .GetPropertyOrFieldMembers()
                .Where(member => member.ContainsAttributeInterface<IModifyAzureStorageTableSave>())
                .Any();
            if (hasModifiers)
                throw new NotImplementedException("Please use the non-depricated StorageTableAttribute with modifier classes");

            return onSuccessWithRollback(
                () => 1.AsTask()).AsTask();
        }

        public Task<TResult> ExecuteUpdateModifiersAsync<TResult>(IAzureStorageTableEntity<EntityType> current, AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<MemberInfo[], TResult> onFailure)
        {
            var hasModifiers = typeof(EntityType)
                   .GetPropertyOrFieldMembers()
                   .Where(member => member.ContainsAttributeInterface<IModifyAzureStorageTableSave>())
                   .Any();
            if (hasModifiers)
                throw new NotImplementedException("Please use the non-depricated StorageTableAttribute with modifier classes");

            return onSuccessWithRollback(
                () => 1.AsTask()).AsTask();
        }

        public Task<TResult> ExecuteDeleteModifiersAsync<TResult>(AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<MemberInfo[], TResult> onFailure)
        {
            var hasModifiers = typeof(EntityType)
                   .GetPropertyOrFieldMembers()
                   .Where(member => member.ContainsAttributeInterface<IModifyAzureStorageTableSave>())
                   .Any();
            if (hasModifiers)
                throw new NotImplementedException("Please use the non-depricated StorageTableAttribute with modifier classes");

            return onSuccessWithRollback(
                () => 1.AsTask()).AsTask();
        }

        #endregion
    }
}

﻿using BlackBarLabs;
using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence.Azure.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class MigratePartitionAttribute : Attribute,
        IModifyAzureStorageTableSave
    {
        public Type From { get; set; }

        public Type To { get; set; }

        private class TypeWrapper : ITableEntity
        {
            public string PartitionKey { get; set; }

            public string RowKey { get; set; }

            public DateTimeOffset Timestamp { get; set; }

            public string ETag { get; set; }

            public IDictionary<string, EntityProperty> properties;

            public TypeWrapper(string rowKey, string partitionKey, IDictionary<string, EntityProperty> properties)
            {
                RowKey = rowKey;
                PartitionKey = partitionKey;
                this.properties = properties;
            }

            public void ReadEntity(IDictionary<string, EntityProperty> properties, OperationContext operationContext)
            {
                throw new NotImplementedException();
            }

            public IDictionary<string, EntityProperty> WriteEntity(OperationContext operationContext)
            {
                return properties;
            }
        }

        public virtual Task<TResult> ExecuteCreateAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKey, string partitionKey,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure)
        {
            var to = (IModifyAzureStorageTablePartitionKey)Activator.CreateInstance(this.To);
            var toPartitionKey = to.GeneratePartitionKey(rowKey, value, memberInfo);
            var typeWrapper = new TypeWrapper(rowKey, toPartitionKey, dictionary);
            return repository.CreateAsync<TEntity, TResult>(typeWrapper,
                (entity) => onSuccessWithRollback(
                    () => repository.DeleteAsync<TEntity, bool>(entity,
                        () => true,
                        () => true)),
                () => onSuccessWithRollback(() => false.AsTask()));
        }

        public Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo, 
                string rowKey, string partitionKey, 
                TEntity valueExisting, IDictionary<string, EntityProperty> dictionaryExisting,
                TEntity valueUpdated, IDictionary<string, EntityProperty> dictionaryUpdated, 
                AzureTableDriverDynamic repository, 
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            var to = (IModifyAzureStorageTablePartitionKey)Activator.CreateInstance(this.To);
            var toPartitionKey = to.GeneratePartitionKey(rowKey, valueUpdated, memberInfo);
            var typeWrapper = new TypeWrapper(rowKey, toPartitionKey, dictionaryUpdated);
            return repository.InsertOrReplaceAsync<TEntity, TResult>(typeWrapper,
                (created, newEntity) => onSuccessWithRollback(
                    () =>
                    {
                        if (created)
                            return repository.DeleteAsync<TEntity, bool>(newEntity,
                                () => true,
                                () => true);
                        var typeWrapperExisting = new TypeWrapper(rowKey, toPartitionKey, dictionaryExisting);
                        return repository.ReplaceAsync<TEntity, bool>(typeWrapperExisting,
                            () => true);
                    }));
        }

        public Task<TResult> ExecuteDeleteAsync<TEntity, TResult>(MemberInfo memberInfo, 
                string rowKey, string partitionKey,
                TEntity value, IDictionary<string, EntityProperty> dictionary, 
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback, 
            Func<TResult> onFailure)
        {
            var to = (IModifyAzureStorageTablePartitionKey)Activator.CreateInstance(this.To);
            var toPartitionKey = to.GeneratePartitionKey(rowKey, value, memberInfo);
            var typeWrapper = new TypeWrapper(rowKey, toPartitionKey, dictionary);
            return repository.DeleteAsync<TEntity, TResult>(typeWrapper,
                () => onSuccessWithRollback(
                    () =>
                    {
                        return repository.CreateAsync<TEntity, bool>(typeWrapper,
                            (createdEntity) => true,
                            () => true);
                    }),
                () => onSuccessWithRollback(
                    () => 1.AsTask()));
        }
    }
}

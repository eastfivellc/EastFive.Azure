using EastFive.Persistence.Azure.StorageTables.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Persistence.Azure.StorageTables
{
    public interface IModifyAzureStorageTableSave
    {
        Task<TResult> ExecuteCreateAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure);

        Task<TResult> ExecuteInsertOrReplaceAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure);
        
        Task<TResult> ExecuteUpdateAsync<TEntity, TResult>(MemberInfo memberInfo,
                IAzureStorageTableEntity<TEntity> updatedEntity, 
                IAzureStorageTableEntity<TEntity> existingEntity,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure);

        Task<TResult> ExecuteDeleteAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> dictionary,
                AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<TResult> onFailure);

        IEnumerable<IBatchModify> GetBatchCreateModifier<TEntity>(MemberInfo member,
            string rowKey, string partitionKey,
            TEntity entity, IDictionary<string, EntityProperty> serializedEntity);

        IEnumerable<IBatchModify> GetBatchUpdateModifier<TEntity>(MemberInfo member,
            string rowKey, string partitionKey,
            TEntity priorEntity,
            TEntity entity, IDictionary<string, EntityProperty> serializedEntity);

        IEnumerable<IBatchModify> GetBatchDeleteModifier<TEntity>(MemberInfo member,
            string rowKey, string partitionKey,
            TEntity entity, IDictionary<string, EntityProperty> serializedEntity);
    }

    public interface IRepairAzureStorageTableSave : IModifyAzureStorageTableSave
    {
        Task<TResult> RepairAsync<TEntity, TResult>(MemberInfo memberInfo,
                string rowKeyRef, string partitionKeyRef,
                TEntity value, IDictionary<string, EntityProperty> propertyAndValues,
                AzureTableDriverDynamic repository,
            Func<string, TResult> onRepaired,
            Func<TResult> onNoChangesNecessary);
    }
}

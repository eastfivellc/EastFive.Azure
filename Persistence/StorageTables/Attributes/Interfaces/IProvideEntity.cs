using EastFive.Persistence.Azure.StorageTables.Driver;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    interface IProvideEntity
    {
        IAzureStorageTableEntity<TEntity> GetEntity<TEntity>(TEntity entity);

        TEntity CreateEntityInstance<TEntity>(string rowKey, string partitionKey, 
            IDictionary<string, EntityProperty> properties,
            string etag, DateTimeOffset lastUpdated);
    }

    public interface IAzureStorageTableEntity<TEntity> : ITableEntity
    {
        TEntity Entity { get; }

        Task<TResult> ExecuteCreateModifiersAsync<TResult>(AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<MemberInfo[], TResult> onFailure);

        Task<TResult> ExecuteInsertOrReplaceModifiersAsync<TResult>(AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<MemberInfo[], TResult> onFailure);

        Task<TResult> ExecuteUpdateModifiersAsync<TResult>(IAzureStorageTableEntity<TEntity> current, AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<MemberInfo[], TResult> onFailure);

        Task<TResult> ExecuteDeleteModifiersAsync<TResult>(AzureTableDriverDynamic repository,
            Func<Func<Task>, TResult> onSuccessWithRollback,
            Func<MemberInfo[], TResult> onFailure);
    }

    public interface IBatchModify
    {
        /// <summary>
        /// This key will be equal for modifications that can use the same resource
        /// </summary>
        string GroupingKey { get; }

        int? GroupLimit { get; }

        Task<TResult> CreateOrUpdateAsync<TResult>(
            AzureTableDriverDynamic repository,
            Func<object, Func<object, Task>, Task<TResult>> callback);
        object Modify(object resource);
    }

    public interface IWriteBatchModify : IBatchModify
    {
        object InsertBatchInstruction(object resource);
    }

    public interface IAzureStorageTableEntityBatchable
    {
        IBatchModify[] BatchCreateModifiers();

        IBatchModify[] BatchUpdateModifiers(object priorEntity);

        IBatchModify[] BatchInsertOrReplaceModifiers();

        IBatchModify[] BatchDeleteModifiers();
    }

}

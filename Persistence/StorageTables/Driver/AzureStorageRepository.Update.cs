using System;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;
using BlackBarLabs.Extensions;
using EastFive.Extensions;
using EastFive.Azure.StorageTables.Driver;
using EastFive;
using EastFive.Linq.Async;

namespace BlackBarLabs.Persistence.Azure.StorageTables
{
    public partial class AzureStorageRepository
    {
        public delegate TResult UpdateDelegate<TData, TResult>(TData currentStorage, SaveDocumentDelegate<TData> saveNew);
        public async Task<TResult> UpdateAsync<TData, TResult>(Guid documentId,
            UpdateDelegate<TData, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default(RetryDelegateAsync<Task<TResult>>),
            Func<string, string> mutatePartition = default(Func<string, string>))
            where TData : class, ITableEntity
        {
            var rowKey = documentId.AsRowKey();
            var partitionKey = rowKey.GeneratePartitionKey();
            if (!mutatePartition.IsDefaultOrNull())
                partitionKey = mutatePartition(partitionKey);
            return await UpdateAsync(rowKey, partitionKey, onUpdate, onNotFound);
        }

        public async Task<TResult> UpdateWithPartitionAsync<TData, TResult>(Guid documentId, string partitionKey,
            UpdateDelegate<TData, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default(RetryDelegateAsync<Task<TResult>>),
            Func<string, string> mutatePartition = default(Func<string, string>))
            where TData : class, ITableEntity
        {
            if (partitionKey.IsNullOrWhiteSpace())
                return await UpdateAsync(documentId, onUpdate, onNotFound, onTimeoutAsync, mutatePartition);

            var rowKey = documentId.AsRowKey();
            if (!mutatePartition.IsDefaultOrNull())
                partitionKey = mutatePartition(partitionKey);
            return await UpdateAsync(rowKey, partitionKey, onUpdate, onNotFound);
        }

        public async Task<TResult> UpdateAsyncAsync<TData, TResult>(Guid documentId,
            UpdateDelegate<TData, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default(Func<Task<TResult>>),
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default(RetryDelegateAsync<Task<TResult>>),
            Func<string, string> mutatePartition = default(Func<string, string>))
            where TData : class, ITableEntity
        {
            var rowKey = documentId.AsRowKey();
            var partitionKey = rowKey.GeneratePartitionKey();
            if (!mutatePartition.IsDefaultOrNull())
                partitionKey = mutatePartition(partitionKey);
            return await UpdateAsyncAsync(rowKey, partitionKey, onUpdate, onNotFound);
        }

        public async Task<TResult> UpdateAsync<TData, TResult>(Guid documentId, string partitionKey,
            UpdateDelegate<TData, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default(RetryDelegateAsync<Task<TResult>>))
            where TData : class, ITableEntity
        {
            var rowKey = documentId.AsRowKey();
            return await UpdateAsync(rowKey, partitionKey, onUpdate, onNotFound);
        }

        public Task<TResult> UpdateAsync<TData, TResult>(string rowKey, string partitionKey,
            UpdateDelegate<TData, Task<TResult>> onUpdate,
            Func<TResult> onNotFound = default(Func<TResult>),
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default(RetryDelegateAsync<Task<TResult>>))
            where TData : class, ITableEntity
        {
            return UpdateAsyncAsync(rowKey, partitionKey, onUpdate, onNotFound.AsAsyncFunc(), onTimeoutAsync);
        }

        public async Task<TResult> UpdateAsyncAsync<TData, TResult>(string rowKey, string partitionKey,
            UpdateDelegate<TData, Task<TResult>> onUpdate,
            Func<Task<TResult>> onNotFound = default(Func<Task<TResult>>),
            RetryDelegateAsync<Task<TResult>> onTimeoutAsync = default(RetryDelegateAsync<Task<TResult>>))
            where TData : class, ITableEntity
        {
            return await await FindByIdAsync(rowKey, partitionKey,
                async (TData currentStorage) =>
                {
                    var resultGlobal = default(TResult);
                    var useResultGlobal = false;
                    var resultLocal = await onUpdate.Invoke(currentStorage,
                        async (documentToSave) =>
                        {
                            useResultGlobal = await await UpdateIfNotModifiedAsync(documentToSave,
                                () => false.ToTask(),
                                async () =>
                                {
                                    if (onTimeoutAsync.IsDefaultOrNull())
                                        onTimeoutAsync = GetRetryDelegateContentionAsync<Task<TResult>>();

                                    resultGlobal = await await onTimeoutAsync(
                                        async () => await UpdateAsyncAsync(rowKey, partitionKey, onUpdate, onNotFound, onTimeoutAsync),
                                        (numberOfRetries) => { throw new Exception("Failed to gain atomic access to document after " + numberOfRetries + " attempts"); });
                                    return true;
                                },
                                onTimeout: GetRetryDelegate());
                        });
                    return useResultGlobal ? resultGlobal : resultLocal;
                },
                onNotFound,
                default(Func<ExtendedErrorInformationCodes, string, Task<TResult>>),
                GetRetryDelegate());
        }
    }
}

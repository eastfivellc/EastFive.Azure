using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using EastFive.Serialization;
using EastFive.Extensions;
using BlackBarLabs.Persistence.Azure.StorageTables;
using System.Runtime.Serialization;
using EastFive.Linq.Async;
using System.Text;

namespace EastFive.Azure.Synchronization.Persistence
{
    public class MutexDocument : TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id => Guid.Parse(this.RowKey);
        
        public long? WhenLastUnlocked { get; set; }

        public long? WhenLocked { get; set; }

        public bool Locked { get; set; }

        public Guid IntegrationId { get; set; }

        public string Key { get; set; }

        public string Action { get; set; }

        private static Guid GetId(Guid integrationId, string resourceKey, string action)
        {
            return integrationId
                .ToByteArray()
                .Concat(Encoding.UTF8.GetBytes(resourceKey))
                .Concat(Encoding.UTF8.GetBytes(action))
                .ToArray()
                .MD5HashGuid();
        }

        internal static Task<TResult> DeleteAsync<TResult>(Guid integrationId, string resourceKey, string action,
            Func<TResult> onDeleted,
            Func<TResult> onNotFound)
        {
            return AzureStorageRepository.Connection(
                connection =>
                {
                    var mutexId = GetId(integrationId, resourceKey, action);
                    return connection.DeleteIfAsync<MutexDocument, TResult>(mutexId,
                        async (mutexDocReal, deleteMutexAsync) =>
                        {
                            await deleteMutexAsync();
                            return onDeleted();
                        },
                        () => onNotFound());
                });
        }

        public interface ILockResult<T>
        {
            T Result { get; }
        }

        private struct LockResult<T> : ILockResult<T>
        {
            private T result;

            public LockResult(T result)
            {
                this.result = result;
            }

            public T Result => result;
        }

        internal static Task<TResult> SynchronizeLockedAsync<TResult>(Guid integrationId, string resourceKey, string action,
            Func<TimeSpan?, // time since last sync
                Func<TResult, Task<ILockResult<TResult>>>, 
                Func<TResult, Task<ILockResult<TResult>>>, 
                Task<ILockResult<TResult>>> onLockAquired,
            Func<int,       // retry count
                TimeSpan,   // retry duration
                TimeSpan?,  // time since last sync
                TimeSpan?,  // time locked
                Func<Task<TResult>>,
                Func<Task<TResult>>,
                Task<TResult>> onAlreadyLocked)
        {
            var mutexId = GetId(integrationId, resourceKey, action);
            return AzureStorageRepository.Connection(
                async connection =>
                {
                    TimeSpan? ComputeDuration(long? last)
                    {
                        var duration = last.HasValue ?
                                (DateTime.UtcNow - new DateTime(last.Value, DateTimeKind.Utc))
                                :
                                default(TimeSpan?);
                        return duration;
                    }

                    var lockResult = await await connection.LockedUpdateAsync<MutexDocument, Task<ILockResult<TResult>>>(mutexId,
                        mutexDoc => mutexDoc.Locked,
                        (mutexDoc, unlockAndUpdate, unlock) =>
                        {
                            var lastSyncDuration = ComputeDuration(mutexDoc.WhenLastUnlocked);
                            return onLockAquired(lastSyncDuration,
                                async r =>
                                {
                                    await unlockAndUpdate(
                                        (doc, save) =>
                                        {
                                            doc.WhenLastUnlocked = DateTime.UtcNow.Ticks;
                                            doc.WhenLocked = default(long?);  // not necessary, but it's nice that this is null when Locked = False
                                            return save(doc);
                                        });
                                    return new LockResult<TResult>(r);
                                },
                                async (r) =>
                                {
                                    await unlockAndUpdate(
                                        (doc, save) =>
                                        {
                                            doc.WhenLocked = default(long?);  // not necessary, but it's nice that this is null when Locked = False
                                            return save(doc);
                                        });
                                    return new LockResult<TResult>(r);
                                }).AsTask();
                        },
                        async () =>
                        {
                            return await await connection.CreateAsync(mutexId,
                                new MutexDocument
                                {
                                    Locked = false,
                                    WhenLastUnlocked = default(long?),
                                    WhenLocked = default(long?),
                                    IntegrationId = integrationId,
                                    Key = resourceKey,
                                    Action = action,
                                },
                                async () =>
                                {
                                    var retryResult = await SynchronizeLockedAsync(integrationId, resourceKey, action, onLockAquired, onAlreadyLocked);
                                    return new LockResult<TResult>(retryResult);
                                },
                                async () =>
                                {
                                    var retryResult = await SynchronizeLockedAsync(integrationId, resourceKey, action, onLockAquired, onAlreadyLocked);
                                    return new LockResult<TResult>(retryResult);
                                });
                        },
                        onAlreadyLocked:
                            async (retryCount, retryDuration, mutexDoc, continueAquiring, force) =>
                            {
                                var lastSyncDuration = ComputeDuration(mutexDoc.WhenLastUnlocked);
                                var lockedDuration = ComputeDuration(mutexDoc.WhenLocked);
                                var lockCompleteResponse = await onAlreadyLocked(retryCount, retryDuration, lastSyncDuration, lockedDuration,
                                    async () =>
                                    {
                                        var lockResponse = await await continueAquiring();
                                        return lockResponse.Result;
                                    },
                                    async () =>
                                    {
                                        var lockResponse = await await force();
                                        return lockResponse.Result;
                                    });
                                return (new LockResult<TResult>(lockCompleteResponse)).AsTask<ILockResult<TResult>>();
                            },
                        mutateUponLock:
                            (doc) =>
                            {
                                doc.WhenLocked = DateTime.UtcNow.Ticks;
                                return doc;
                            });
                    return lockResult.Result;
                });
        }
    }
}
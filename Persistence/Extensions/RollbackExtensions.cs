using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using EastFive;
using EastFive.Extensions;
using BlackBarLabs.Persistence.Azure.StorageTables;

namespace EastFive.Persistence.Azure
{
    public static class RollbackExtensions
    {

        private struct Carry<T>
        {
            public T carry;
        }

        public class UpdateCallback<T>
        {
            private UpdateCallback(T t, bool success, bool found, bool save, bool reject)
            {
                this.t = t;
                this.success = success;
                this.found = found;
                this.save = save;
                this.rejected = reject;
            }

            private UpdateCallback(bool success, bool found, bool save, bool reject)
            {
                this.t = default(T);
                this.success = success;
                this.found = found;
                this.save = save;
                this.rejected = reject;
            }

            internal T t;
            internal bool success;
            internal bool found;
            internal bool save;
            internal bool rejected;

            internal static UpdateCallback<T> Save(T t)
            {
                return new UpdateCallback<T>(t, true, true, true, false);
            }
            internal static UpdateCallback<T> SuccessNoSave()
            {
                return new UpdateCallback<T>(true, true, false, false);
            }
            internal static UpdateCallback<T> NotFound()
            {
                return new UpdateCallback<T>(false, false, false, false);
            }

            internal static UpdateCallback<T> Reject()
            {
                return new UpdateCallback<T>(false, true, false, true);
            }
        }

        public static void AddTaskUpdate<T, TRollback, TDocument>(this RollbackAsync<TRollback> rollback,
            Guid docId,
            Func<
                    TDocument,
                    Func<T, UpdateCallback<T>>, // Save + Success
                    Func<UpdateCallback<T>>, // No Save + Success
                    Func<UpdateCallback<T>>, // Reject
                UpdateCallback<T>> mutateUpdate,
            Func<T, TDocument, bool> mutateRollback,
            Func<TRollback> onMutateRejected,
            Func<TRollback> onNotFound,
            AzureStorageRepository repo)
            where TDocument : class, ITableEntity
        {
            rollback.AddTask(
                async (success, failure) =>
                {
                    var r = await repo.UpdateAsync<TDocument, UpdateCallback<T>>(docId,
                        async (doc, save) =>
                        {
                            var passThrough = mutateUpdate(doc,
                                (passThroughSuccess) => UpdateCallback<T>.Save(passThroughSuccess),
                                () => UpdateCallback<T>.SuccessNoSave(),
                                () => UpdateCallback<T>.Reject());
                            if(passThrough.save)
                                await save(doc);
                            return passThrough;
                        },
                        () => UpdateCallback<T>.NotFound());

                    if (!r.found)
                        return failure(onNotFound());
                    if (!r.success)
                        return failure(onMutateRejected());

                    return success(
                        async () =>
                        {
                            if (r.save)
                                await repo.UpdateAsync<TDocument, bool>(docId,
                                    async (doc, save) =>
                                    {
                                        if (mutateRollback(r.t, doc))
                                            await save(doc);
                                        return true;
                                    },
                                    () => false);

                            // If this was not saved, there is no reason to do anything on the rollback
                        });

                });
        }

        public static void AddTaskUpdate<T, TRollback, TDocument>(this RollbackAsync<T, TRollback> rollback,
                Guid docId,
            Func<TDocument, T> mutateUpdate,
            Func<T, TDocument, bool> mutateRollback,
            Func<T> ifNotFound,
            AzureStorageRepository repo)
            where TDocument : class, ITableEntity
        {
            rollback.AddTask(
                async (success, failure) =>
                {
                    var r = await repo.UpdateAsync<TDocument, Carry<T>?>(docId,
                        async (doc, save) =>
                        {
                            var carry = mutateUpdate(doc);
                            await save(doc);
                            return new Carry<T>
                            {
                                carry = carry,
                            };
                        },
                        () => default(Carry<T>?));
                    if (r.HasValue)
                        return success(r.Value.carry,
                            async () =>
                            {
                                await repo.UpdateAsync<TDocument, bool>(docId,
                                    async (doc, save) =>
                                    {
                                        mutateRollback(r.Value.carry, doc);
                                        await save(doc);
                                        return true;
                                    },
                                    () => false);
                            });
                    return success(ifNotFound(), () => ((object)null).AsTask());
                });
        }

        public static void AddTaskCreate<TRollback, TDocument>(this RollbackAsync<TRollback> rollback,
            Guid docId, TDocument document,
            Func<TRollback> onAlreadyExists,
            AzureStorageRepository repo,
            Func<string, string> mutatePartition = default(Func<string, string>))
            where TDocument : class, ITableEntity
        {
            rollback.AddTask(
                async (success, failure) =>
                {
                    return await repo.CreateAsync(docId, document,
                        () => success(
                            async () =>
                            {
                                await repo.DeleteIfAsync<TDocument, bool>(docId,
                                    async (doc, delete) => { await delete(); return true; },
                                    () => false,
                                    mutatePartition: mutatePartition);
                            }),
                        () => failure(onAlreadyExists()),
                        mutatePartition:mutatePartition);
                });
        }

        
        public static void AddTaskCreateOrUpdate<TRollback, TDocument>(this RollbackAsync<TRollback> rollback,
            Guid docId,
            Func<bool, TDocument, bool> isMutated,
            Action<TDocument> mutateRollback,
            AzureStorageRepository repo)
            where TDocument : class, ITableEntity
        {
            rollback.AddTask(
                (success, failure) =>
                {
                    return repo.CreateOrUpdateAsync<TDocument, RollbackAsync<TRollback>.RollbackResult>(docId,
                        async (created, doc, save) =>
                        {
                            if (!isMutated(created, doc))
                                return success(
                                    async () =>
                                    {
                                        if (created)
                                            await repo.DeleteAsync(doc,
                                                () => true,
                                                () => false);
                                    });

                            await save(doc);
                            return success(
                                async () =>
                                {
                                    if (created)
                                    {
                                        await repo.DeleteIfAsync<TDocument, bool>(docId,
                                            async (docDelete, delete) =>
                                            {
                                                // TODO: Check etag if(docDelete.ET)
                                                await delete();
                                                return true;
                                            },
                                            () => false);
                                        return;
                                    }
                                    await repo.UpdateAsync<TDocument, bool>(docId,
                                        async (docRollback, saveRollback) =>
                                        {
                                            mutateRollback(docRollback);
                                            await saveRollback(docRollback);
                                            return true;
                                        },
                                        () => false);
                                });
                        });
                });
        }


        public static void AddTaskCreateOrUpdate<TRollback, TDocument>(this RollbackAsync<TRollback> rollback,
            Guid docId, string partitionKey,
            Func<bool, TDocument, bool> isMutated,
            Action<TDocument> mutateRollback,
            AzureStorageRepository repo)
            where TDocument : class, ITableEntity
        {
            rollback.AddTask(
                (success, failure) =>
                {
                    return repo.CreateOrUpdateAsync<TDocument, RollbackAsync<TRollback>.RollbackResult>(docId, partitionKey,
                        async (created, doc, save) =>
                        {
                            if (!isMutated(created, doc))
                                return success(
                                    async () =>
                                    {
                                        if (created)
                                            await repo.DeleteAsync(doc,
                                                () => true,
                                                () => false);
                                    });

                            await save(doc);
                            return success(
                                async () =>
                                {
                                    if (created)
                                    {
                                        await repo.DeleteIfAsync<TDocument, bool>(docId,
                                            async (docDelete, delete) =>
                                            {
                                                // TODO: Check etag if(docDelete.ET)
                                                await delete();
                                                return true;
                                            },
                                            () => false);
                                        return;
                                    }
                                    await repo.UpdateAsync<TDocument, bool>(docId,
                                        async (docRollback, saveRollback) =>
                                        {
                                            mutateRollback(docRollback);
                                            await saveRollback(docRollback);
                                            return true;
                                        },
                                        () => false);
                                });
                        });
                });
        }


        public static async Task<TRollback> ExecuteAsync<TRollback>(this RollbackAsync<TRollback> rollback,
            Func<TRollback> onSuccess)
        {
            return await rollback.ExecuteAsync(onSuccess, r => r);
        }
    }
}

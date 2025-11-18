using System;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using EastFive.Functional;
using EastFive.Extensions;
using System.Linq.Expressions;
using EastFive.Linq.Async;

namespace EastFive.Azure.Persistence.AzureStorageTables;

public static class StorageConvienceExtensions
{
    public static Task<TResult> StorageGetAsync<TStored1, TStored2, TResult>(
        this (IRef<TStored1>, IRef<TStored2>) entityRefs,
        Func<TStored1, TStored2, TResult> onFound,
        Func<TResult> onFirstNotFound,
        Func<TResult> onSecondNotFound)
        where TStored1 : IReferenceable
        where TStored2 : IReferenceable
    {
        Func<TStored1, TStored2, Task<TResult>> onBothFound = (t1, t2) => onFound(t1, t2).AsTask();
        var onCompiled = onBothFound.InvokeDelayed<TStored1, TStored2, TResult>(
            delayed1Callback:async (onSuccessAsync) => await await entityRefs.Item1.StorageGetAsync<TStored1, Task<TResult>>(
                async (t1) =>
                {
                    var temp = await onSuccessAsync(t1);
                    return temp;
                },
                () =>
                {
                    return onFirstNotFound().AsTask();
                }),
            delayed2Callback:async (onSuccessAsync) => await await entityRefs.Item2.StorageGetAsync<TStored2, Task<TResult>>(
                (t2) => onSuccessAsync(t2),
                () =>
                {
                    return onSecondNotFound().AsTask();
                }));
            return onCompiled();
    }

    public static async Task<TResult> StorageGetWithAssociatedAsync<TStored1, TStored2, TResult>(
            this IRef<TStored1> entityRef,
            Func<TStored1, IRef<TStored2>> associatedRefExpr,
        Func<TStored1, TStored2, TResult> onFound,
        Func<TResult> onFirstNotFound,
        Func<TResult> onSecondNotFound)
        where TStored1 : IReferenceable
        where TStored2 : IReferenceable
    {
        return await await entityRef
                .StorageGetAsync(
                    (entity1) =>
                    {
                        return associatedRefExpr(entity1)
                            .StorageGetAsync<TStored2, TResult>(
                                (entity2) =>
                                {
                                    return onFound(entity1, entity2);
                                },
                                () => onSecondNotFound());
                    },
                    () => onFirstNotFound().AsTask());
    }

    public static async Task<TResult> StorageGetByAssociatedPropertyAsync<TEntity, TIntermediateProperty, TAssociated, TResult>(
            this IRef<TEntity> entityRef,
            Func<TEntity, IRef<TIntermediateProperty>> getIntermediateRef,
            Expression<Func<TAssociated, IRef<TIntermediateProperty>>> idPropertyExpr,
        Func<TEntity, TAssociated[], TResult> onFound,
        Func<TResult> onFirstNotFound,
        Func<TResult> onSecondNotFound)
        where TEntity : IReferenceable
        where TIntermediateProperty : IReferenceable
        where TAssociated : IReferenceable
    {
        return await await entityRef
                .StorageGetAsync(
                    async (entity1) =>
                    {
                        var associatedRef = getIntermediateRef(entity1);
                        var entity2s = await associatedRef
                            .StorageGetByIdProperty(idPropertyExpr)
                            .ToArrayAsync();
                        return onFound(entity1, entity2s);
                    },
                    () => onSecondNotFound().AsTask());
    }

    public static async Task<TResult> StorageGetWithAssociatedAsync<TStored1, TStored2, TStored3, TResult>(
            this IRef<TStored1> entityRef,
            Func<TStored1, IRef<TStored2>> associatedRefExpr,
            Func<TStored1, IRefs<TStored3>> associatedRefExpr3,
        Func<TStored1, TStored2, TStored3[], TResult> onFound,
        Func<TResult> onFirstNotFound,
        Func<TResult> onSecondNotFound)
        where TStored1 : IReferenceable
        where TStored2 : IReferenceable
        where TStored3 : IReferenceable
    {
        return await await entityRef
                .StorageGetAsync(
                    async (entity1) =>
                    {
                        return await await associatedRefExpr(entity1)
                            .StorageGetAsync<TStored2, Task<TResult>>(
                                async (entity2) =>
                                {
                                    var entity3Refs = associatedRefExpr3(entity1);
                                    var entity3s = await entity3Refs
                                        .StorageGet()
                                        .ToArrayAsync();
                                    return onFound(entity1, entity2, entity3s);
                                },
                                () => onSecondNotFound().AsTask());
                    },
                    () => onFirstNotFound().AsTask());
    }

}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EastFive.Analytics;
using EastFive.Azure.Persistence;
using EastFive.Linq.Async;

namespace EastFive.Persistence.Azure.StorageTables
{
    public interface IProvideFindBy
    {
        TResult GetKeys<TResult>(
                MemberInfo memberInfo, Driver.AzureTableDriverDynamic repository,
                KeyValuePair<MemberInfo, object>[] queries,
            Func<IEnumerableAsync<IRefAst>, TResult> onQueriesMatched,
            Func<TResult> onQueriesDidNotMatch,
                ILogger logger = default);

        Task<TResult> GetLookupInfoAsync<TResult>(
                MemberInfo memberInfo, Driver.AzureTableDriverDynamic repository,
                KeyValuePair<MemberInfo, object>[] queries,
            Func<string, DateTime, int, TResult> onEtagLastModifedFound,
            Func<TResult> onNoLookupInfo);

        Task<PropertyLookupInformation[]> GetInfoAsync(
            MemberInfo memberInfo);
    }

    public interface IProvideFindByAsync
    {
        Task<TResult> GetKeysAsync<TEntity, TResult>(IRef<TEntity> value,
                Driver.AzureTableDriverDynamic repository, MemberInfo memberInfo,
            Func<IEnumerableAsync<IRefAst>, TResult> onRefFound,
            Func<TResult> onRefNotFound)
            where TEntity : IReferenceable;
    }
}

using EastFive.Extensions;
using EastFive.Linq.Async;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure
{
    public interface ICacheEntites
    {
        IEnumerableAsync<TEntity> ByQuery<TEntity>(string whereFilter, 
            Func<IEnumerableAsync<TEntity>> onCacheMiss);

        TResult ByRowPartitionKey<TEntity, TResult>(string rowKey, string partitionKey,
            Func<TEntity, TResult> onCacheHit,
            Func<Action<TEntity>, TResult> onCacheMiss);
    }

    public static class CacheEntitesNullSaveExtensions
    {
        public static IEnumerableAsync<TEntity> ByQueryEx<TEntity>(this ICacheEntites cache,
            string whereFilter,
            Func<IEnumerableAsync<TEntity>> onCacheMiss)
        {
            if (cache.IsDefaultOrNull())
                return onCacheMiss();
            return cache.ByQuery(whereFilter, onCacheMiss);
        }

        public static TResult ByRowPartitionKeyEx<TEntity, TResult>(this ICacheEntites cache, 
                string rowKey, string partitionKey,
            Func<TEntity, TResult> onCacheHit,
            Func<Action<TEntity>, TResult> onCacheMiss)
        {
            if (cache.IsDefaultOrNull())
                return onCacheMiss((e) => { });
            return cache.ByRowPartitionKey(rowKey, partitionKey,
                onCacheHit,
                onCacheMiss);
        }
    }
}

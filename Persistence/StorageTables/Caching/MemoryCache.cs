using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    public class MemoryCache : ICacheEntites, IDisposable
    {
        private object locker = new object();

        private Dictionary<Type, object> entities = new Dictionary<Type, object>();

        public int Misses
        {
            get
            {
                return entities.Sum(x => (x.Value as IEntityCache).Misses);
            }
        }

        public int Hits
        {
            get
            {
                return entities.Sum(x => (x.Value as IEntityCache).Hits);
            }
        }

        public TResult ByRowPartitionKey<TEntity, TResult>(string rowKey, string partitionKey,
            Func<TEntity, TResult> onCacheHit,
            Func<Action<TEntity>, TResult> onCacheMiss)
        {
            MemoryCache<TEntity> entityCache;
            lock (locker)
            {
                if (!entities.ContainsKey(typeof(TEntity)))
                    entities.Add(typeof(TEntity), new MemoryCache<TEntity>());
                entityCache = (MemoryCache<TEntity>)entities[typeof(TEntity)];
            }

            return entityCache.ByRowPartitionKey(rowKey, partitionKey,
                onCacheHit,
                onCacheMiss);
        }

        public IEnumerableAsync<TEntity> ByQuery<TEntity>(string whereFilter, 
            Func<IEnumerableAsync<TEntity>> onCacheMiss)
        {
            MemoryCache<TEntity> entityCache;
            lock (locker)
            {
                if (!entities.ContainsKey(typeof(TEntity)))
                    entities.Add(typeof(TEntity), new MemoryCache<TEntity>());
                entityCache = (MemoryCache<TEntity>)entities[typeof(TEntity)];
            }

            return entityCache.ByQuery(whereFilter, onCacheMiss);
        }

        public void Dispose()
        {
        }
    }

    public interface IEntityCache
    {
        int Misses { get; }
        int Hits { get; }
    }

    public class MemoryCache<TEntity> : IEntityCache //: IDisposable
    {
        public Func<Task<TEntity[]>> SourceAsync;

        private object entityLocker = new object();
        private IDictionary<string, IDictionary<string, TEntity>> entities = 
            new Dictionary<string, IDictionary<string, TEntity>>();

        private object queryLocker = new object();
        private IDictionary<string, IEnumerableAsync<TEntity>> queries =
            new Dictionary<string, IEnumerableAsync<TEntity>>();

        public int Misses
        {
            get
            {
                lock (entityLocker)
                {
                    lock (queryLocker)
                    {
                        return EntityMisses + QueryMisses;
                    }
                }
            }
        }

        public int Hits
        {
            get
            {
                lock (entityLocker)
                {
                    lock (queryLocker)
                    {
                        return EntityHits + QueryHits;
                    }
                }
            }
        }
        
        public int EntityMisses { get; private set; }

        public int EntityHits { get; private set; }

        public int QueryMisses { get; private set; }

        public int QueryHits { get; private set; }

        public TResult ByRowPartitionKey<TResult>(string rowKey, string partitionKey,
            Func<TEntity, TResult> onCacheHit,
            Func<Action<TEntity>, TResult> onCacheMiss)
        {
            IDictionary<string, TEntity> entityCache;
            TEntity entity = default;
            bool found = false;
            lock (entityLocker)
            {
                if (!entities.ContainsKey(partitionKey))
                    entities.Add(partitionKey, new Dictionary<string, TEntity>());
                entityCache = entities[partitionKey];
                if (entityCache.ContainsKey(rowKey))
                {
                    found = true;
                    entity = entityCache[rowKey];
                    EntityHits++;
                }
                else
                    EntityMisses++;
            }

            if (found)
                return onCacheHit(entity);

            return onCacheMiss(
                newEntity =>
                {
                    lock (entityLocker)
                    {
                        if(!entityCache.ContainsKey(rowKey))
                            entityCache.Add(rowKey, newEntity);
                    }
                });
        }

        public IEnumerableAsync<TEntity> ByQuery(string whereFilter,
            Func<IEnumerableAsync<TEntity>> onCacheMiss)
        {
            lock (queryLocker)
            {
                if (queries.ContainsKey(whereFilter))
                {
                    QueryHits++;
                    return queries[whereFilter];
                }
                QueryMisses++;
            }

            var entitiesSet = onCacheMiss();
            lock (queryLocker)
            {
                queries.Add(whereFilter, entitiesSet);
            }
            return entitiesSet
                .Select(
                    entity =>
                    {
                        var rowKey = entity.StorageGetRowKey();
                        var partitionKey = entity.StorageGetPartitionKey();
                        lock (entityLocker)
                        {
                            if (!entities.ContainsKey(partitionKey))
                                entities.Add(partitionKey, new Dictionary<string, TEntity>());
                            var entityCache = entities[partitionKey];
                            if (!entityCache.ContainsKey(rowKey))
                                entityCache.Add(rowKey, entity);
                        }
                        return entity;
                    });
            // TODO: OnComplete, replace IEnumerableAsync with results collection
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Persistence.Azure.StorageTables
{
    public interface IWrapTableEntity<TEntity> : ITableEntity
    {

        TEntity Entity { get; }

        string RawRowKey { get; }

        string RawPartitionKey { get; }

        IDictionary<string, EntityProperty> RawProperties { get; }
    }
}

using System;
using Microsoft.Azure.Cosmos.Table;

namespace BlackBarLabs.Persistence.Azure.StorageTables
{
    [Serializable]
    [Obsolete]
    public abstract class DocumentTemplate<TKey> : TableEntity, IDocument
    {
        protected DocumentTemplate()
        {
        }

        protected DocumentTemplate(TKey id)
        {
            RowKey = id.ToString().Replace("-", "");
            PartitionKey = RowKey.GeneratePartitionKey();
        }

        public int EntityState { get; set; }

    }
}

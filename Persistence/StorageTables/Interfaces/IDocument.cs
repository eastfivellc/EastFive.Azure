using Microsoft.Azure.Cosmos.Table;

namespace BlackBarLabs.Persistence.Azure.StorageTables
{
    public interface IDocument : ITableEntity
    {
        int EntityState { get; set; }
    }
}

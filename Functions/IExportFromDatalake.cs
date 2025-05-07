using System;
using EastFive.Azure.Persistence.StorageTables;
namespace EastFive.Azure.Functions
{
	public interface IExportFromDatalake : IReferenceable
    {
        AzureBlobFileSystemUri exportUri { get;}

        /// <summary>
        /// This is a hash that represents the current instance of the source data
        /// </summary>
        Guid sourceId { get; }
    }

    public interface IDataLakeItem
    {
        public int skip { get; set; }
        public string path { get; }
    }
}


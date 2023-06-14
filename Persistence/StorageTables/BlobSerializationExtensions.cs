using System;
using System.IO;
using System.Threading.Tasks;
using EastFive.Serialization.Parquet;

namespace EastFive.Azure.Persistence.AzureStorageTables
{
	public static class BlobSerializationExtensions
	{
        public static async Task<TResult> ReadParquetDataFromDataLakeAsync<TResource, TResult>(this string path, string containerName, 
            Func<TResource[], TResult> onLoaded,
            Func<TResult> onNotFound)
		{
            return await path.BlobLoadBytesAsync(containerName,
                (payerGapsParquetBytes, properties) =>
                {
                    using (var payerGapsParquetStream = new MemoryStream(payerGapsParquetBytes))
                    {
                        var claimsLines = payerGapsParquetStream.ParseParquet<TResource>(default, default);
                        return onLoaded(claimsLines);
                    }
                },
                onNotFound: () =>
                {
                    return onNotFound();
                },
                connectionStringConfigKey: EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString);
        }
	}
}


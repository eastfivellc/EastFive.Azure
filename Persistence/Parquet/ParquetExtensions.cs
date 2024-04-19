using System;
using System.Linq;
using System.Data;
using System.Threading.Tasks;
using System.Reflection.Metadata;

using Parquet;

using Azure.Storage.Blobs.Models;

using EastFive;
using EastFive.Extensions;
using EastFive.Serialization;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Serialization.Parquet;

namespace EastFive.Azure.Persistence
{
	public static class ParquetExtensions
	{
        public static IEnumerableAsync<BlobContentInfo> WriteToBlobAsParquet(this IDataReader data, AzureBlobFileSystemUri exportLocation, int rowsPerfile = 100000)
            => WriteToBlobAsParquetInternal(data, exportLocation, rowsPerfile: rowsPerfile).FoldTask();

        private static async Task<IEnumerableAsync<BlobContentInfo>> WriteToBlobAsParquetInternal(this IDataReader data, AzureBlobFileSystemUri exportLocation,
            int rowsPerfile)
        {
            var schema = data.GetParquetSchema();
            bool deleted = await exportLocation.BlobDeleteIfExistsAsync(
                () => true,
                connectionStringConfigKey: EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString);
            return data
                .ReadAsTuple()
                .Segment(rowsPerfile)
                .Select(
                    (segment, index) =>
                    {
                        var fileName = $"{index:00000}.parquet";
                        return exportLocation
                            .AppendToPath(fileName)
                            .BlobCreateOrUpdateAsync(
                                writeStreamAsync: async (stream) =>
                                {
                                    segment.WriteToParquetStream(schema, stream);
                                    await stream.FlushAsync();

                                    //using (var writer = new ParquetWriter(schema, stream))
                                    //{
                                    //    var table = segment
                                    //        .Aggregate(
                                    //            new global::Parquet.Data.Rows.Table(schema),
                                    //            (table, row) =>
                                    //            {
                                    //                var values = row
                                    //                    .Select(
                                    //                        col =>
                                    //                        {
                                    //                            if (col.value.IsNull())
                                    //                                return col.value;

                                    //                            if (col.value.GetType() == typeof(System.DBNull))
                                    //                                return null;

                                    //                            if (col.value.GetType() == typeof(DateTime))
                                    //                                return new DateTimeOffset(((DateTime)col.value));

                                    //                            //if (col.type == typeof(DateTime))
                                    //                            //    if (col.value.GetType() == typeof(DateTimeOffset))
                                    //                            //        return ((DateTimeOffset)col.value).DateTime;

                                    //                            return col.value;
                                    //                        });
                                    //                var parquetRow = new global::Parquet.Data.Rows.Row(values);
                                    //                table.Add(parquetRow);
                                    //                return table;
                                    //            });
                                    //    writer.Write(table);
                                    //}
                                    //await stream.FlushAsync();
                                },
                                (blobContentInfo) => blobContentInfo,
                                    contentTypeString: "application/vnd.apache.parquet",
                                    fileName: fileName,
                                    connectionStringConfigKey: EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString);
                    })
                .AsyncEnumerable();
        }
    }
}


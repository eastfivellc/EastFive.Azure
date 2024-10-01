using System;
using System.Linq;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection.Metadata;

using Parquet;

using Azure.Storage.Blobs.Models;

using EastFive;
using EastFive.Extensions;
using EastFive.Serialization;
using EastFive.Reflection;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Linq.Async;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Serialization.Parquet;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

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
                                },
                                (blobContentInfo) => blobContentInfo,
                                    contentTypeString: "application/vnd.apache.parquet",
                                    fileName: fileName,
                                    connectionStringConfigKey: EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString);
                    })
                .AsyncEnumerable();
        }

        public static string GetExportStatement(this IDataReader dataReader,
            string schemaName, string tableName, string dataSourceName, string fileFormat)
        {
            var createStatement = GenerateQuery();
            var withStatement = $"WITH(DATA_SOURCE = [{dataSourceName}],"
                + $"\n\tLOCATION = N'{schemaName}/{tableName}/**',"
                + $"\n\tFILE_FORMAT = [{fileFormat}])"
                // + $" REJECT_TYPE = VALUE,"
                // + $" REJECT_VALUE = 0)"
                + "\nGO";

            return createStatement + "\n" + withStatement;

            string GenerateQuery()
            {
                var fieldCount = dataReader.FieldCount;
                var properties = Enumerable
                    .Range(0, fieldCount)
                    .Select(
                        (index) =>
                        {
                            var name = dataReader.GetName(index);
                            var clrType = dataReader.GetFieldType(index);
                            var sqlType = GetSqlType(clrType);
                            return $"[{name}] {sqlType} NULL";

                            string GetSqlType(Type clrType)
                            {
                                if (clrType == typeof(int))
                                {
                                    return "[bigint]";
                                }
                                if (clrType == typeof(long))
                                {
                                    return "[bigint]";
                                }
                                if (clrType == typeof(float))
                                {
                                    return "[real]";
                                }
                                if (clrType == typeof(decimal))
                                {
                                    return "[float] (53)";
                                }
                                if (clrType == typeof(double))
                                {
                                    return "[float] (53)";
                                }
                                if (clrType == typeof(string))
                                {
                                    return "[nvarchar] (4000)";
                                }
                                if (clrType == typeof(DateTime))
                                {
                                    return "[datetime2] (7)";
                                }
                                if (clrType == typeof(bool))
                                {
                                    return "[BIT]";
                                }
                                if (clrType.TryGetNullableUnderlyingType(out Type underlyingType))
                                    return GetSqlType(underlyingType);
                                if (clrType.IsArray)
                                    return "[varchar] (max)";

                                throw new Exception("Type not supported for SQL export");
                            }
                        })
                    .Join(",\n\t\t");

                return $"CREATE EXTERNAL TABLE [{schemaName}].[{tableName}]"
                    + "\n\t("
                    + $"\n\t\t{properties}"
                    + "\n\t)";
            }
        }

        public static IEnumerableAsync<BlobContentInfo> WriteToBlobAsParquet<TEntity>(this IEnumerable<TEntity> data,
                AzureBlobFileSystemUri exportLocation, int rowsPerfile = 100000)
            => WriteToBlobAsParquetInternal(data, exportLocation, rowsPerfile: rowsPerfile).FoldTask();

        private static async Task<IEnumerableAsync<BlobContentInfo>> WriteToBlobAsParquetInternal<TEntity>(
            this IEnumerable<TEntity> data, AzureBlobFileSystemUri exportLocation, int rowsPerfile)
        {
            var schema = GetParquetSchema<TEntity>();
            bool deleted = await exportLocation.BlobDeleteIfExistsAsync(
                () => true,
                connectionStringConfigKey: EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString);

            var mappers = typeof(TEntity)
                .GetPropertyAndFieldsWithAttributesInterface<IMapParquetProperty>(inherit: true)
                .ToArray();

            return data
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
                                    GetRows(segment)
                                        .WriteToParquetStream(schema, stream);
                                    await stream.FlushAsync();
                                },
                                (blobContentInfo) => blobContentInfo,
                                    contentTypeString: "application/vnd.apache.parquet",
                                    fileName: fileName,
                                    connectionStringConfigKey: EastFive.Azure.AppSettings.Persistence.DataLake.ConnectionString);
                    })
                .AsyncEnumerable();

            IEnumerable<(string name, Type type, object value)[]> GetRows(IEnumerable<TEntity> entities)
            {
                return entities
                    .Select(
                        entity =>
                        {
                            return mappers
                                .Select(
                                    mapper =>
                                    {
                                        var value = mapper.Item2.GetParquetDataValue(entity, mapper.Item1);
                                        return value;
                                    })
                                .ToArray();
                        });
            }
        }

        public static global::Parquet.Data.Schema GetParquetSchema<TEntity>()
        {
            var fields = typeof(TEntity)
                .GetPropertyAndFieldsWithAttributesInterface<IMapParquetProperty>(inherit:true)
                .Select(
                    (tpl) =>
                    {
                        var (memberInfo, mapParquetProperty) = tpl;
                        var dataField = mapParquetProperty.GetParquetDataField(memberInfo);
                        return dataField;
                    })
                .ToArray();

            return new global::Parquet.Data.Schema(fields);
        }
    }
}


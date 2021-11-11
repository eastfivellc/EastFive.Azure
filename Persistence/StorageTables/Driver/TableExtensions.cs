using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Analytics;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Linq.Async;
using EastFive.Linq.Expressions;
using EastFive.Reflection;
using EastFive.Serialization;


namespace EastFive.Persistence.Azure.StorageTables.Driver
{
    public static class TableExtensions
    {
        #region BATCH

        public static async Task<TableBatchResult> ExecuteBatchWithCreateAsync(this CloudTable cloudTable, TableBatchOperation batch)
        {
            try
            {
                return await cloudTable.ExecuteBatchAsync(batch);
            }
            catch (StorageException storageException)
            {
                return await await storageException.ResolveCreate(cloudTable,
                    () => cloudTable.ExecuteBatchAsync(batch),
                    (errorCode, errorMessage) => throw storageException,
                    () => throw storageException);
            }
        }

        #endregion
    }
}


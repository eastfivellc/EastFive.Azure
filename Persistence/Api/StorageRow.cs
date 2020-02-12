using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Persistence.Azure.StorageTables;

using Newtonsoft.Json;

namespace EastFive.Azure.Persistence
{
    [FunctionViewController6(
        Route = "StorageRow",
        Resource = typeof(StorageRow),
        ContentType = "x-application/eastfive.azure.storage-row",
        ContentTypeVersion = "0.1")]
    [DataContract]
    public class StorageRow
    {
        #region Properties

        [JsonProperty]
        public string rowKey;

        [JsonProperty]
        public string partitionKey;

        [JsonProperty]
        public IDictionary<string, object> properties;

        #endregion
    }
}

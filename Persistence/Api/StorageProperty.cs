using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using EastFive.Api;
using EastFive.Persistence.Azure.StorageTables;

using Newtonsoft.Json;

namespace EastFive.Azure.Persistence
{
    [FunctionViewController(
        Route = "StorageProperty",
        Resource = typeof(StorageProperty),
        ContentType = "x-application/eastfive.azure.storage-property",
        ContentTypeVersion = "0.1")]
    [DataContract]
    [StorageTable]
    public class StorageProperty : IReferenceable
    {
        #region Properties

        [JsonIgnore]
        public Guid id => this.storagePropertyRef.id;

        public const string IdPropertyName = "id";
        [JsonProperty(PropertyName = IdPropertyName)]
        [ApiProperty(PropertyName = IdPropertyName)]
        public IRef<StorageProperty> storagePropertyRef;

        [JsonProperty]
        public string name;

        [JsonProperty]
        public Type type;
        internal MemberInfo member;

        #endregion
    }
}

using BlackBarLabs.Persistence.Azure.Attributes;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.Documents
{
    [Serializable]
    [DataContract]
    // commented out b/c the EastFive.Azure.Persistence.Documents.LookupDocument copy has this already
    //[StorageResource(typeof(StandardPartitionKeyGenerator), typeof(OnePlaceHexadecimalKeyGenerator))]  
    public class LookupDocument : TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id { get { return Guid.Parse(this.RowKey); } }

        public Guid Lookup { get; set; }
    }
}

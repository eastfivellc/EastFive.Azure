using Microsoft.Azure.Cosmos.Table;
using System;
using System.Runtime.Serialization;

namespace EastFive.Persistence.Azure.Documents
{
    [Serializable]
    [DataContract]
    // commented out b/c the EastFive.Azure.Persistence.Documents.LookupDocument copy has this already
    public class LookupDocument : TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id { get { return Guid.Parse(this.RowKey); } }

        public Guid Lookup { get; set; }
    }
}

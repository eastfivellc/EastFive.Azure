using System;
using System.Runtime.Serialization;
using Microsoft.Azure.Cosmos.Table;
using EastFive.Serialization;
using System.Linq;
using BlackBarLabs.Extensions;
using EastFive.Linq;
using EastFive.Collections.Generic;
using BlackBarLabs.Linq;
using System.Collections.Generic;

namespace EastFive.Azure.Synchronization.Persistence
{
    [Serializable]
    [DataContract]
    internal class IntegrationAdapterLookupDocument : EastFive.Azure.Persistence.Documents.LookupDocument
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public string SystemName => this.PartitionKey;
    }
}
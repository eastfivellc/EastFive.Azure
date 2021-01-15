using BlackBarLabs.Persistence.Azure.Attributes;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Runtime.Serialization;

namespace EastFive.Security.SessionServer.Persistence.Azure.Documents
{
    [StorageResourceNoOp]
    public class CredentialMappingLookupDocument : TableEntity
    {
        #region Properties
        
        public Guid CredentialMappingId { get; set; }

        public string Method { get; set; }

        public string Subject { get; set; }

        #endregion

    }
}

using Microsoft.Azure.Cosmos.Table;
using System;

namespace EastFive.Security.SessionServer.Persistence.Azure.Documents
{
    public class CredentialMappingLookupDocument : TableEntity
    {
        #region Properties
        
        public Guid CredentialMappingId { get; set; }

        public string Method { get; set; }

        public string Subject { get; set; }

        #endregion

    }
}

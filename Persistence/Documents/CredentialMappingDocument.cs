using Microsoft.Azure.Cosmos.Table;
using System;
using System.Runtime.Serialization;

namespace EastFive.Security.SessionServer.Persistence.Documents
{
    public class CredentialMappingDocument : TableEntity
    {
        #region Properties
        
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id { get { return Guid.Parse(this.RowKey); } }

        public Guid ActorId { get; set; }

        public string Method { get; set; }

        public string Subject { get; set; }

        #endregion

    }
}

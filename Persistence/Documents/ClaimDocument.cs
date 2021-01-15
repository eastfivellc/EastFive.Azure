using Microsoft.Azure.Cosmos.Table;
using System;

namespace EastFive.Security.SessionServer.Persistence.Documents
{
    internal class ClaimDocument : TableEntity
    {
        #region Properties
        
        public Guid ClaimId { get; set; }
        public string Issuer { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }

        #endregion
    }
}

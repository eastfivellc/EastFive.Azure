
using Microsoft.Azure.Cosmos.Table;
using System;

namespace EastFive.Security.SessionServer.Persistence.Documents
{
    [Serializable]
    internal class SessionDocument : TableEntity
    {
        public Guid AuthorizationId { get; set; }
        public string RefreshToken { get; set; }
        public Guid SessionId { get; set; }
    }
}

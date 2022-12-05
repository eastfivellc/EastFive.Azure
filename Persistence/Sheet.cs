using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Api.Azure.Persistence
{
    [Serializable]
    [DataContract]
    internal class Sheet : TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id { get { return Guid.Parse(this.RowKey); } }
        
        public Guid IntegrationId { get; set; }
        
    }
}
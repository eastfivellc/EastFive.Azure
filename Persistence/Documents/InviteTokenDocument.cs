﻿using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Runtime.Serialization;

namespace EastFive.Security.SessionServer.Persistence.Azure.Documents
{
    internal class InviteTokenDocument : Microsoft.WindowsAzure.Storage.Table.TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id
        {
            get { return Guid.Parse(this.RowKey); }
        }

        public Guid CredentialMappingId { get; set; }
    }
}

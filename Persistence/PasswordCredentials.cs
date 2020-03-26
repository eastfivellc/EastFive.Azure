using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BlackBarLabs.Persistence.Azure;
using BlackBarLabs.Persistence.Azure.StorageTables;
using BlackBarLabs.Extensions;
using BlackBarLabs.Persistence;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;

namespace EastFive.Security.SessionServer.Persistence
{
    public struct CredentialMapping
    {
        public Guid id;
        public Guid actorId;
        public Guid loginId;
        public DateTime? lastSent;
        public string method;
        public string subject;
    }

}
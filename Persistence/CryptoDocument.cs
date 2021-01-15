using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

namespace BlackBarLabs.Identity.AzureStorageTables
{
    public class CryptoDocument : TableEntity
    {
        public override void ReadEntity(IDictionary<string, EntityProperty> properties, OperationContext operationContext)
        {
            base.ReadEntity(properties, operationContext);
            this.DecryptFields();
        }

        public override IDictionary<string, EntityProperty> WriteEntity(OperationContext operationContext)
        {
            this.EncryptFields();
            return base.WriteEntity(operationContext);
        }
    }
}

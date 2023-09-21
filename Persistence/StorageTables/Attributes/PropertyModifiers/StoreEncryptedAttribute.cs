using EastFive.Persistence;
using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace EastFive.Azure.Persistence.StorageTables
{
    public class StoreEncryptedAttribute : StorageAttribute
    {
        public string KeylookupValue { get; set; }
        public string KeylookupProperty { get; set; }

        private static IDictionary<IComparable, byte[]> encryptionTables;

        public static async Task LoadEncryptionTableAsync()
        {
            encryptionTables = new Dictionary<IComparable, byte[]>();
        }

        protected override TResult BindEntityProperties<TResult>(string propertyName, Type type, 
            IDictionary<string, EntityProperty> allValues, 
            Func<object, TResult> onBound,
            Func<TResult> onFailedToBind)
        {
            throw new NotImplementedException();
        }

        public override KeyValuePair<string, EntityProperty>[] CastValue(Type typeOfValue, object value, string propertyName)
        {
            throw new NotImplementedException();
        }
    }
}

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Persistence.Azure.StorageTables
{
    interface IScopeKeys
    {
        string MutateKey(string currentKey, MemberInfo key, object value, out bool ignore);

        double Order { get; }

        string Scope { get; }
    }
}

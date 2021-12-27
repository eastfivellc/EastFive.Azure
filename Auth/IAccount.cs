using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using EastFive;
using EastFive.Extensions;
using EastFive.Reflection;
using EastFive.Linq;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Collections.Generic;
using EastFive.Persistence;

using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Azure.Auth
{
    public interface IAccount : IReferenceable
    {
        [AccountLinks]
        public AccountLinks AccountLinks { get; set; }
    }
}


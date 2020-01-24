using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using EastFive.Linq.Async;

namespace EastFive.Persistence.Azure.StorageTables
{
    public interface IProvideQueryValues
    {
        IEnumerable<Reflection.Assignment> GetStorageValues(
            MethodInfo methodInfo, Expression[] methodArguments);
    }
}

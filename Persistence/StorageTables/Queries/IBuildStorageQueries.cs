using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace EastFive.Persistence.Azure.StorageTables
{
    public interface IBuildStorageQueries
    {
        (MemberInfo, object)[] BindStorageQueryValue(
            MethodInfo method,
            Expression[] arguments);
    }
}

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace EastFive.Azure.Search
{
    interface IProvideSearchQuery
    {
        string GetSearchParameter(MethodInfo methodInfo, Expression[] expressions);
    }
}

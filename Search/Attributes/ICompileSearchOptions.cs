using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Azure.Search.Documents;

namespace EastFive.Azure.Search
{
    public interface ICompileSearchOptions
    {
        SearchOptions GetSearchFilters(SearchOptions searchOptions, MethodInfo methodInfo, Expression[] expressions);
    }
}

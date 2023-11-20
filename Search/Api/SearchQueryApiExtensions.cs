using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

using EastFive.Extensions;
using EastFive.Linq.Async;
using EastFive.Linq;
using EastFive.Reflection;
using EastFive.Collections;
using EastFive.Collections.Generic;
using EastFive.Web.Configuration;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence;
using EastFive.Serialization.Text;
using EastFive.Api;
using EastFive.Azure.Search.Api;

namespace EastFive.Azure.Search
{
    public static class SearchQueryApiExtensions
    {
        public static IHttpResponse SearchQueryResponse<T>(this IEnumerableAsync<(double?, T)> query,
            SearchResultsResponse<T> onSearchResults,
            Dictionary<string, Func<FacetResult[]>> facets = default, Func<long> getTotals = default)
        {
            var results = query
                .Select(tpl => tpl.Item2);
            return onSearchResults(results,
                facets,
                getTotals);
        }

    }
}

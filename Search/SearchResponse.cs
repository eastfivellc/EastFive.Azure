using System;
using System.Collections.Generic;
using Azure;
using Azure.Search.Documents.Models;

namespace EastFive.Azure.Search
{
    public struct SearchResponse
    {
        public double? Coverage { get; private set; }
        public long? TotalCount { get; private set; }
        public IDictionary<string, IList<FacetResult>> Facets { get; private set; }

        internal static SearchResponse FromResult<TIntermediary>(Response<SearchResults<TIntermediary>> result)
        {
            return new SearchResponse
            {
                Coverage = result.Value.Coverage,
                TotalCount = result.Value.TotalCount,
                Facets = result.Value.Facets,
            };
        }
    }

    public delegate void SearchResponseDelegate(SearchResponse searchResponse);
}

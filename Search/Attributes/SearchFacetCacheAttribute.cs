using Azure.Search.Documents.Indexes.Models;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using EastFive.Reflection;
using Azure.Search.Documents;
using System.Linq;
using EastFive.Serialization;

namespace EastFive.Azure.Search
{
    public interface IProvideSearchFacetCache
    {
        public string GetSearchHash(SearchOptions searchOptions);
    }

    public class SearchFacetCacheAttribute : Attribute, IProvideSearchFacetCache
    {
        public string[] HashedProperties { get; set; }

        public string GetSearchHash(SearchOptions searchOptions)
        {
            return searchOptions.Filter
                .Split(" and ")
                .Where(
                    relation =>
                    {
                        var doesMatchProperty = HashedProperties
                            .Select(
                                hashedProperty =>
                                {
                                    var isContained = relation.Contains(hashedProperty);
                                    return isContained;
                                })
                            .Any();
                        return doesMatchProperty;
                    })
                .Join("")
                .HashXX32()
                .ToString();
        }
    }
}

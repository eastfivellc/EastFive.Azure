using Azure.Search.Documents.Indexes.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EastFive.Azure.Search
{
    public class SearchKeyAttribute : SearchFieldAttribute
    {
        protected override SearchField PopulateFieldKey(SearchField searchField)
        {
            var searchFieldKey = base.PopulateFieldKey(searchField);
            searchFieldKey.IsKey = true;
            return searchFieldKey;
        }
    }
}

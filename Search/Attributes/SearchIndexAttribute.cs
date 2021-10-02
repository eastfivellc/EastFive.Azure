using Azure.Search.Documents.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EastFive.Azure.Search
{
    public class SearchIndexAttribute : Attribute, IProvideSearchIndex
    {
        public string Index { get; set; }

    }
}

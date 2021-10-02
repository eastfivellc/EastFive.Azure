using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EastFive.Azure.Search
{
    public interface IProvideSearchIndex
    {
        public string Index { get; }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Reflection;

using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using DocumentFormat.OpenXml.Presentation;

namespace EastFive.Azure.Search
{
    public class SearchQuery<TResource> : Query<TResource,
        (
            SearchClient searchIndexClient,
            IList<SearchResponseDelegate> callbacks,
            IList<string> fullResponseHashes
        )>
    {
        public SearchClient SearchIndexClient => this.carry.searchIndexClient;
        public IList<SearchResponseDelegate> Callbacks => this.carry.callbacks;
        public IList<string> FullResponseHashes => this.carry.fullResponseHashes;

        public static SearchQuery<TResource> FromIndex(SearchClient searchIndexClient)
        {
            var callbacks = new List<SearchResponseDelegate>();
            var fullResponseHashes = new List<string>();
            return new SearchQuery<TResource>((searchIndexClient, callbacks, fullResponseHashes));
        }

        public SearchQuery((SearchClient, IList<SearchResponseDelegate>, IList<string> fullResponseHashes) carry) : base(carry)
        {
        }

        public SearchQuery((SearchClient, IList<SearchResponseDelegate>, IList<string> fullResponseHashes) carry, Expression condition)
            : base(carry, condition)
        {
        }

        public SearchQuery<TResource> SearchQueryFromExpression(Expression condition)
        {
            return new SearchQuery<TResource>(this.carry, condition);
        }

        protected override Query<TResource,
            (
                SearchClient searchIndexClient,
                IList<SearchResponseDelegate> callbacks,
                IList<string> fullResponseHashes
            )> From()
        {
            return new SearchQuery<TResource>(this.carry);
        }

        protected override Query<TResource,
            (
                SearchClient searchIndexClient,
                IList<SearchResponseDelegate> callbacks,
                IList<string> fullResponseHashes
            )> FromExpression(Expression condition)
        {
            return new SearchQuery<TResource>(this.carry, condition);
        }
    }
}

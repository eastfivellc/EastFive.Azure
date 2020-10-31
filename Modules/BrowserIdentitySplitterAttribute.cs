using EastFive.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using EastFive.Serialization;
using EastFive.Extensions;
using EastFive.Azure.Auth;
using System.Net.Http;
using System.Threading;
using EastFive.Linq;
using EastFive.Web.Configuration;
using EastFive.Azure;
using System.Net.Http.Headers;
using EastFive.Persistence.Azure.StorageTables;
using Newtonsoft.Json;
using EastFive.Api;
using EastFive;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence;

namespace EastFive.Azure.Auth
{
    public class BrowserIdentitySplitterAttribute : System.Attribute, IResolveRedirection
    {
        IDictionary<string, Uri> redirections;
        IDictionary<string, long> limits;

        public BrowserIdentitySplitterAttribute()
        {
            (redirections, limits) = AppSettings.Redirections.ConfigurationJson(
                   (RedirectionConfiguration[] kvps) =>
                   {
                       var redirs = kvps
                           .Select(
                               kvp =>
                               {
                                   var success = Uri.TryCreate(kvp.Value, UriKind.Absolute, out Uri uri);
                                   return (success, kvp.Key.PairWithValue(uri));
                               })
                           .Where(tpl => tpl.success)
                           .Select(tpl => tpl.Item2)
                           .ToDictionary();

                       var lmts = kvps
                           .Select(
                               kvp =>
                               {
                                   var success = true;
                                   return (success, kvp.Limit.PairWithKey(kvp.Key));
                               })
                           .Where(tpl => tpl.success)
                           .Select(tpl => tpl.Item2)
                           .ToDictionary();
                       return (redirs, lmts);
                   },
                   (why) => (new Dictionary<string, Uri>(), new Dictionary<string, long>()),
                   () =>    (new Dictionary<string, Uri>(), new Dictionary<string, long>()));
        }

        public float Order { get; set; }

        public async Task<(Func<HttpResponseMessage, HttpResponseMessage>, Uri)> ResolveAbsoluteUrlAsync(Uri defaultUri, 
            HttpRequestMessage request, Guid? accountIdMaybe, 
            IDictionary<string, string> authParams)
        {
            Func<HttpResponseMessage, HttpResponseMessage> emptyModifier = x => x;
            if (request.Headers.IsDefaultNullOrEmpty())
                return (emptyModifier, defaultUri);
            var userAgents = request.Headers.UserAgent;
            if (!userAgents.AnyNullSafe())
                return (emptyModifier, defaultUri);
            var userAgent = userAgents.First();
            var name = userAgent.Product.Name;
            var version = userAgent.Product.Version;
            var unique = $"{name}++++{version}";
            var browserId = unique.MD5HashGuid();
            return await browserId
                .AsRef<BrowserIdentity>()
                .StorageCreateOrUpdateAsync(
                    async (created, browserIdentity, saveAsync) =>
                    {
                        var matchedRedirections = redirections
                            .Select(
                                redirAvailable =>
                                {
                                    var limit = limits[redirAvailable.Key];
                                    return browserIdentity.redirections
                                        .NullToEmpty()
                                        .Where(redir => redirAvailable.Key == redir.redirection)
                                        .First(
                                            (redir, next) => redir,
                                            () => new BrowserIdentity.Redirection()
                                            {
                                                limit = limit,
                                                count = 0,
                                                redirection = redirAvailable.Key,
                                            });
                                })
                            .ToArray();
                        var (valid, availableRedirection) = matchedRedirections
                            .Where(redir => redir.count < redir.limit)
                            .First(
                                (redir, next) =>
                                {
                                    return (true, redir);
                                },
                                () => (false, default(BrowserIdentity.Redirection)));
                        if(!valid)
                            return (emptyModifier, defaultUri);

                        if (accountIdMaybe.HasValue)
                        {

                        }
                        else
                        {
                            availableRedirection.count++;
                        }
                        browserIdentity.redirections = matchedRedirections
                            .Where(matchedRedirection =>
                                matchedRedirection.redirection != availableRedirection.redirection)
                            .Append(availableRedirection)
                            .ToArray();
                        await saveAsync(browserIdentity);

                        var baseUri = redirections
                            .Where(redir => redir.Key == availableRedirection.redirection)
                            .First().Value;
                        var overriddenUrl = defaultUri.ReplaceBase(baseUri);
                        return (emptyModifier, overriddenUrl);
                    });
        }

        [StorageTable]
        public struct BrowserIdentity : IReferenceable
        {
            [JsonIgnore]
            public Guid id => browserIdentityRef.id;

            private const string IdPropertyName = "id";
            [ApiProperty(PropertyName = IdPropertyName)]
            [JsonProperty(PropertyName = IdPropertyName)]
            [RowKey]
            [RowKeyPrefix(Characters = 1)]
            public IRef<BrowserIdentity> browserIdentityRef;

            [ETag]
            [JsonIgnore]
            public string eTag;

            private const string NamePropertyName = "name";
            [JsonProperty(PropertyName = NamePropertyName)]
            [Storage]
            public string name;

            private const string VersionPropertyName = "version";
            [JsonProperty(PropertyName = VersionPropertyName)]
            [Storage]
            public string version;

            private const string RedirectionsPropertyName = "redirections";
            [JsonProperty(PropertyName = RedirectionsPropertyName)]
            [Storage]
            public Redirection[] redirections;

            public struct Redirection
            {
                private const string RedirectionPropertyName = "redirection";
                [JsonProperty(PropertyName = RedirectionPropertyName)]
                [Storage]
                public string redirection;

                private const string LimitPropertyName = "limit";
                [JsonProperty(PropertyName = LimitPropertyName)]
                [Storage]
                public long limit;

                private const string CountPropertyName = "count";
                [JsonProperty(PropertyName = CountPropertyName)]
                [Storage]
                public long count;
            }
        }
    }
}

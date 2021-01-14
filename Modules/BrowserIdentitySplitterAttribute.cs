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

        public async Task<(Func<IHttpResponse, IHttpResponse>, Uri)> ResolveAbsoluteUrlAsync(Uri defaultUri,
            IHttpRequest request, Guid? accountIdMaybe,
            IDictionary<string, string> authParams)
        {
            Func<IHttpResponse, IHttpResponse> emptyModifier = x => x;
            if (request.Headers.IsDefaultNullOrEmpty())
                return (emptyModifier, defaultUri);
            if(!request.TryGetUserAgent(out ProductInfoHeaderValue userAgent))
                return (emptyModifier, defaultUri);

            var name = userAgent.Product.Name;
            var version = userAgent.Product.Version;
            return await redirections.First(
                (redirAvailable, next) =>
                {
                    var unique = $"{name}++++{version}++++{redirAvailable.Key}";
                    var browserId = unique.MD5HashGuid();
                    return browserId
                        .AsRef<BrowserIdentity>()
                        .StorageCreateOrUpdateAsync(
                            async (created, browserIdentity, saveAsync) =>
                            {
                                var limit = limits[redirAvailable.Key];
                                if(created)
                                {
                                    browserIdentity.limit = limit;
                                    browserIdentity.count = 0;
                                    browserIdentity.name = name;
                                    browserIdentity.version = version;
                                    browserIdentity.redirection = redirAvailable.Key;
                                }

                                if (browserIdentity.count >= browserIdentity.limit)
                                    if (browserIdentity.limit >= 0)
                                        return await next();

                                if (accountIdMaybe.HasValue)
                                {
                                    var accountCount = browserIdentity.accounts
                                        .NullToEmpty()
                                        .Count();
                                    browserIdentity.accounts = browserIdentity.accounts
                                        .NullToEmpty()
                                        .Append(accountIdMaybe.Value)
                                        .Distinct()
                                        .ToArray();
                                    if(accountCount < browserIdentity.accounts.Length)
                                        browserIdentity.count++;
                                }
                                else
                                {
                                    browserIdentity.count++;
                                }

                                await saveAsync(browserIdentity);

                                var baseUri = redirAvailable.Value;
                                var overriddenUrl = defaultUri.ReplaceBase(baseUri);
                                return (emptyModifier, overriddenUrl);
                            });
                },
                () =>
                {
                    return (emptyModifier, defaultUri).AsTask();
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

            private const string AccountsPropertyName = "accounts";
            [JsonProperty(PropertyName = AccountsPropertyName)]
            [Storage]
            public Guid[] accounts;
        }
    }
}

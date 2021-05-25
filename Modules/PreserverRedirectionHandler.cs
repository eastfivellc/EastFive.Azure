using EastFive.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using EastFive.Serialization;
using EastFive.Extensions;
using BlackBarLabs.Web;
using System.Net.Http;
using System.Threading;
using EastFive.Linq;
using EastFive.Web.Configuration;
using EastFive.Azure;
using System.Net.Http.Headers;
using EastFive.Api;
using System.Runtime.Serialization;

namespace EastFive.Azure.Auth
{
    [DataContract]
    public class RedirectionConfiguration
    {
        [DataMember]
        public string Key;

        [DataMember]
        public string Value;

        [DataMember]
        public long Limit;
    }

    public class PreserverRedirectionAttribute : System.Attribute, IResolveRedirection
    {
        IDictionary<string, Uri> redirections;

        public PreserverRedirectionAttribute()
        {
            redirections = AppSettings.Redirections.ConfigurationJson(
                (RedirectionConfiguration[] kvps) =>
                {
                    return kvps
                        .NullToEmpty()
                        .Select(
                            kvp =>
                            {
                                var success = Uri.TryCreate(kvp.Value, UriKind.Absolute, out Uri uri);
                                return (success, kvp.Key.PairWithValue(uri));
                            })
                        .Where(tpl => tpl.success)
                        .Select(tpl => tpl.Item2)
                        .ToDictionary();
                },
                (why) => new Dictionary<string, Uri>(),
                () => new Dictionary<string, Uri>());
        }

        public float Order { get; set; }

        public Task<(Func<IHttpResponse, IHttpResponse>, Uri)> ResolveAbsoluteUrlAsync(Uri defaultUri, 
            IHttpRequest request, Guid? accountIdMaybe, 
            IDictionary<string, string> authParams)
        {
            Func<IHttpResponse, IHttpResponse> modifier =
                (response) =>
                {
                    // No redirect, no modification
                    if (!response.TryGetLocation(out Uri finalUrl))
                        return response;
                    
                    if (!finalUrl.IsAbsoluteUri)
                        return response;
                    var finalUrlStr = finalUrl.AbsoluteUri;
                    
                    // No mutation, no modification
                    if (finalUrlStr.StartsWith(defaultUri.AbsoluteUri))
                        return response;
                    
                    var matchingRedirections = redirections
                        .Where(redir => finalUrlStr.StartsWith(redir.Value.AbsoluteUri));
                    if (!matchingRedirections.TrySingle(out KeyValuePair<string, Uri> matchingRedirection))
                        return response;
                    
                    var lookupToken = matchingRedirection.Key;
                    response.AddCookie("e5-redirect-base", lookupToken, TimeSpan.FromDays(365));
                    return response;
                };

            return request.ReadCookie("e5-redirect-base",
                redirBase =>
                {
                    if (!redirections.ContainsKey(redirBase))
                        return (modifier, defaultUri);

                    var redirect = redirections[redirBase];
                    var fullUri = defaultUri.ReplaceBase(redirect);
                    Func<IHttpResponse, IHttpResponse> emptyModifier =
                        (response) => response;
                    return (emptyModifier, fullUri);
                },
                () => (modifier, defaultUri)).AsTask();
        }
    }
}

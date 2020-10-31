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

namespace EastFive.Azure.Auth
{
    public class RedirectionConfiguration
    {
        public string Key;
        public string Value;
        public long Limit;
    }

    public class PreserverRedirectionAttribute : System.Attribute, IResolveRedirection
    {
        IDictionary<string, Uri> redirections;

        public PreserverRedirectionAttribute()
        {
            redirections = AppSettings.Redirections.ConfigurationJson(
                (KeyValuePair<string, string>[] kvps) =>
                {
                    return kvps
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

        public Task<(Func<HttpResponseMessage, HttpResponseMessage>, Uri)> ResolveAbsoluteUrlAsync(Uri defaultUri, 
            HttpRequestMessage request, Guid? accountIdMaybe, 
            IDictionary<string, string> authParams)
        {
            Func<HttpResponseMessage, HttpResponseMessage> emptyModifier = x => x;
            if (request.Headers.IsDefaultNullOrEmpty())
                return (emptyModifier, defaultUri).AsTask();

            return request.Headers
                .GetCookies()
                .NullToEmpty()
                .SelectMany(cookieBucket => cookieBucket.Cookies
                    .Select(cookie => (cookie, cookieBucket.Expires)))
                .Where(cookie => cookie.cookie.Name == "e5-redirect-base")
                .Where(cookie => redirections.ContainsKey(cookie.cookie.Value))
                .OrderByDescending(cookie => cookie.Expires)
                .First(
                    (cookie, next) =>
                    {
                        var redirect = redirections[cookie.cookie.Value];
                        var fullUri = defaultUri.ReplaceBase(redirect);
                        Func<HttpResponseMessage, HttpResponseMessage> modifier =
                            (response) => response;
                        return (modifier, fullUri).AsTask();
                    },
                    () =>
                    {
                        Func<HttpResponseMessage, HttpResponseMessage> modifier =
                            (response) =>
                            {
                                // No redirect, no modification
                                if (response.Headers.Location.IsDefaultOrNull())
                                    return response;

                                var finalUrl = response.Headers.Location;
                                if (!finalUrl.IsAbsoluteUri)
                                    return response;
                                var finalUrlStr = finalUrl.AbsoluteUri;

                                // No mutation, no modification
                                if (finalUrlStr.StartsWith(defaultUri.AbsoluteUri))
                                    return response;

                                var matchingRedirections = redirections
                                    .Where(redir => finalUrlStr.StartsWith(redir.Value.AbsoluteUri));
                                if(!matchingRedirections.TrySingle(out KeyValuePair<string, Uri> matchingRedirection))
                                    return response;

                                var lookupToken = matchingRedirection.Key;
                                response.Headers.AddCookies(
                                    new CookieHeaderValue("e5-redirect-base", lookupToken).AsEnumerable());
                                return response;
                            };

                        return (modifier, defaultUri).AsTask();

                    });
        }
    }
}

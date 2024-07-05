using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using EastFive.Api;
using EastFive.Azure.Auth;
using EastFive.Collections.Generic;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Reflection;

using Newtonsoft.Json;

namespace EastFive.Azure.Persistence
{
	public abstract class SecureLinkAttribute : Attribute, ICastJsonProperty
    {
        public string Method { get; set; }

        public string[] RoleClaims { get; set; }

        public SecureLinkAttribute(string method)
        {
            this.Method = method;
        }

        public bool CanConvert(MemberInfo member, ParameterInfo paramInfo,
            IHttpRequest httpRequest, IApplication application,
            IProvideApiValue apiValueProvider, object objectValue)
        {
            return true;
        }

        public async Task WriteAsync(JsonWriter writer, JsonSerializer serializer, MemberInfo documentsMember, ParameterInfo paramInfo, IProvideApiValue apiValueProvider,
            object objectValue, object memberValue, IHttpRequest httpRequest, IApplication application)
        {
            if (this.Method.IsNullOrWhiteSpace())
            {
                await writer.WriteNullAsync();
                await writer.WriteCommentAsync($"{documentsMember.DeclaringType.FullName}..{documentsMember.Name} does not specify a method.");
                return;
            }

            bool wasErrorFree = await documentsMember.DeclaringType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(
                    method => this.Method.Equals(method.Name, StringComparison.OrdinalIgnoreCase))
                .First(
                    async (method, next) =>
                    {
                        var httpActionAttrMaybe = method.GetCustomAttribute<HttpActionAttribute>();
                        if (!httpActionAttrMaybe.TryIsNotDefaultOrNull(out HttpActionAttribute httpActionAttr))
                        {
                            await writer.WriteNullAsync();
                            await writer.WriteCommentAsync($"{documentsMember.DeclaringType.FullName}..{documentsMember.Name} links to method `{method.Name}` which does not have an {nameof(HttpActionAttribute)} attribute.");
                            return false;
                        }

                        var url = GetUrl(documentsMember, paramInfo, apiValueProvider, objectValue:objectValue, memberValue:memberValue,
                            actionAttribute: httpActionAttr, httpRequest: httpRequest, application:application);

                        return await httpRequest.GetSessionId(
                            (sessionId, claims) =>
                            {
                                return claims.GetActorId(
                                    (accountId) =>
                                    {
                                        return url.SignWithAccessTokenAccount(sessionId, accountId, DateTime.UtcNow + TimeSpan.FromHours(1),
                                            async signedUrl =>
                                            {
                                                var decodedUrl = HttpUtility.UrlDecode(signedUrl.OriginalString);
                                                await writer.WriteValueAsync(decodedUrl);
                                                return true;
                                            },
                                            onSystemNotConfigured: async () =>
                                            {
                                                var decodedUrl = HttpUtility.UrlDecode(url.OriginalString);
                                                await writer.WriteValueAsync(decodedUrl);
                                                return true;
                                            },
                                                RoleClaims
                                                    .NullToEmpty()
                                                    .Select(r => ClaimTypes.Role.PairWithValue(r))
                                                    .ToDictionary());
                                    },
                                    async () =>
                                    {
                                        var decodedUrl = HttpUtility.UrlDecode(url.OriginalString);
                                        await writer.WriteValueAsync(decodedUrl);
                                        await writer.WriteCommentAsync("No account specified in request.");
                                        return false;

                                    });
                            },
                            onNoSessionClaims: async () =>
                            {
                                var decodedUrl = HttpUtility.UrlDecode(url.OriginalString);
                                await writer.WriteValueAsync(decodedUrl);
                                await writer.WriteCommentAsync("No session on request.");
                                return false;
                            });
                    },
                    async () =>
                    {
                        await writer.WriteNullAsync();
                        await writer.WriteCommentAsync($"{documentsMember.DeclaringType.FullName}..{documentsMember.Name} references none existant method `{this.Method}`.");
                        return false;
                    });
        }

        protected abstract Uri GetUrl(MemberInfo documentsMember, ParameterInfo paramInfo, IProvideApiValue apiValueProvider,
            object objectValue, object memberValue,
            HttpActionAttribute actionAttribute, IHttpRequest httpRequest, IApplication application);
    }
}


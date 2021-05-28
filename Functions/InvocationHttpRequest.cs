using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Razor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EastFive.Api
{
    public class InvocationHttpRequest : IHttpRequest
    {
        private byte[] content;
        public byte[] Content
        {
            get => content;
            set
            {
                this.HasBody = !value.IsDefaultNullOrEmpty();
                content = value;
            }
        }

        public InvocationHttpRequest(Uri absoluteUri, CancellationToken cancellationToken = default)
        {
            this.RequestUri = absoluteUri;
            this.CancellationToken = cancellationToken;
            this.Headers = new Dictionary<string, string[]>();
        }

        public Uri RequestUri { get; set; }

        public CancellationToken CancellationToken { get; private set; }

        public HttpMethod Method { get; set; }

        private bool hasWrittenBody;
        private Func<Stream, Task> writeBody;
        public Func<Stream, Task> WriteBodyAsync
        {
            get => writeBody;
            set
            {
                hasWrittenBody = false;
                HasBody = true;
                writeBody = value;
            }
        }

        public bool HasBody { get; private set; }

        public async Task<string> ReadContentAsStringAsync()
        {
            var contentBytes = await ReadContentAsync();
            return contentBytes.GetString(Encoding.UTF8);
        }

        public async Task<byte[]> ReadContentAsync()
        {
            await WriteBodyAsync();
            return Content;
            
            async Task WriteBodyAsync()
            {
                if (this.WriteBodyAsync.IsDefaultOrNull())
                    return;

                if (hasWrittenBody)
                    return;

                using (var stream = new MemoryStream())
                {
                    await this.WriteBodyAsync(stream);
                    Content = stream.ToArray();
                }
            }
        }

        public bool HasFormContentType => false; // TODO:

        public IFormCollection Form => throw new NotImplementedException();

        public IDictionary<string, string[]> Headers { get; private set; }

        public IRazorViewEngine RazorViewEngine => throw new NotImplementedException();

        IDictionary<string, object> IHttpRequest.Properties { get; }

        public string GetHeader(string headerKey)
        {
            if (Headers.ContainsKey(headerKey))
            {
                var values = Headers[headerKey];
                return values.AnyNullSafe() ? values[0] : string.Empty;
            }
            return String.Empty;
        }

        public IRequestHeaders RequestHeaders => throw new NotImplementedException();

        public IEnumerable<string> GetHeaders(string headerKey)
        {
            if (Headers.ContainsKey(headerKey))
                return Headers[headerKey];
            return Enumerable.Empty<string>();
        }

        public void UpdateHeader(string headerKey,
            Func<string[], string[]> callback)
        {
            var currentHeaders = GetHeaders(headerKey).ToArray();
            var updatedHeaders = callback(currentHeaders);
            if (Headers.ContainsKey(headerKey))
            {
                Headers[headerKey] = updatedHeaders;
                return;
            }
            Headers.Add(headerKey, updatedHeaders);
        }

        public TResult ReadCookie<TResult>(string cookieKey, Func<string, TResult> onCookie, Func<TResult> onNotAvailable)
        {
            throw new NotImplementedException();
            //request.Headers
            //    .GetCookies()
            //    .NullToEmpty()
            //       .SelectMany(cookieBucket => cookieBucket.Cookies
            //           .Select(cookie => (cookie, cookieBucket.Expires)))
            //       .Where(cookie => cookie.cookie.Name == cookieKey)
            //       .First(
            //    )
        }
    }
}

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Mime;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Azure.Persistence.StorageTables;
using System.Net.Http.Headers;

namespace EastFive.Azure.Persistence.Blobs
{
    internal class BlobRefRawData : IBlobRef
    {
        public string Id { get; private set; }

        public string ContainerName { get; set; }

        private byte[] content;

        private string contentType;

        public BlobRefRawData(string containerName, byte [] content, string contentType = default)
        {
            Id = Guid.NewGuid().ToString("N");
            this.content = content;
            this.ContainerName = containerName;
            this.contentType = contentType.HasBlackSpace() ? contentType : IBlobRef.DefaultMediaType;
        }

        public Task<TResult> LoadBytesAsync<TResult>(
            Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null)
        {
            if (!MediaTypeHeaderValue.TryParse(contentType,
                            out MediaTypeHeaderValue mediaType))
                mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);

            ContentDispositionHeaderValue.TryParse(string.Empty,
                out ContentDispositionHeaderValue contentDisposition);
            if (contentDisposition.IsDefaultOrNull())
            {
                contentDisposition = new ContentDispositionHeaderValue("inline")
                {
                    FileName = this.Id,
                };
            }
            return onFound(this.Id, content, mediaType, contentDisposition).AsTask();
        }

        public Task<TResult> LoadStreamAsync<TResult>(
            Func<string, System.IO.Stream, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            if (!MediaTypeHeaderValue.TryParse(contentType,
                            out MediaTypeHeaderValue mediaType))
                mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);

            ContentDispositionHeaderValue.TryParse(string.Empty,
                out ContentDispositionHeaderValue contentDisposition);
            if (contentDisposition.IsDefaultOrNull())
            {
                contentDisposition = new ContentDispositionHeaderValue("inline")
                {
                    FileName = this.Id,
                };
            }
            return onFound(this.Id, new System.IO.MemoryStream(content), mediaType, contentDisposition).AsTask();
        }

        public async Task<TResult> LoadStreamToAsync<TResult>(System.IO.Stream stream,
            Func<string, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            if (!MediaTypeHeaderValue.TryParse(contentType,
                            out MediaTypeHeaderValue mediaType))
                mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);

            ContentDispositionHeaderValue.TryParse(string.Empty,
                out ContentDispositionHeaderValue contentDisposition);
            if (contentDisposition.IsDefaultOrNull())
            {
                contentDisposition = new ContentDispositionHeaderValue("inline")
                {
                    FileName = this.Id,
                };
            }
            await stream.WriteAsync(content, 0, content.Length);
            return onFound(this.Id, mediaType, contentDisposition);
        }

        public void Write(JsonWriter writer, JsonSerializer serializer,
            IHttpRequest httpRequest, IAzureApplication application)
        {
            writer.WriteValue(content);
        }
    }
}


using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Mime;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Serialization;
using System.Net.Http.Headers;
using System.Linq;
using EastFive.Linq;

namespace EastFive.Azure.Persistence.Blobs
{
    internal class BlobRefUrl : IApiBoundBlobRef
    {
        public string Id { get; set; }

        private string containerName = default;
         
        public string ContainerName
        {
            get
            {
                if (containerName.HasBlackSpace())
                    return containerName;
                throw new Exception($"{nameof(BlobRefProperty)} must be used as the Http Method Parameter decorator for IBlobRef");
            }
            set
            {
                if (value.IsNullOrWhiteSpace())
                    throw new Exception("Container names but not be empty.");
                this.containerName = value;
            }
        }

        private Uri content;

        public BlobRefUrl(Uri content)
        {
            Id = content.AbsoluteUri.MD5HashGuid().AsBlobName();
            this.content = content;
        }

        public async Task<TResult> LoadAsync<TResult>(
            Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound, Func<ExtendedErrorInformationCodes,
                string, TResult> onFailure = null)
        {
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(this.content))
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                var disposition = response.GetContentDispositionNullSafe();
                if(disposition.IsDefaultOrNull())
                {
                    disposition = new ContentDispositionHeaderValue("inline")
                    {
                        FileName = content
                            .ParsePath()
                            .LastOrEmpty(
                                (path) => path,
                                () => this.Id),
                    };
                }
                var mediaType = response.GetContentMediaTypeHeaderNullSafe();
                if (mediaType.IsDefaultOrNull())
                    mediaType = new MediaTypeHeaderValue("application/octet-stream");
                return onFound(this.Id, bytes, mediaType, disposition);
            }
        }

        public void Write(JsonWriter writer, JsonSerializer serializer,
            IHttpRequest httpRequest, IAzureApplication application)
        {
            writer.WriteValue(content);
        }
    }
}


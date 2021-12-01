using System;
using System.Net.Http;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Azure.Persistence.StorageTables;

namespace EastFive.Azure.Persistence.Blobs
{
    internal class BlobRefUrl : IApiBoundBlobRef
    {
        public string Id { get; private set; }

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
            Id = Guid.NewGuid().ToString("N");
            this.content = content;
        }

        public async Task<TResult> LoadAsync<TResult>(
            Func<string, byte[], string, string, TResult> onFound,
            Func<TResult> onNotFound, Func<ExtendedErrorInformationCodes,
                string, TResult> onFailure = null)
        {
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(this.content))
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return onFound(this.Id, bytes,
                    response.GetContentMediaTypeNullSafe(),
                    response.GetFileNameNullSafe());
            }
        }

        public void Write(JsonWriter writer, JsonSerializer serializer,
            IHttpRequest httpRequest, IAzureApplication application)
        {
            writer.WriteValue(content);
        }
    }
}


using System;
using System.Net.Http;
using System.Threading.Tasks;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Persistence.Azure.StorageTables.Driver;
using System.Net.Http.Headers;

namespace EastFive.Azure.Persistence.Blobs
{
    internal class BlobRefStorage : IBlobRef
    {
        public string ContainerName { get; set; }

        public string Id { get; set; }

        public Task<TResult> LoadAsync<TResult>(
            Func<string, byte[], string, string, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            var blobName = this.Id;
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadBytesAsync(blobName: blobName, containerName: this.ContainerName,
                    (bytes, properties) =>
                    {
                        ContentDispositionHeaderValue.TryParse(
                            properties.ContentDisposition, out ContentDispositionHeaderValue fileNameHeaderValue);
                        var fileName = fileNameHeaderValue.IsDefaultOrNull() ?
                            string.Empty
                            :
                            fileNameHeaderValue.FileName;
                        return onFound(blobName, bytes,
                            properties.ContentType, fileName);
                    },
                    onNotFound: onNotFound,
                    onFailure: onFailure);
        }
    }
}


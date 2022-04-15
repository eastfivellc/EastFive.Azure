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
using System.Net.Mime;

namespace EastFive.Azure.Persistence.Blobs
{
    internal class BlobRefStorage : IBlobRef
    {
        public string ContainerName { get; set; }

        public string Id { get; set; }

        public Task<TResult> LoadBytesAsync<TResult>(
            Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            var blobName = this.Id;
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadBytesAsync(blobName: blobName, containerName: this.ContainerName,
                    (bytes, properties) =>
                    {
                        ContentDispositionHeaderValue.TryParse(properties.ContentDisposition,
                            out ContentDispositionHeaderValue disposition);
                        if (disposition.IsDefaultOrNull())
                        {
                            disposition = new ContentDispositionHeaderValue("inline")
                            {
                                FileName = blobName,
                            };
                        }
                        if(!MediaTypeHeaderValue.TryParse(properties.ContentType,
                            out MediaTypeHeaderValue mediaType))
                            mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);

                        return onFound(blobName, bytes, mediaType, disposition);
                    },
                    onNotFound: onNotFound,
                    onFailure: onFailure);
        }

        public Task<TResult> LoadStreamAsync<TResult>(
            Func<string, System.IO.Stream, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            var blobName = this.Id;
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadStreamAsync(blobName: blobName, containerName: this.ContainerName,
                    (stream, properties) =>
                    {
                        ContentDispositionHeaderValue.TryParse(properties.ContentDisposition,
                            out ContentDispositionHeaderValue disposition);
                        if (disposition.IsDefaultOrNull())
                        {
                            disposition = new ContentDispositionHeaderValue("inline")
                            {
                                FileName = blobName,
                            };
                        }
                        if (!MediaTypeHeaderValue.TryParse(properties.ContentType,
                            out MediaTypeHeaderValue mediaType))
                            mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);

                        return onFound(blobName, stream, mediaType, disposition);
                    },
                    onNotFound: onNotFound,
                    onFailure: onFailure);
        }

        public Task<TResult> LoadStreamToAsync<TResult>(System.IO.Stream stream,
            Func<string, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default)
        {
            var blobName = this.Id;
            return AzureTableDriverDynamic
                .FromSettings()
                .BlobLoadToAsync(blobName: blobName, containerName: this.ContainerName, stream,
                    (properties) =>
                    {
                        ContentDispositionHeaderValue.TryParse(properties.ContentDisposition,
                            out ContentDispositionHeaderValue disposition);
                        if (disposition.IsDefaultOrNull())
                        {
                            disposition = new ContentDispositionHeaderValue("inline")
                            {
                                FileName = blobName,
                            };
                        }
                        if (!MediaTypeHeaderValue.TryParse(properties.ContentType,
                            out MediaTypeHeaderValue mediaType))
                            mediaType = new MediaTypeHeaderValue(IBlobRef.DefaultMediaType);

                        return onFound(blobName, mediaType, disposition);
                    },
                    onNotFound: onNotFound,
                    onFailure: onFailure);
        }
    }
}


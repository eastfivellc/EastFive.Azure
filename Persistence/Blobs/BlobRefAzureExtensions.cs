using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos.Table;

using Newtonsoft.Json;

using Azure.Storage;
using Azure.Storage.Sas;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization;
using EastFive.Reflection;
using EastFive.Api;
using EastFive.Api.Bindings;

namespace EastFive.Azure.Persistence.Blobs
{
    public static class BlobRefAzureExtensions
    {
        public const string AzureFileUploadSASUrlHeader = "X-Azure-Blob-Content-Write";

        public static (IBlobRef, Uri) GenerateBlobFileSasLink(this IBlobRef blobRef,
            TimeSpan? lifespan = default(TimeSpan?))
        {
            var sasUrl = GenerateBlobFileSasLink(blobRef.ContainerName, blobRef.Id, lifespan: lifespan);
            var newBlobRef = new BlobRef(blobRef, sasUrl);
            return (newBlobRef, sasUrl);
        }

        public static Uri GenerateBlobFileSasLink(this MemberInfo member, string blobName,
            TimeSpan? lifespan = default(TimeSpan?))
        {
            var containerName = member.BlobContainerName();
            return GenerateBlobFileSasLink(containerName, blobName, lifespan: lifespan);
        }

        public static Uri GenerateBlobFileSasLink(this string containerName, string blobName,
            TimeSpan? lifespan = default(TimeSpan?))
        {
            var expiresOn = lifespan.HasValue ?
                DateTime.UtcNow + lifespan.Value
                :
                DateTime.UtcNow.AddMinutes(15);//default SAS token expire after 15 minutes.

            var blobSasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = containerName,
                BlobName = blobName,
                ExpiresOn = expiresOn,
            };
            blobSasBuilder.SetPermissions(BlobSasPermissions.Write);

            var azureDriver = AzureTableDriverDynamic.FromSettings();
            var blobClient = azureDriver.BlobClient;
            var sharedKeyCredential = azureDriver.StorageSharedKeyCredential;
            var sasToken = blobSasBuilder.ToSasQueryParameters(sharedKeyCredential).ToString();
            var sasUrl = blobClient.Uri
                .AppendToPath(containerName)
                .AppendToPath(blobName)
                .SetQuery(sasToken);
            return sasUrl;
        }

        private class BlobRef : IBlobRef, IApiBoundBlobRef
        {
            IBlobRef baseBlob;
            public BlobRef(IBlobRef baseBlob, Uri sasUrl)
            {
                this.baseBlob = baseBlob;
                this.sasUrl = sasUrl;
                this.ContainerName = baseBlob.ContainerName;
            }

            public string ContainerName { get; set; }

            public string Id => baseBlob.Id;

            private Uri sasUrl;

            public Task<TResult> LoadBytesAsync<TResult>(
                Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null) =>
                    baseBlob.LoadBytesAsync(onFound, onNotFound, onFailure: onFailure);

            public Task<TResult> LoadStreamAsync<TResult>(
                    Func<string, Stream, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                    Func<TResult> onNotFound,
                    Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default) =>
                baseBlob.LoadStreamAsync(onFound: onFound, onNotFound: onNotFound, onFailure: onFailure);

            public Task<TResult> LoadStreamToAsync<TResult>(Stream stream,
                    Func<string, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                    Func<TResult> onNotFound,
                    Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default) =>
                baseBlob.LoadStreamToAsync(stream, onFound: onFound, onNotFound: onNotFound, onFailure: onFailure);

            public void Write(JsonWriter writer, JsonSerializer serializer,
                IHttpRequest httpRequest, IAzureApplication application)
            {
                writer.WriteValue(sasUrl);
            }
        }
    }
}


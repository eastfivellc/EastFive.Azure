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
        public static (IBlobRef, Uri) GenerateBlobFileSasLink(this IBlobRef blobRef)
        {
            var blobSasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = blobRef.ContainerName,
                BlobName = blobRef.Id,
                ExpiresOn = DateTime.UtcNow.AddMinutes(15),//Let SAS token expire after 5 minutes.
            };
            blobSasBuilder.SetPermissions(BlobSasPermissions.Write);

            var azureDriver = AzureTableDriverDynamic.FromSettings();
            var blobClient = azureDriver.BlobClient;
            var sharedKeyCredential = azureDriver.StorageSharedKeyCredential;
            var sasToken = blobSasBuilder.ToSasQueryParameters(sharedKeyCredential).ToString();
            var sasUrl = blobClient.Uri
                .AppendToPath(blobRef.ContainerName)
                .AppendToPath(blobRef.Id)
                .SetQuery(sasToken);
            var newBlobRef = new BlobRef(blobRef.ContainerName, blobRef.Id, sasUrl);
            return (newBlobRef, sasUrl);
        }

        private class BlobRef : IBlobRef, IApiBoundBlobRef
        {
            public BlobRef(string containerName, string id, Uri sasUrl)
            {
                this.Id = id;
                ContainerName = containerName;
                this.sasUrl = sasUrl;
            }

            public string ContainerName { get; set; }

            public string Id { get; set; }

            private Uri sasUrl;

            public Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], string, string, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null)
            {
                throw new NotImplementedException();
            }

            public void Write(JsonWriter writer, JsonSerializer serializer,
                IHttpRequest httpRequest, IAzureApplication application)
            {
                writer.WriteValue(sasUrl);
            }
        }
    }
}


using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using EastFive.Azure.StorageTables.Driver;

using Microsoft.Azure.Cosmos.Table;
using Azure.Storage.Blobs;
using System.IO;
using Azure.Storage.Blobs.Models;

namespace EastFive.Api.Azure.Persistence
{
    [Serializable]
    [DataContract]
    internal class Content : TableEntity
    {
        private const string ContainerName = "content";

        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id { get { return Guid.Parse(this.RowKey); } }
        
        public Guid IntegrationId { get; set; }

        private static BlobServiceClient BlobStore()
        {
            return Web.Configuration.Settings.GetString(EastFive.Azure.AppSettings.Persistence.StorageTables.ConnectionString,
                (connectionString) =>
                {
                    return new BlobServiceClient(connectionString,
                            new BlobClientOptions
                            {
                                // Retry = new global::Azure.Core.RetryOptions()
                            });
                },
                (issue) =>
                {
                    throw new Exception($"Azure storage key not specified: {issue}");
                });
        }

        private static async Task<BlobClient> GetBlobClientAsync(string containerReference, string blockId)
        {
            var blobClient = BlobStore();
            var container = blobClient.GetBlobContainerClient(containerReference);
            var createResponse = await container.CreateIfNotExistsAsync();
            //global::Azure.ETag created = createResponse.Value.ETag;
            return container.GetBlobClient(blockId);
        }

        public static async Task<TResult> CreateAsync<TResult>(Guid contentId, string contentType, byte[] content,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(ContainerName, contentId.ToString("N"));

                using (var stream = new MemoryStream(content))
                {
                    await blockClient.UploadAsync(stream,
                        new BlobUploadOptions
                        {
                            HttpHeaders = new global::Azure.Storage.Blobs.Models.BlobHttpHeaders()
                            {
                                ContentType = contentType,
                            }
                        });
                }
                return onCreated();
            }
            catch (StorageException ex)
            {
                if (ex.IsProblemResourceAlreadyExists())
                    return onAlreadyExists();
                throw;
            }
        }

        public Task<TResult> FindByIdAsync<TResult>(Guid contentId,
            Func<string, byte[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            return FindContentByIdAsync(contentId, onFound, onNotFound);
        }
        
        public static async Task<TResult> FindContentTypeByIdAsync<TResult>(Guid contentId,
            Func<string, TResult> onFound,
            Func<TResult> onNotFound)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(ContainerName, contentId.ToString("N"));
                var properties = await blockClient.GetPropertiesAsync();
                var contentType = (!String.IsNullOrWhiteSpace(properties.Value.ContentType)) ?
                        properties.Value.ContentType
                        :
                        "image/*";
                return onFound(contentType);
            }
            catch (StorageException ex)
            {
                if (ex.IsProblemDoesNotExist())
                    return onNotFound();
                throw;
            }
        }

        public static async Task<TResult> FindContentByIdAsync<TResult>(Guid contentId,
            Func<string, byte[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            try
            {
                var blockClient = await GetBlobClientAsync(ContainerName, contentId.ToString("N"));
                var returnStream = await blockClient.OpenReadAsync(
                    new BlobOpenReadOptions(true)
                    {
                        Conditions = new BlobRequestConditions()
                        {
                        }
                    });
                var properties = await blockClient.GetPropertiesAsync();
                var image = await returnStream.ToBytesAsync();
                var contentType = (!String.IsNullOrWhiteSpace(properties.Value.ContentType)) ?
                    properties.Value.ContentType
                    :
                    "image/*";
                return onFound(contentType, image);
            }
            catch (StorageException storageEx)
            {
                if (storageEx.IsProblemDoesNotExist())
                    return onNotFound();
                throw;
            }
        }
    }
}
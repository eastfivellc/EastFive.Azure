using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BlackBarLabs.Persistence.Azure;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Security.SessionServer;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Microsoft.WindowsAzure.Storage.Table;

namespace EastFive.Api.Azure.Persistence
{
    [Serializable]
    [DataContract]
    internal class Content : TableEntity
    {
        [IgnoreDataMember]
        [IgnoreProperty]
        public Guid Id { get { return Guid.Parse(this.RowKey); } }
        
        public Guid IntegrationId { get; set; }

        private static CloudBlobClient BlobStore(CloudStorageAccount cloudStorageAccount = null)
        {
            if (cloudStorageAccount == null)
                cloudStorageAccount = Web.Configuration.Settings.GetString(EastFive.Azure.AppSettings.ASTConnectionStringKey,
                    (storageSetting) =>
                    {
                        return CloudStorageAccount.Parse(storageSetting);
                    },
                    (issue) =>
                    {
                        throw new Exception($"Azure storage key not specified: {issue}");
                    });

            var blobClient = cloudStorageAccount.CreateCloudBlobClient();
            blobClient.DefaultRequestOptions.RetryPolicy = new ExponentialRetry(TimeSpan.FromSeconds(1), 10);
            return blobClient;
        }

        public static async Task<TResult> CreateAsync<TResult>(Guid contentId, string contentType, byte[] content,
            Func<TResult> onCreated,
            Func<TResult> onAlreadyExists)
        {
            var container = BlobStore().GetContainerReference("content");
            await container.CreateIfNotExistsAsync();
            var blockBlob = container.GetBlockBlobReference(contentId.ToString("N"));
            try
            {
                if (contentType.HasBlackSpace())
                    blockBlob.Properties.ContentType = contentType;
                using (var stream = await blockBlob.OpenWriteAsync())
                {
                    await stream.WriteAsync(content, 0, content.Length);
                }
                return onCreated();
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException ex)
            {
                if (ex.IsProblemResourceAlreadyExists())
                    return onAlreadyExists();
                throw;
            }
        }

        public async Task<TResult> FindByIdAsync<TResult>(Guid contentId,
            Func<string, byte[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            var container = BlobStore().GetContainerReference("content");
            await container.CreateIfNotExistsAsync();
            try
            {
                var blockBlob = container.GetBlockBlobReference(contentId.ToString("N"));
                using (var stream = await blockBlob.OpenReadAsync())
                {
                    var image = await stream.ToBytesAsync();
                    var contentType = (!String.IsNullOrWhiteSpace(blockBlob.Properties.ContentType)) ?
                        blockBlob.Properties.ContentType
                        :
                        "image/*";
                    return onFound(contentType, image);
                }
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException ex)
            {
                if (ex.IsProblemDoesNotExist())
                    return onNotFound();
                throw;
            }
        }
        
        public static async Task<TResult> FindContentTypeByIdAsync<TResult>(Guid contentId,
            Func<string, TResult> onFound,
            Func<TResult> onNotFound)
        {
            var container = BlobStore().GetContainerReference("content");
            await container.CreateIfNotExistsAsync();
            try
            {
                var blockBlob = await container.GetBlobReferenceFromServerAsync(contentId.ToString("N"));
                var contentType = (!String.IsNullOrWhiteSpace(blockBlob.Properties.ContentType)) ?
                        blockBlob.Properties.ContentType
                        :
                        "image/*";
                return onFound(contentType);
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException ex)
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
            var container = BlobStore().GetContainerReference("content");
            await container.CreateIfNotExistsAsync();
            try
            {
                var blockBlob = container.GetBlockBlobReference(contentId.ToString("N"));
                using (var stream = await blockBlob.OpenReadAsync())
                {
                    var image = await stream.ToBytesAsync();
                    var contentType = (!String.IsNullOrWhiteSpace(blockBlob.Properties.ContentType)) ?
                        blockBlob.Properties.ContentType
                        :
                        "image/*";
                    return onFound(contentType, image);
                }
            }
            catch (Microsoft.WindowsAzure.Storage.StorageException ex)
            {
                if (ex.IsProblemDoesNotExist())
                    return onNotFound();
                throw;
            }
        }
    }
}
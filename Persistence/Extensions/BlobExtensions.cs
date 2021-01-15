using BlackBarLabs.Persistence.Azure.StorageTables;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;

namespace BlackBarLabs.Identity.AzureStorageTables.Extensions
{
    public static class BlobExtensions
    {

        //public static Task<TResult> SaveBlobAsync<TResult>(this Persistence.Azure.DataStores context, Type containerReference, Guid id, byte[] data,
        //    Func<TResult> success,
        //    Func<string, TResult> failure)
        //{
        //    return context.SaveBlobAsync(containerReference.GetType().Name, id, data, new Dictionary<string, string>(), string.Empty,  success, failure);
        //}

        //public static async Task<TResult> SaveBlobAsync<TResult>(this Persistence.Azure.DataStores context, string containerReference,
        //        Guid id, byte[] data,
        //        Dictionary<string, string> metadata,
        //        string contentType,
        //    Func<TResult> success,
        //    Func<string, TResult> failure)
        //{
        //    try
        //    {
        //        var blockId = id.AsRowKey();
        //        var container = context.BlobStore.GetContainerReference(containerReference);
        //        await container.CreateIfNotExistsAsync();
        //        var blockBlob = container.GetBlockBlobReference(blockId);
                
        //        await blockBlob.UploadFromByteArrayAsync(data, 0, data.Length);
                
        //        foreach (var item in metadata)
        //        {
        //            blockBlob.Metadata[item.Key] = item.Value;
        //        }
        //        if (metadata.Count > 0)
        //            await blockBlob.SetMetadataAsync();

        //        if (!string.IsNullOrEmpty(contentType))
        //        {
        //            blockBlob.Properties.ContentType = contentType;
        //            await blockBlob.SetPropertiesAsync();
        //        }

        //        return success();
        //    }
        //    catch (Exception ex)
        //    {
        //        return failure(ex.Message);
        //    }
        //}

        //public static async Task<TResult> SaveBlobIfNotExistsAsync<TResult>(this Persistence.Azure.DataStores context, string containerReference, 
        //        Guid id, byte[] data, Dictionary<string, string> metadata,
        //    Func<TResult> success,
        //    Func<TResult> blobAlreadyExists,
        //    Func<string, TResult> failure)
        //{
        //    try
        //    {
        //        var blockId = id.AsRowKey();
        //        var container = context.BlobStore.GetContainerReference(containerReference);
        //        if (!await container.ExistsAsync())
        //            return await context.SaveBlobAsync(containerReference, id, data, new Dictionary<string, string>(), string.Empty, success, failure);

        //        var blockBlob = container.GetBlockBlobReference(blockId);
        //        if (await blockBlob.ExistsAsync())
        //            return blobAlreadyExists();

        //        foreach (var item in metadata)
        //        {
        //            blockBlob.Metadata[item.Key] = item.Value;
        //        }
        //        if (metadata.Count > 0)
        //            await blockBlob.SetMetadataAsync();

        //        return await context.SaveBlobAsync(containerReference, id, data, new Dictionary<string, string>(), string.Empty, success, failure);
        //    }
        //    catch (Exception ex)
        //    {
        //        return failure(ex.Message);
        //    }
        //}

        //public static async Task<TResult> SaveBlobIfNotExistsAsync<TResult>(this Persistence.Azure.DataStores context, string containerReference, Guid id, byte[] data,
        //    Func<TResult> success,
        //    Func<TResult> blobAlreadyExists,
        //    Func<string, TResult> failure)
        //{
        //    try
        //    {
        //        return await SaveBlobIfNotExistsAsync(context, containerReference, id, data, new Dictionary<string, string>(), success, blobAlreadyExists, failure);
        //    }
        //    catch (Exception ex)
        //    {
        //        return failure(ex.Message);
        //    }
        //}


        //public static async Task<TResult> ReadBlobAsync<TResult>(this Persistence.Azure.DataStores context, string containerReference, Guid id, 
        //    Func<Stream, TResult> success,
        //    Func<string, TResult> failure)
        //{
        //    try
        //    {
        //        var container = context.BlobStore.GetContainerReference(containerReference);
        //        var blockId = id.AsRowKey();
        //        var blob = container.GetBlobReference(blockId);
        //        var returnStream = await blob.OpenReadAsync();
        //        return success(returnStream);
        //    }
        //    catch (Exception ex)
        //    {
        //        return failure(ex.Message);
        //    }
        //}

        //public static async Task<TResult> ReadBlobAsync<TResult>(this Persistence.Azure.DataStores context, string containerReference, Guid id,
        //    Func<Stream, string, IDictionary<string,string>, TResult> success,
        //    Func<string, TResult> failure)
        //{
        //    try
        //    {
        //        var container = context.BlobStore.GetContainerReference(containerReference);
        //        var blockId = id.AsRowKey();
        //        var blob = await container.GetBlobReferenceFromServerAsync(blockId);
        //        var returnStream = await blob.OpenReadAsync(AccessCondition.GenerateEmptyCondition(),
        //            new BlobRequestOptions(), new OperationContext());
        //        return success(returnStream, blob.Properties.ContentType, blob.Metadata);
        //    }
        //    catch (Exception ex)
        //    {
        //        return failure(ex.Message);
        //    }
        //}

        //public static async Task<TResult> DeleteBlobAsync<TResult>(this Persistence.Azure.DataStores context, string containerReference, Guid id,
        //    Func<TResult> success,
        //    Func<string, TResult> failure)
        //{
        //    try
        //    {
        //        var container = context.BlobStore.GetContainerReference(containerReference);
        //        var blockId = id.AsRowKey();
        //        var blob = container.GetBlobReference(blockId);
        //        await blob.DeleteIfExistsAsync();
        //        return success();
        //    }
        //    catch (Exception ex)
        //    {
        //        return failure(ex.Message);
        //    }

        //}

        //public static async Task DeleteBlobContainerAsync(this Persistence.Azure.DataStores context, string containerReference)
        //{
        //    var container = context.BlobStore.GetContainerReference(containerReference);
        //    if (await container.ExistsAsync())
        //        await container.DeleteAsync();
        //}

    }
}

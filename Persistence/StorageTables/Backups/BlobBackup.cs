using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using EastFive.Azure.Persistence.StorageTables.Backups;

namespace EastFive.Azure.Persistence.StorageTables.Backups
{
    public static class BlobFunctions
    {

        //private struct BlobAccess
        //{
        //    public string error;
        //    public string key;
        //    public DateTime expiresUtc;
        //}

        //private struct SparseCloudBlob
        //{
        //    public string contentMD5;
        //    public long length;
        //}

        public static Task ProcessBackupContainersAsync(Containers.ContainerMessage message,
            CancellationToken functionToken, EastFive.Analytics.ILogger logger)
        {
            throw new NotImplementedException();

            //    var EmptyCondition = AccessCondition.GenerateEmptyCondition();
            //    var RequestOptions = new BlobRequestOptions();

            //    logger.Trace($"Starting backup for container {message.name}");
            //    var sw = new Stopwatch();
            //    sw.Start();

            //    var sourceRepo = GetRepository(message.sourceConnectionString);
            //    var destRepo = GetRepository(message.destConnectionString);
            //    await Backup.CreateOrUpdateAsync(destRepo, message.name, message.prefix);
            //    var completedBlobs = 0;


            //    var sourceContainer = sourceRepo.BlobClient.GetContainerReference(message.name);
            //    var sourceContainerExists = await sourceContainer.ExistsAsync();
            //    if (!sourceContainerExists)
            //        return;

            //    var destContainer = destRepo.BlobClient.GetContainerReference(message.name);
            //    var destContainerExists = await destContainer.ExistsAsync();
            //    if (!destContainerExists)
            //    {
            //        var createPermissions = await sourceContainer.GetPermissionsAsync(EmptyCondition, RequestOptions, new OperationContext());
            //        await destContainer.CreateAsync(createPermissions.PublicAccess, RequestOptions, new OperationContext());

            //        var metadataModified = false;
            //        foreach (var item in sourceContainer.Metadata)
            //        {
            //            if (!destContainer.Metadata.ContainsKey(item.Key) || destContainer.Metadata[item.Key] != item.Value)
            //            {
            //                destContainer.Metadata[item.Key] = item.Value;
            //                metadataModified = true;
            //            }
            //        }
            //        if (metadataModified)
            //            await destContainer.SetMetadataAsync(EmptyCondition, RequestOptions, new OperationContext());
            //    }
            //    var keyName = $"{sourceContainer.ServiceClient.Credentials.AccountName}-{message.name}-access";
            //    async Task<BlobAccess> RenewAccessAsync(TimeSpan sourceAccessWindow)
            //    {
            //        try
            //        {
            //            var renewContext = new OperationContext();
            //            var permissions = await sourceContainer.GetPermissionsAsync(EmptyCondition, RequestOptions, renewContext);
            //            permissions.SharedAccessPolicies.Clear();
            //            var access = new BlobAccess
            //            {
            //                key = keyName,
            //                expiresUtc = DateTime.UtcNow.Add(sourceAccessWindow)
            //            };
            //            permissions.SharedAccessPolicies.Add(access.key, new SharedAccessBlobPolicy
            //            {
            //                SharedAccessExpiryTime = access.expiresUtc,
            //                Permissions = SharedAccessBlobPermissions.Read
            //            });
            //            await sourceContainer.SetPermissionsAsync(permissions, EmptyCondition, RequestOptions, renewContext);
            //            return access;
            //        }
            //        catch (Exception e)
            //        {
            //            return new BlobAccess { error = $"Error renewing access policy on container, Detail: {e.Message}" };
            //        }
            //    }
            //    async Task ReleaseAccessAsync()
            //    {
            //        try
            //        {
            //            var releaseContext = new OperationContext();
            //            var permissions = await sourceContainer.GetPermissionsAsync(EmptyCondition, RequestOptions, releaseContext);
            //            permissions.SharedAccessPolicies.Clear();
            //            await sourceContainer.SetPermissionsAsync(permissions, EmptyCondition, RequestOptions, releaseContext);
            //        }
            //        catch (Exception e)
            //        {
            //            // Failure here is no big deal as the container will still be usable
            //            return;
            //        }
            //    }
            //    async Task<BlobAccess> RenewWhenExpiredAsync(BlobAccess access, TimeSpan accessPeriod)
            //    {
            //        if (access.expiresUtc < DateTime.UtcNow + TimeSpan.FromMinutes(5))
            //        {
            //            access = await RenewAccessAsync(accessPeriod);
            //            await Task.Delay(TimeSpan.FromSeconds(10));  // let settle in so first copy will be ok
            //        }
            //        return access;
            //    }


            //    BlobContinuationToken continuationToken = default;


            //    sw.Stop();

            //    await Backup.CreateOrUpdateAsync(destRepo, message.name, message.prefix, completedBlobs, sw.Elapsed.TotalSeconds);
            //    logger.Trace($"copied {completedBlobs} blobs in {sw.Elapsed.TotalSeconds} seconds for {message.prefix}");
        }

        //var completedRows = await sourceRepo.Copy(filter, table, destRepo)
        //    .ToArrayAsync();

        // stuff todo next for ProcessBackupContainer method
        //var sourceRepo = GetRepository(sourceConnectionString);
        //var stuff = await containerResources
        //    .Select(
        //    async (attr) =>
        //    {
        //        var name = attr.Name;
        //        var container = sourceRepo.BlobClient.GetContainerReference(name);
        //        if (!(await container.ExistsAsync()))
        //            return true;
        //
        //        BlobContinuationToken token = default;
        //        int count = 0;
        //        do
        //        {
        //            //new method to listblobs so that continuation is observed in case it goes over 5K
        //            //trial and error on how many blobs can be processed in 10 mins.
        //            var segment = await container.ListBlobsSegmentedAsync("00", token);
        //            token = segment.ContinuationToken;
        //            var results = segment.Results.Cast<CloudBlob>().ToArray();
        //            count += results.Length;
        //        } while (token != null);
        //
        //        return true;
        //    })
        //    .AsyncEnumerable()
        //    .ToArrayAsync();


        // make this yield a list of all like Athena 
        //private static async Task<TResult> FindNextBlobSegmentAsync<TResult>(CloudBlobContainer sourceContainer, BlobContinuationToken continuationToken, string prefix, CancellationToken functionToken,
        //    Func<BlobContinuationToken, CloudBlob[], TResult> onSuccess,
        //    Func<string, TResult> onFailure)
        //{
        //    try
        //    {
        //        var segment = await sourceContainer.ListBlobsSegmentedAsync(prefix, true, BlobListingDetails.UncommittedBlobs, default,
        //            continuationToken, new BlobRequestOptions(), new Microsoft.WindowsAzure.Storage.OperationContext(), functionToken);
        //        var results = segment.Results.Cast<CloudBlob>().ToArray();
        //        return onSuccess(segment.ContinuationToken, results);
        //    }
        //    catch (Exception ex)
        //    {
        //        return onFailure(ex.Message);
        //    }
        //}

        //// Must be public for dynamic casting
        //public struct PaginationResult<T>
        //{
        //    public T[] items;
        //    public BlobContinuationToken token;
        //    public string error;
        //}
        //public static PaginationResult<T> GetDefaultPaginationResult<T>(Exception ex)
        //{
        //    return GetDefaultPaginationResult<T>($"{ex.GetType().Name}:{ex.Message}");
        //}

        //public static PaginationResult<T> GetDefaultPaginationResult<T>(string error = default)
        //{
        //    var result = new PaginationResult<T>
        //    {
        //        token = default,
        //        items = new T[] { },
        //        error = error,
        //    };
        //    return result;
        //}

        //private static IEnumerableAsync<T> GetPaginatedResults<T>(
        //    Func<BlobContinuationToken, Task<PaginationResult<T>>> taskGenerator,
        //        EastFive.Analytics.ILogger logger = default)
        //{
        //    var paginationTask = taskGenerator(default);
        //    return EnumerableAsync.YieldBatch<T>(
        //        async (yieldReturn, yieldBreak) =>
        //        {
        //            if (paginationTask.IsDefaultOrNull())
        //                return yieldBreak;

        //            var paginationResult = await paginationTask;
        //            var moreDataToFetch = paginationResult.token != default;

        //            paginationTask = moreDataToFetch ?
        //                taskGenerator(paginationResult.token)
        //                :
        //                default(Task<PaginationResult<T>>);

        //            if (moreDataToFetch || paginationResult.items.Any())
        //                return yieldReturn(paginationResult.items);

        //            if (paginationResult.error.HasBlackSpace())
        //            {
        //                var scopedLogger = logger.CreateScope($"Paginate<{typeof(T).Name}>[{Guid.NewGuid().ToString().Substring(0, 4)}]");
        //                scopedLogger.Trace(paginationResult.error);
        //            }

        //            return yieldBreak;
        //        });
        //}

        //private static PaginationResult<T> GetNextPaginationResult<T>(BlobResultSegment segment, T[] items)
        //{
        //    return segment
        //        .WhereKey(
        //            kvp => kvp.Key == "next",
        //            kvpNext =>
        //            {
        //                var nextQueryString = kvpNext.Value.Value<string>();

        //                // Query param extraction is not supported on relative URIs so build off of example.com
        //                if (!Uri.TryCreate(new Uri("http://example.com"), nextQueryString, out Uri nextUrl))
        //                    return new PaginationResult<T>
        //                    {
        //                        token = default(int?),
        //                        items = items,
        //                    };

        //                if (!nextUrl.TryGetQueryParam("offset", out string offsetString))
        //                    return new PaginationResult<T>
        //                    {
        //                        token = default(int?),
        //                        items = items,
        //                    };

        //                if (!int.TryParse(offsetString, out int nextOffset))
        //                    return new PaginationResult<T>
        //                    {
        //                        token = default(int?),
        //                        items = items,
        //                    };

        //                return new PaginationResult<T>
        //                {
        //                    token = nextOffset,
        //                    items = items,
        //                };
        //            },
        //            () => new PaginationResult<T>
        //            {
        //                token = default(int?),
        //                items = items,
        //            });
        //}
    }
}
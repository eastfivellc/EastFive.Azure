using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Persistence.Azure.StorageTables;
using EastFive.Serialization;
using System.Net.Http.Headers;
using EastFive.Linq;
using System.Net;
using EastFive.Extensions;
using EastFive.Azure.StorageTables.Driver;
using System.IO;
using EastFive.Collections.Generic;
using RestSharp;
using System.Threading;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos.Table;

namespace EastFive.Persistence.Azure.StorageTables.Caching
{
    [StorageTable]
    public struct CacheItem : IReferenceable
    {
        [JsonIgnore]
        public Guid id => cacheItemRef.id;

        public const string IdPropertyName = "id";
        [RowKey]
        [StandardParititionKey]
        public IRef<CacheItem> cacheItemRef;

        [Storage]
        public string source;

        [Storage]
        public IDictionary<Guid, DateTime> whenLookup;

        [Storage]
        public IDictionary<string, Guid> checksumLookup;

        public static Task<TResult> GetHttpResponseAsync<TResult>(Uri source,
            Func<HttpResponseMessage, TResult> onRetrievedOrCached,
            Func<TResult> onFailedToRetrieve,
                DateTime? newerThanUtcMaybe = default,
                DateTime? asOfUtcMaybe = default,
                Func<HttpRequestMessage, HttpRequestMessage> mutateHttpRequest = default)
        {
            return RetrieveAsync(source,
                    default, default,
                    async (request) =>
                    {
                        var mutatedRequest = mutateHttpRequest(request);
                        using (var httpClient = new HttpClient())
                        {
                            // Disposed by caller
                            var response = await httpClient.SendAsync(mutatedRequest);
                            return response;
                        }
                    },
                onRetrievedOrCached,
                onFailedToRetrieve,
                    newerThanUtcMaybe, asOfUtcMaybe);
        }

        public static Task<TResult> PostRestResponseAsync<TResult>(Uri source,
                IDictionary<string, string> headers, byte [] body,
            Func<HttpResponseMessage, TResult> onRetrievedOrCached,
            Func<TResult> onFailedToRetrieve,
                DateTime? newerThanUtcMaybe = default,
                DateTime? asOfUtcMaybe = default,
                Func<RestRequest, RestRequest> mutateRestRequest = default)
        {
            return RetrieveAsync(source,
                    headers, body,
                    (request) =>
                    {
                        var sleepTime = TimeSpan.FromSeconds(1);
                        while (true)
                        {
                            try
                            {
                                var client = new RestClient(source);
                                var restRequest = new RestRequest(Method.POST);
                                var mutatedRequest = mutateRestRequest(restRequest);
                                var restResponse = client.Execute(mutatedRequest);
                                var response = new HttpResponseMessage(restResponse.StatusCode)
                                {
                                    RequestMessage = new HttpRequestMessage()
                                    {
                                        RequestUri = source,
                                    },
                                    Content = new StringContent(restResponse.Content, System.Text.Encoding.UTF8, "application/json"),
                                };
                                return response.AsTask();
                            } catch (Exception)
                            {
                                Thread.Sleep(sleepTime);
                                sleepTime = TimeSpan.FromSeconds(sleepTime.TotalSeconds * 2.0);
                            }
                        }
                    },
                onRetrievedOrCached,
                onFailedToRetrieve,
                    newerThanUtcMaybe, asOfUtcMaybe);
        }

        private static Task<TResult> RetrieveAsync<TResult>(Uri source,
                IDictionary<string, string> headers, byte[] body,
                Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync,
            Func<HttpResponseMessage, TResult> onRetrievedOrCached,
            Func<TResult> onFailedToRetrieve,
                DateTime? newerThanUtcMaybe = default(DateTime?),
                DateTime? asOfUtcMaybe = default(DateTime?))
        {
            if (!source.IsAbsoluteUri)
                throw new ArgumentException($"Url `{source}` is not an absolute URL.");

            var cacheId = source.AbsoluteUri.MD5HashGuid();
            cacheId = headers
                .NullToEmpty()
                .Aggregate(cacheId,
                    (cId, header) => cId.ComposeGuid(
                        header.Key.MD5HashGuid().ComposeGuid(
                            header.Value.MD5HashGuid())));
            if (body.AnyNullSafe())
                cacheId = cacheId.ComposeGuid(body.MD5HashGuid());

            var cacheRef = cacheId.AsRef<CacheItem>();
            return cacheRef.StorageCreateOrUpdateAsync<CacheItem, TResult>(
                async (created, item, saveAsync) =>
                {
                    if (item.whenLookup.IsDefaultOrNull())
                        item.whenLookup = new Dictionary<Guid, DateTime>();
                    if (item.checksumLookup.IsDefaultOrNull())
                        item.checksumLookup = new Dictionary<string, Guid>();
                    bool ShouldFetch()
                    {
                        if (created)
                            return true;
                        if (!item.whenLookup.AnyNullSafe())
                            return true;
                        if (newerThanUtcMaybe.HasValue)
                        {
                            var newerThanUtc = newerThanUtcMaybe.Value;
                            var newerIds = item.whenLookup
                                .Where(lookupKvp => lookupKvp.Value > newerThanUtc);
                            if (!newerIds.Any())
                                return true;
                        }
                        return false;
                    }

                    if (ShouldFetch())
                    {
                        return await FetchRequest();
                    }

                    var cacheResponse = await item.ConstructResponseAsync(asOfUtcMaybe);
                    if(!cacheResponse.IsSuccessStatusCode)
                        return await FetchRequest();

                    return onRetrievedOrCached(cacheResponse);

                    async Task<TResult> FetchRequest()
                    {
                        var sleepTime = TimeSpan.FromSeconds(1);
                        while (true)
                        {
                            var request = new HttpRequestMessage();
                            request.RequestUri = source;
                            try
                            {
                                var response = await sendAsync(request);
                                var responseData = await response.Content.ReadAsByteArrayAsync();
                                await item.ImportResponseAsync(
                                        response, responseData, saveAsync);

                                var undisposedResponse = await item.ConstructResponseAsync(asOfUtcMaybe);
                                return onRetrievedOrCached(response);
                            }
                            catch (TaskCanceledException)
                            {
                                Thread.Sleep(sleepTime);
                                sleepTime = TimeSpan.FromSeconds(sleepTime.TotalSeconds * 2.0);
                            }
                            catch (HttpRequestException requestEx)
                            {
                                if (!requestEx.InnerException.IsDefaultOrNull())
                                {
                                    if (requestEx.InnerException is WebException)
                                    {
                                        var webEx = requestEx.InnerException as WebException;
                                        if (
                                            webEx.Status == WebExceptionStatus.Timeout ||
                                            webEx.Status == WebExceptionStatus.RequestCanceled ||
                                            // Also a timeout conditions.... 
                                            webEx.Status == WebExceptionStatus.ConnectFailure ||
                                            webEx.Status == WebExceptionStatus.ConnectionClosed
                                            )
                                        {
                                            Thread.Sleep(sleepTime);
                                            sleepTime = TimeSpan.FromSeconds(sleepTime.TotalSeconds * 2.0);
                                            continue;
                                        }
                                    }
                                }
                                return onFailedToRetrieve();
                            }
                            catch (Exception)
                            {
                                return onFailedToRetrieve();
                            }
                        }
                    }
                });
        }

        private BlobServiceClient GetBlobClient()
        {
            var blobClient = Web.Configuration.Settings.GetString(
                    EastFive.Azure.AppSettings.ASTConnectionStringKey,
                (storageSetting) =>
                {
                    var cloudStorageAccount = CloudStorageAccount.Parse(storageSetting);
                    var bc = new BlobServiceClient(storageSetting);
                    return bc;
                },
                (issue) =>
                {
                    throw new Exception($"Azure storage key not specified: {issue}");
                });
            return blobClient;
        }

        private async Task ImportResponseAsync(HttpResponseMessage response, byte[] responseData,
            Func<CacheItem, Task> saveAsync)
        {
            var when = DateTime.UtcNow;
            var checksum = responseData.Md5Checksum();
            if (this.checksumLookup.ContainsKey(checksum))
            {
                var blobIdExisting = this.checksumLookup[checksum];
                this.whenLookup[blobIdExisting] =  when;
                await saveAsync(this);
                return;
            }
            var contentType = response.Content.Headers.ContentType.IsDefaultOrNull() ?
                string.Empty
                :
                response.Content.Headers.ContentType.MediaType;

            var metadata = new Dictionary<string, string>();
            metadata.AddOrReplace("statuscode",
                Enum.GetName(typeof(HttpStatusCode), response.StatusCode));
            metadata.AddOrReplace("method", response.RequestMessage.Method.Method);
            metadata.AddOrReplace("requestUri", response.RequestMessage.RequestUri.AbsoluteUri);
            if (!response.Headers.ETag.IsDefaultOrNull())
                metadata.AddOrReplace("eTag", response.Headers.ETag.Tag);
            if (response.Content.Headers.ContentEncoding.AnyNullSafe())
                metadata.AddOrReplace("ContentEncoding",
                    response.Content.Headers.ContentEncoding.First());
            if (!response.Content.Headers.ContentLocation.IsDefaultOrNull())
                metadata.AddOrReplace("ContentLocation",
                    response.Content.Headers.ContentLocation.AbsoluteUri);

            var blobId = await responseData.BlobCreateAsync("cache",
                onSuccess: blobId => blobId,
                contentType: contentType,
                metadata: metadata);

            this.whenLookup.Add(blobId, when);
            this.checksumLookup.Add(checksum, blobId);
            await saveAsync(this);
        }

        private async Task<HttpResponseMessage> ConstructResponseAsync(DateTime? asOfUtcMaybe)
        {
            var asOfUtc = asOfUtcMaybe.HasValue ? asOfUtcMaybe.Value : DateTime.UtcNow;
            var blobId = this.whenLookup
                .OrderByDescending(whenKvp => whenKvp.Value)
                .First<KeyValuePair<Guid, DateTime>, Guid>(
                    (whenKvp, next) =>
                    {
                        if (whenKvp.Value < asOfUtc)
                            return whenKvp.Key;
                        return next();
                    },
                    () =>
                    {
                        throw new Exception("No cached values");
                    });

            return await await blobId.BlobLoadStreamAsync("cache",
                async (stream, contentType, metadata) =>
                {
                    var statusCode = HttpStatusCode.OK;
                    if (metadata.ContainsKey("statuscode"))
                        Enum.TryParse(metadata["statuscode"], out statusCode);
                    var method = default(HttpMethod);
                    if (metadata.ContainsKey("method"))
                        method = new HttpMethod(metadata["method"]);
                    var requestUri = default(Uri);
                    if (metadata.ContainsKey("requestUri"))
                        Uri.TryCreate(metadata["requestUri"], UriKind.RelativeOrAbsolute, out requestUri);
                    var responseBytes = await stream.ToBytesAsync(); ;
                    var response = new HttpResponseMessage(statusCode)
                    {
                        Content = new ByteArrayContent(responseBytes),
                        RequestMessage = new HttpRequestMessage(method, requestUri),
                    };
                    if (metadata.ContainsKey("eTag"))
                        response.Headers.ETag = new EntityTagHeaderValue(metadata["eTag"]);
                    if (contentType.HasBlackSpace())
                        response.Content.Headers.ContentType =
                            new MediaTypeHeaderValue(contentType);
                    if (metadata.ContainsKey("ContentEncoding"))
                        response.Content.Headers.ContentEncoding.Add(
                            metadata["ContentEncoding"]);
                    if (metadata.ContainsKey("ContentLocation"))
                        if (Uri.TryCreate(metadata["ContentLocation"], UriKind.RelativeOrAbsolute, out Uri contentLocation))
                            response.Content.Headers.ContentLocation = contentLocation;

                    return response;
                },
                onNotFound: () =>
                     new HttpResponseMessage(HttpStatusCode.NotFound).AsTask());
        }
    }
}

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

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization;
using EastFive.Reflection;


namespace EastFive.Azure.Persistence.Blobs
{
    [BlobRefSerializer]
    [BlobRefBinding]
    public interface IBlobRef
    {
        string ContainerName { get; }

        string Id { get; }

        Task<TResult> LoadAsync<TResult>(
                Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
                Func<TResult> onNotFound,
                Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default);
    }
}

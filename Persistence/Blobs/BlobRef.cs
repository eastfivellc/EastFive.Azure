using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using EastFive;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Azure.Persistence.StorageTables;
using EastFive.Azure.StorageTables.Driver;
using EastFive.Collections.Generic;
using EastFive.Persistence.Azure.StorageTables.Driver;
using EastFive.Serialization;
using EastFive.Reflection;
using EastFive.Api.Meta.Flows;

namespace EastFive.Azure.Persistence.Blobs
{
    [BlobRefSerializer]
    [BlobRefBinding]
    [BlobRefMeta]
    public interface IBlobRef
    {
        const string DefaultMediaType = "application/octet-stream";

        string ContainerName { get; }

        string Id { get; }

        Task<TResult> LoadBytesAsync<TResult>(
            Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default);

        Task<TResult> LoadStreamAsync<TResult>(
            Func<string, Stream, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default);

        Task<TResult> LoadStreamToAsync<TResult>(Stream stream,
            Func<string, MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = default);
    }

    public static class BlobRefLoadExtensions
    {

    }

    public class BlobRefMetaAttribute : Attribute, IDefineWorkflowParameterAttributes
    {
        public bool IsFileType(ParameterInfo parameter)
        {
            if (!parameter.ParameterType.IsAssignableTo(typeof(IBlobRef)))
                throw new ArgumentException($"{parameter.ParameterType.DisplayFullName} cannot be decorated with {nameof(BlobRefMetaAttribute)}");

            return true;
        }
    }
}

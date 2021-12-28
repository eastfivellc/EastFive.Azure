﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Mime;

using Newtonsoft.Json;

using EastFive;
using EastFive.Extensions;
using EastFive.Api;
using EastFive.Azure.Persistence.StorageTables;
using System.Net.Http.Headers;

namespace EastFive.Azure.Persistence.Blobs
{
    internal class BlobRefRawData : IBlobRef
    {
        public string Id { get; private set; }

        public string ContainerName { get; set; }

        private byte[] content;

        public BlobRefRawData(string containerName, byte [] content)
        {
            Id = Guid.NewGuid().ToString("N");
            this.content = content;
            this.ContainerName = containerName;
        }

        public Task<TResult> LoadAsync<TResult>(
            Func<string, byte[], MediaTypeHeaderValue, ContentDispositionHeaderValue, TResult> onFound,
            Func<TResult> onNotFound,
            Func<ExtendedErrorInformationCodes, string, TResult> onFailure = null)
        {
            MediaTypeHeaderValue.TryParse(String.Empty,
                out MediaTypeHeaderValue mediaType);
            ContentDispositionHeaderValue.TryParse(string.Empty,
                out ContentDispositionHeaderValue contentDisposition);
            return onFound(this.Id, content, mediaType, contentDisposition).AsTask();
        }

        public void Write(JsonWriter writer, JsonSerializer serializer,
            IHttpRequest httpRequest, IAzureApplication application)
        {
            writer.WriteValue(content);
        }
    }
}


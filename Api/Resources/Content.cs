﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using BlackBarLabs.Web;
using BlackBarLabs.Api;
using System.Threading.Tasks;
using EastFive.Api.Controllers;
using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;
using EastFive.Azure.Persistence.AzureStorageTables;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using EastFive.Extensions;
using EastFive.Linq;

namespace EastFive.Api.Azure.Resources
{
    [FunctionViewController4(
        Route = "Content",
        Resource = typeof(Content),
        ContentType = "x-application/content",
        ContentTypeVersion = "0.1")]
    public class Content : IReferenceable
    {
        [JsonIgnore]
        public Guid id => contentRef.id;

        public const string ContentIdPropertyName = "id";
        [JsonProperty(PropertyName = ContentIdPropertyName)]
        public IRef<Content> contentRef;

        public const string ContentPropertyName = "content";
        [JsonProperty(PropertyName = ContentPropertyName)]
        public byte[] Data { get; set; }

        public const string WidthPropertyName = "width";
        [JsonProperty(PropertyName = WidthPropertyName)]
        public int? Width { get; set; }

        public const string HeightPropertyName = "height";
        [JsonProperty(PropertyName = HeightPropertyName)]
        public int? Height { get; set; }

        public const string FillPropertyName = "fill";
        [JsonProperty(PropertyName = FillPropertyName)]
        public bool? Fill { get; set; }

        public const string StreamingPropertyName = "streaming";
        [JsonProperty(PropertyName = StreamingPropertyName)]
        public bool? Streaming { get; set; }

        public const string ContentTypePropertyName = "content_type";
        [JsonProperty(PropertyName = ContentTypePropertyName)]
        public string contentType { get; set; }

        [HttpPost]
        public static async Task<HttpResponseMessage> CreateContentAsync(
                [QueryParameter(CheckFileName = true, Name = ContentIdPropertyName)]Guid contentId,
                [QueryParameter(Name = ContentPropertyName)]ByteArrayContent content,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists)
        {
            var contentType = content.Headers.ContentType.MediaType;
            var contentBytes = await content.ReadAsByteArrayAsync();
            return await EastFive.Api.Azure.Content.CreateContentAsync(contentId, contentType, contentBytes,
                () => onCreated(),
                () => onAlreadyExists());
        }

        [HttpPost]
        public static async Task<HttpResponseMessage> CreateContentFormAsync(
                [Property(Name = ContentIdPropertyName)]Guid contentId,
                [Property(Name = ContentPropertyName)]byte[] contentBytes,
                [Header(Content = ContentPropertyName)]System.Net.Http.Headers.MediaTypeHeaderValue mediaHeader,
            CreatedResponse onCreated,
            AlreadyExistsResponse onAlreadyExists)
        {
            var contentType = mediaHeader.MediaType;
            return await EastFive.Api.Azure.Content.CreateContentAsync(contentId, contentType, contentBytes,
                () => onCreated(),
                () => onAlreadyExists());
        }

        [HttpGet]
        public static async Task<HttpResponseMessage> QueryByContentIdAsync(
                [QueryParameter(CheckFileName = true, Name = ContentIdPropertyName)]Guid contentId,
                [OptionalQueryParameter]int? width,
                [OptionalQueryParameter]int? height,
                [OptionalQueryParameter]bool? fill,
                [OptionalQueryParameter]string renderer,
            HttpRequestMessage request,
            System.Web.Http.Routing.UrlHelper url)
        {
            var response = await EastFive.Api.Azure.Content.FindContentByContentIdAsync(contentId,
                (contentType, image) =>
                {
                    if (renderer.HasBlackSpace())
                    {
                        if (renderer.ToLower() == "unzip")
                        {
                            using (var compressedStream = new MemoryStream(image))
                            using (var zipStream = new System.IO.Compression.ZipArchive(compressedStream, ZipArchiveMode.Read))
                            using (var resultStream = new MemoryStream())
                            {
                                var zipFile = zipStream.Entries.First();
                                zipFile.Open().CopyTo(resultStream);
                                var data = resultStream.ToArray();
                                return request.CreateFileResponse(data, "application/object", filename: zipFile.Name);
                            }
                        }
                    }

                    if (contentType.StartsWith("video", StringComparison.InvariantCultureIgnoreCase) &&
                        (width.HasValue || height.HasValue || fill.HasValue))
                    {
                        var videoPreviewImage = default(System.Drawing.Image); // Properties.Resources.video_preview;
                        return request.CreateImageResponse(videoPreviewImage,
                            width: width, height: height, fill: fill,
                            filename: contentId.ToString("N"));
                    }
                    return request.CreateImageResponse(image,
                        width: width, height: height, fill: fill,
                        filename: contentId.ToString("N"),
                        contentType: contentType);
                },
                () => request.CreateResponse(HttpStatusCode.NotFound),
                () => request.CreateResponse(HttpStatusCode.Unauthorized));
            return response;
        }

        public Task<TResult> LoadStreamAsync<TResult>(
            Func<Stream, string, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return contentRef.id.BlobLoadStreamAsync("content",
                onFound,
                onNotFound);
        }

        public static Task<TResult> FindContentStreamByIdAsync<TResult>(Guid contentId,
            Func<string, byte[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            return contentId.BlobLoadBytesAsync("content",
                (data, contentType) => onFound(contentType, data),
                onNotFound);
        }

        public static Task<TResult> FindContentByIdAsync<TResult>(Guid contentId,
            Func<string, byte[], TResult> onFound,
            Func<TResult> onNotFound)
        {
            return contentId.BlobLoadBytesAsync("content",
                (data, contentType) => onFound(contentType, data),
                onNotFound);
        }

        private static async Task<HttpResponseMessage> QueryAsVideoStream(
                [QueryParameter(CheckFileName = true, Name = ContentIdPropertyName)]Guid contentId,
                [QueryParameter(Name = StreamingPropertyName)]bool streaming,
                HttpRequestMessage request,
                EastFive.Api.Controllers.Security security,
                System.Web.Http.Routing.UrlHelper url)
        {
            var response = await EastFive.Api.Azure.Content.FindContentByContentIdAsync(contentId,
                    security,
                (contentType, video) => request.CreateResponseVideoStream(video, contentType),
                () => request.CreateResponse(HttpStatusCode.NotFound),
                () => request.CreateResponse(HttpStatusCode.Unauthorized));
            return response;
        }
    }
}

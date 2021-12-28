using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

using EastFive.Api;
using EastFive.Api.Azure.Resources;
using EastFive.Azure.Persistence.AzureStorageTables;
using EastFive.Extensions;
using EastFive.Linq;
using EastFive.Images;
using EastFive.Web.Configuration;
using System.Drawing;
using EastFive.Azure.Persistence.Blobs;

namespace EastFive.Azure.Media
{
    public static class ContentExtensions
    {
        public static Task<TResult> LoadBytesAsync<TResult>(this IRef<Content> contentRef,
            Func<byte [], string, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return contentRef.id.BlobLoadBytesAsync("content",
                onFound,
                onNotFound);
        }

        public static Task<TResult> LoadStreamAsync<TResult>(this IRef<Content> contentRef,
            Func<Stream, string, TResult> onFound,
            Func<TResult> onNotFound)
        {
            return contentRef.id.BlobLoadStreamAsync("content",
                onFound,
                onNotFound);
        }

        public static Task<TResult> LoadImageAsync<TResult>(this IRef<Content> contentRef,
            Func<System.Drawing.Image, string, TResult> onFound,
            Func<TResult> onNotFound,
            Func<Stream, string, TResult> onInvalidImage = default)
        {
            return contentRef.id.BlobLoadStreamAsync("content",
                (imageStream, contentType) =>
                {
                    //var image = System.Drawing.Image.FromStream(imageStream);
                    if(imageStream.TryReadImage(out Image image))
                        return onFound(image, contentType);

                    if(onInvalidImage.IsDefaultOrNull())
                        return onNotFound();

                    return onInvalidImage(imageStream, contentType);
                },
                onNotFound);
        }

        public static Task<TResult> LoadImageAsync<TResult>(this IBlobRef blobRef,
            Func<System.Drawing.Image, string, TResult> onFound,
            Func<TResult> onNotFound,
            Func<byte [], string, TResult> onInvalidImage = default)
        {
            return blobRef.LoadAsync(
                (blobId, imageBytes, mediaType, disposition) =>
                {
                    //var image = System.Drawing.Image.FromStream(imageStream);
                    var contentType = mediaType.MediaType;
                    if (imageBytes.TryReadImage(out Image image))
                        return onFound(image, contentType);

                    if (onInvalidImage.IsDefaultOrNull())
                        return onNotFound();

                    return onInvalidImage(imageBytes, contentType);
                },
                onNotFound);
        }

        public static Task<IRef<Content>> ContentCreateAsync(this byte[] content,
            string contentType = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return content.BlobCreateAsync("content",
                (contentId) => contentId.AsRef<Content>(),
                contentType: contentType,
                onTimeout: onTimeout);
        }

        public static Task<TResult> SaveBytesAsync<TResult>(this IRef<Content> contentRef, 
                byte[] content,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<Persistence.StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return content.BlobCreateAsync(contentRef.id, "content",
                onSuccess,
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                contentType: contentType,
                onTimeout: onTimeout);
        }

        public static Task<TResult> SaveStreamAsync<TResult>(this IRef<Content> contentRef,
                Stream content,
            Func<TResult> onSuccess,
            Func<TResult> onAlreadyExists = default,
            Func<Persistence.StorageTables.ExtendedErrorInformationCodes, string, TResult> onFailure = default,
            string contentType = default,
            Azure.StorageTables.Driver.AzureStorageDriver.RetryDelegate onTimeout = null)
        {
            return content.BlobCreateAsync(contentRef.id, "content",
                onSuccess,
                onAlreadyExists: onAlreadyExists,
                onFailure: onFailure,
                contentType: contentType,
                onTimeout: onTimeout);
        }

        public static Task<TResult> AnalyzeAsync<TResult>(this IBlobRef contentRef,
            Func<ImageAnalysis, double?, TResult> onAnalyzed,
            Func<TResult> onInvalidFormat = default,
            Func<TResult> onNotFound = default)
        {
            return AppSettings.CognitiveServices.ComputerVisionSubscriptionKey.ConfigurationString(
                subscriptionKey =>
                {
                    return AppSettings.CognitiveServices.ComputerVisionEndpoint.ConfigurationUri(
                        async endpointUri =>
                        {
                            using (var computerVision = new ComputerVisionClient(
                                new ApiKeyServiceClientCredentials(subscriptionKey),
                                new System.Net.Http.DelegatingHandler[] { }))
                            {
                                return await await contentRef.LoadAsync(
                                    async (blobId, imageData, contentType, disposition) =>
                                    {
                                        var widthMultiplier = default(double?);
                                        if (imageData.Length > 4000000)
                                        {
                                            if (!imageData.TryReadImage(out Image image))
                                            {
                                                if (onInvalidFormat.IsNotDefaultOrNull())
                                                    return onInvalidFormat();
                                                if (onNotFound.IsNotDefaultOrNull())
                                                    return onNotFound();

                                                throw new ArgumentException($"Blob `{blobId}` is not an image.");
                                            }
                                            using (var newImageStream = new MemoryStream())
                                            {
                                                widthMultiplier = Math.Sqrt(4000000.0 / imageData.Length);
                                                image
                                                    .ResizeImage(
                                                        (int)(image.Width * widthMultiplier),
                                                        (int)(image.Height * widthMultiplier))
                                                    .Save(newImageStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                                                newImageStream.Position = 0;
                                                imageData = await newImageStream.ToBytesAsync();
                                            }
                                        }
                                        computerVision.Endpoint = endpointUri.OriginalString;
                                        var featuresToSearchFor = VisualFeatureTypes.Categories.AsArray()
                                                .Append(VisualFeatureTypes.Description)
                                                .Append(VisualFeatureTypes.ImageType)
                                                .Append(VisualFeatureTypes.Objects)
                                                .Append(VisualFeatureTypes.Tags)
                                                .Append(VisualFeatureTypes.Brands)
                                                .Append(VisualFeatureTypes.Color)
                                                .Append(VisualFeatureTypes.Faces)
                                                .ToList();
                                        try
                                        {
                                            using (var imageStream = new MemoryStream(imageData))
                                            {
                                                var analysis = await computerVision.AnalyzeImageInStreamAsync(
                                                    imageStream, featuresToSearchFor);
                                                return onAnalyzed(analysis, widthMultiplier);
                                            }
                                        }
                                        catch (ComputerVisionErrorException ex)
                                        {
                                            throw ex;
                                        }
                                    },
                                    () =>
                                    {
                                        if (onNotFound.IsDefaultOrNull())
                                            throw new ResourceNotFoundException();
                                        return onNotFound().AsTask();
                                    });
                            }
                        });
                });
        }

        public static Task<TResult> AnalyzeAsync<TResult>(this IRef<Content> contentRef,
            Func<ImageAnalysis, double?, TResult> onAnalyzed,
            Func<TResult> onNotFound = default)
        {
            return AppSettings.CognitiveServices.ComputerVisionSubscriptionKey.ConfigurationString(
                subscriptionKey =>
                {
                    return AppSettings.CognitiveServices.ComputerVisionEndpoint.ConfigurationUri(
                        async endpointUri =>
                        {
                            using (var computerVision = new ComputerVisionClient(
                                new ApiKeyServiceClientCredentials(subscriptionKey),
                                new System.Net.Http.DelegatingHandler[] { }))
                            {
                                return await await contentRef.LoadStreamAsync(
                                    async (imageStream, contentType) =>
                                    {
                                        var widthMultiplier = default(double?);
                                        if (imageStream.Length > 4000000)
                                        {
                                            var image = System.Drawing.Image.FromStream(imageStream);
                                            var newImageStream = new MemoryStream();
                                            widthMultiplier = Math.Sqrt(4000000.0 / imageStream.Length);
                                            image
                                                .ResizeImage(
                                                    (int)(image.Width * widthMultiplier),
                                                    (int)(image.Height * widthMultiplier))
                                                .Save(newImageStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                                            newImageStream.Position = 0;
                                            imageStream = newImageStream;
                                        }
                                        computerVision.Endpoint = endpointUri.OriginalString;
                                        var featuresToSearchFor = VisualFeatureTypes.Categories.AsArray()
                                                .Append(VisualFeatureTypes.Description)
                                                .Append(VisualFeatureTypes.ImageType)
                                                .Append(VisualFeatureTypes.Objects)
                                                .Append(VisualFeatureTypes.Tags)
                                                .Append(VisualFeatureTypes.Brands)
                                                .Append(VisualFeatureTypes.Color)
                                                .Append(VisualFeatureTypes.Faces)
                                                .ToList();
                                        try
                                        {
                                            var analysis = await computerVision.AnalyzeImageInStreamAsync(
                                                imageStream, featuresToSearchFor);
                                            return onAnalyzed(analysis, widthMultiplier);
                                        }
                                        catch(ComputerVisionErrorException ex)
                                        {
                                            throw ex;
                                        }
                                    },
                                    () =>
                                    {
                                        if(onNotFound.IsDefaultOrNull())
                                            throw new ResourceNotFoundException();
                                        return onNotFound().AsTask();
                                    });
                            }
                        });
                });
        }
    }
}

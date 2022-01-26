using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using SixLabors.ImageSharp;

using EastFive;
using EastFive.Extensions;
using EastFive.Images;
using EastFive.Azure.Persistence.Blobs;

namespace EastFive.Azure.Media
{
	public static class ImageExtensions
	{
        public static Task<IBlobRef> SaveImageAsync<TResource>(this Image image,
            Expression<Func<TResource, IBlobRef>> propertyExpr,
            string encoding = default, long? quality = default, string fileName = default)
        {
            return propertyExpr.CreateBlobRefFromStreamAsync(
                async stream =>
                {
                    var imageFormat = quality.HasValue ?
                        await image.SaveAsync(stream, encodingMimeType: encoding, encoderQuality: quality.Value)
                        :
                        await image.SaveAsync(stream, encodingMimeType: encoding);
                },
                contentType: encoding,
                fileName: fileName);

            //var measureSaveContent = profile.Start($"Saving {encoding}");
            //var measureByteGeneration = profile.Start($"Generating Bytes For {encoding}");
            //var (imageBytes, imageFormat) = quality.HasValue ?
            //    await image.GetBytesAsync(encodingMimeType: encoding, encoderQuality: quality.Value)
            //    :
            //    await image.GetBytesAsync(encodingMimeType: encoding);
            //measureByteGeneration.End();
            //var measureByteStoring = profile.Start($"Storing Bytes For {encoding}");
            //var blobRef = await imageBytes
            //    .CreateBlobRefAsync(
            //        propertyExpr,
            //        contentType: encoding,
            //        fileName: disposition.DispositionType);
            //measureByteStoring.End();
            //measureSaveContent.End();
            //return blobRef;
        }

    }
}


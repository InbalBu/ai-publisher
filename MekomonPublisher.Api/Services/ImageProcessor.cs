using MekomonPublisher.Api.Config;
using MekomonPublisher.Api.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace MekomonPublisher.Api.Services;

public sealed record ProcessedImage(byte[] Bytes, int Width, int Height);

/// <summary>
/// Re-encodes every uploaded image before it reaches WordPress: auto-oriented,
/// stripped of metadata, JPEG at quality 82 (matching the quality WordPress's
/// own resizer already uses on this site, per the format analysis). No original
/// bytes are ever forwarded to the CMS.
/// </summary>
public sealed class ImageProcessor
{
    /// <summary>
    /// Cropped to 1200x800 for use as the WordPress featured image. When
    /// <paramref name="faceRegion"/> is given, the crop is centered on the
    /// faces' bounding box instead of the image center, so a subject standing
    /// off to one side does not get trimmed out of frame; falls back to a
    /// plain center crop when no face region is known (detection failed, or
    /// the photo has no faces).
    /// </summary>
    public ProcessedImage ProcessFeatured(byte[] input, FaceDetectionResult? faceRegion = null)
    {
        using Image image = Image.Load(input);
        image.Mutate(x => x.AutoOrient());

        Rectangle cropRect = ComputeFeaturedCropRect(image.Width, image.Height, faceRegion);
        image.Mutate(x => x
            .Crop(cropRect)
            .Resize(ArticleFormat.FeaturedWidth, ArticleFormat.FeaturedHeight));

        return Encode(image, ArticleFormat.FeaturedMaxBytes);
    }

    /// <summary>
    /// The largest FeaturedWidth:FeaturedHeight-aspect rectangle that fits
    /// inside the source image, positioned to center on the face region when
    /// one is given (clamped to stay within the source bounds). If the face
    /// span is wider or taller than the crop itself, it cannot be fully
    /// contained by cropping alone; centering on it still splits the overflow
    /// evenly on both sides rather than trimming from just one.
    /// </summary>
    private static Rectangle ComputeFeaturedCropRect(int sourceWidth, int sourceHeight, FaceDetectionResult? face)
    {
        double targetAspect = (double)ArticleFormat.FeaturedWidth / ArticleFormat.FeaturedHeight;
        double sourceAspect = (double)sourceWidth / sourceHeight;

        int cropWidth, cropHeight;
        if (sourceAspect > targetAspect)
        {
            cropHeight = sourceHeight;
            cropWidth = (int)Math.Round(sourceHeight * targetAspect);
        }
        else
        {
            cropWidth = sourceWidth;
            cropHeight = (int)Math.Round(sourceWidth / targetAspect);
        }

        int offsetX = (sourceWidth - cropWidth) / 2;
        int offsetY = (sourceHeight - cropHeight) / 2;

        if (face is not null)
        {
            offsetX = CenteredOffset(face.XMin * sourceWidth, face.XMax * sourceWidth, cropWidth, sourceWidth);
            offsetY = CenteredOffset(face.YMin * sourceHeight, face.YMax * sourceHeight, cropHeight, sourceHeight);
        }

        return new Rectangle(offsetX, offsetY, cropWidth, cropHeight);
    }

    private static int CenteredOffset(double rangeMin, double rangeMax, int cropLength, int sourceLength)
    {
        double rangeCenter = (rangeMin + rangeMax) / 2;
        int offset = (int)Math.Round(rangeCenter - cropLength / 2.0);
        return Math.Clamp(offset, 0, Math.Max(0, sourceLength - cropLength));
    }

    /// <summary>
    /// Fit within a 1200x900 box, aspect preserved, never upscaled. Bounding
    /// both dimensions (not just the long side) matters for portrait shots:
    /// capping only the long side still let a tall phone photo keep its full
    /// ~1536px width, so it rendered far taller than any landscape image on
    /// the site and dominated the article ("smeared" down the page).
    /// </summary>
    public ProcessedImage ProcessBody(byte[] input)
    {
        using Image image = Image.Load(input);
        image.Mutate(x => x.AutoOrient());

        if (image.Width > ArticleFormat.BodyImageMaxWidth || image.Height > ArticleFormat.BodyImageMaxHeight)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(ArticleFormat.BodyImageMaxWidth, ArticleFormat.BodyImageMaxHeight),
                Mode = ResizeMode.Max,
            }));
        }

        return Encode(image, ArticleFormat.BodyImageMaxBytes);
    }

    private static ProcessedImage Encode(Image image, int maxBytes)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        int quality = ArticleFormat.JpegQuality;
        byte[] bytes;

        while (true)
        {
            using var stream = new MemoryStream();
            image.Save(stream, new JpegEncoder { Quality = quality });
            bytes = stream.ToArray();

            if (bytes.Length <= maxBytes || quality <= 70)
            {
                break;
            }

            quality -= 4;
        }

        return new ProcessedImage(bytes, image.Width, image.Height);
    }
}

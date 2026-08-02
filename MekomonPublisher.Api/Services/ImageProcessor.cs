using MekomonPublisher.Api.Config;
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
    /// <summary>Center-cropped to 1200x800 for use as the WordPress featured image.</summary>
    public ProcessedImage ProcessFeatured(byte[] input)
    {
        using Image image = Image.Load(input);
        image.Mutate(x => x
            .AutoOrient()
            .Resize(new ResizeOptions
            {
                Size = new Size(ArticleFormat.FeaturedWidth, ArticleFormat.FeaturedHeight),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
            }));

        return Encode(image, ArticleFormat.FeaturedMaxBytes);
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

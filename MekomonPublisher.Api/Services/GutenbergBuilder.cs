using System.Net;
using System.Text;
using MekomonPublisher.Api.Models;

namespace MekomonPublisher.Api.Services;

/// <summary>
/// Assembles the final WordPress block markup. This exists as plain code, not
/// as something Gemini generates directly, because the &lt;figure&gt; blocks
/// need real WordPress attachment IDs baked into the class attribute
/// (<c>wp-image-{id}</c>), and those IDs do not exist until after upload.
/// Layout follows the site's own observed pattern: an opening empty paragraph,
/// a lead image, the deck as an H2, a second image, then body content with
/// any remaining images placed where Gemini said to put them.
/// </summary>
public static class GutenbergBuilder
{
    public sealed record ImageInfo(int MediaId, string Url, int Width, int Height, string AltText, string? Caption);

    public static string Build(GeneratedArticle article, IReadOnlyList<ImageInfo> images)
    {
        var sb = new StringBuilder();

        sb.Append("<p class=\"wp-block-paragraph\"></p>");

        if (images.Count > 0)
        {
            AppendImage(sb, images[0]);
        }

        sb.Append("<h2 class=\"wp-block-heading\">")
          .Append(HtmlSafety.Clean(article.Subtitle))
          .Append("</h2>");

        if (images.Count > 1)
        {
            AppendImage(sb, images[1]);
        }

        // Only images beyond the fixed lead/second slots are placed by Gemini.
        ILookup<int, ImagePlacement> roamingByBlockIndex = article.ImagePlacements
            .Where(p => p.ImageIndex >= 2 && p.ImageIndex < images.Count)
            .ToLookup(p => Math.Clamp(p.AfterBlockIndex, 0, article.Blocks.Count));

        for (var blockIndex = 0; blockIndex < article.Blocks.Count; blockIndex++)
        {
            AppendBlock(sb, article.Blocks[blockIndex]);

            foreach (ImagePlacement placement in roamingByBlockIndex[blockIndex])
            {
                AppendImage(sb, images[placement.ImageIndex]);
            }
        }

        // Placements that ask to land after the very last block.
        foreach (ImagePlacement placement in roamingByBlockIndex[article.Blocks.Count])
        {
            AppendImage(sb, images[placement.ImageIndex]);
        }

        sb.Append("<p class=\"wp-block-paragraph\"></p>");
        sb.Append("<p class=\"wp-block-paragraph\"></p>");

        return sb.ToString();
    }

    private static void AppendBlock(StringBuilder sb, ArticleBlock block)
    {
        switch (block.Type)
        {
            case "heading3":
                sb.Append("<h3 class=\"wp-block-heading\">")
                  .Append(HtmlSafety.Clean(block.Html))
                  .Append("</h3>");
                break;

            case "list":
                sb.Append("<ul class=\"wp-block-list\">");
                foreach (string item in block.Items ?? [])
                {
                    sb.Append("<li>").Append(HtmlSafety.Clean(item)).Append("</li>");
                }
                sb.Append("</ul>");
                break;

            default: // paragraph
                sb.Append("<p class=\"wp-block-paragraph\">")
                  .Append(HtmlSafety.Clean(block.Html))
                  .Append("</p>");
                break;
        }
    }

    private static void AppendImage(StringBuilder sb, ImageInfo image)
    {
        sb.Append("<figure class=\"wp-block-image size-large\"><img src=\"")
          .Append(WebUtility.HtmlEncode(image.Url))
          .Append("\" alt=\"")
          .Append(WebUtility.HtmlEncode(image.AltText))
          .Append("\" class=\"wp-image-")
          .Append(image.MediaId)
          .Append("\" width=\"")
          .Append(image.Width)
          .Append("\" height=\"")
          .Append(image.Height)
          .Append("\" />");

        if (!string.IsNullOrWhiteSpace(image.Caption))
        {
            sb.Append("<figcaption class=\"wp-element-caption\">")
              .Append(WebUtility.HtmlEncode(image.Caption))
              .Append("</figcaption>");
        }

        sb.Append("</figure>");
    }
}

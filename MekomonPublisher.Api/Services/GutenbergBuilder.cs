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
/// remaining images placed where Gemini said to. Two images are never left
/// adjacent with nothing between them: roaming images are first spread across
/// distinct real body blocks (so genuine article text separates them), and a
/// normalization pass guarantees the invariant even if there are more images
/// than there is text to spread them across.
/// </summary>
public static class GutenbergBuilder
{
    public sealed record ImageInfo(int MediaId, string Url, int Width, int Height, string AltText, string? Caption);

    private abstract record SequenceItem;
    private sealed record TextItem(string Html) : SequenceItem;
    private sealed record ImageItem(ImageInfo Image) : SequenceItem;

    public static string Build(GeneratedArticle article, IReadOnlyList<ImageInfo> images)
    {
        List<SequenceItem> items = [new TextItem("<p class=\"wp-block-paragraph\"></p>")];

        if (images.Count > 0)
        {
            items.Add(new ImageItem(images[0]));
        }

        items.Add(new TextItem(
            $"<h2 class=\"wp-block-heading\">{HtmlSafety.Clean(article.Subtitle)}</h2>"));

        if (images.Count > 1)
        {
            items.Add(new ImageItem(images[1]));
        }

        Dictionary<int, List<int>> imagesBySlot = AssignRoamingSlots(article, images.Count);

        for (var blockIndex = 0; blockIndex < article.Blocks.Count; blockIndex++)
        {
            items.Add(new TextItem(RenderBlockHtml(article.Blocks[blockIndex])));

            if (imagesBySlot.TryGetValue(blockIndex, out List<int>? atThisSlot))
            {
                items.AddRange(atThisSlot.Select(imageIndex => new ImageItem(images[imageIndex])));
            }
        }

        if (imagesBySlot.TryGetValue(article.Blocks.Count, out List<int>? atEnd))
        {
            items.AddRange(atEnd.Select(imageIndex => new ImageItem(images[imageIndex])));
        }

        items.Add(new TextItem("<p class=\"wp-block-paragraph\"></p>"));
        items.Add(new TextItem("<p class=\"wp-block-paragraph\"></p>"));

        return Render(NormalizeNoAdjacentImages(items));
    }

    /// <summary>
    /// Assigns each roaming image (index 2 or higher) its own slot among
    /// 0..Blocks.Count, so a real body block sits between any two of them
    /// instead of trusting Gemini's raw positions, which can collide. Starts
    /// from the slot Gemini requested and nudges forward to the next free one
    /// on collision. Only when there are more roaming images than slots does
    /// more than one image land in the same slot - the caller's normalization
    /// pass covers that remaining case.
    /// </summary>
    private static Dictionary<int, List<int>> AssignRoamingSlots(GeneratedArticle article, int imageCount)
    {
        Dictionary<int, int> requestedSlotByImageIndex = article.ImagePlacements
            .Where(p => p.ImageIndex >= 2 && p.ImageIndex < imageCount)
            .GroupBy(p => p.ImageIndex)
            .ToDictionary(g => g.Key, g => Math.Clamp(g.First().AfterBlockIndex, 0, article.Blocks.Count));

        var used = new HashSet<int>();
        var result = new Dictionary<int, List<int>>();

        foreach ((int imageIndex, int requestedSlot) in requestedSlotByImageIndex.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key))
        {
            int slot = requestedSlot;
            while (used.Contains(slot) && slot < article.Blocks.Count)
            {
                slot++;
            }
            used.Add(slot);

            if (!result.TryGetValue(slot, out List<int>? list))
            {
                list = [];
                result[slot] = list;
            }
            list.Add(imageIndex);
        }

        return result;
    }

    /// <summary>The one guaranteed backstop: whatever produced the sequence, no two ImageItems ever end up adjacent.</summary>
    private static List<SequenceItem> NormalizeNoAdjacentImages(List<SequenceItem> items)
    {
        var normalized = new List<SequenceItem>(items.Count);

        foreach (SequenceItem item in items)
        {
            if (item is ImageItem && normalized.Count > 0 && normalized[^1] is ImageItem)
            {
                normalized.Add(new TextItem("<p class=\"wp-block-paragraph\"></p>"));
            }

            normalized.Add(item);
        }

        return normalized;
    }

    private static string Render(List<SequenceItem> items)
    {
        var sb = new StringBuilder();

        foreach (SequenceItem item in items)
        {
            switch (item)
            {
                case TextItem text:
                    sb.Append(text.Html);
                    break;
                case ImageItem image:
                    AppendImage(sb, image.Image);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string RenderBlockHtml(ArticleBlock block)
    {
        var sb = new StringBuilder();
        AppendBlock(sb, block);
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

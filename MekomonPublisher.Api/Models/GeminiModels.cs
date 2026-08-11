using System.Text.Json.Serialization;

namespace MekomonPublisher.Api.Models;

/// <summary>
/// The structured article Gemini returns. Deliberately not HTML: attachment IDs
/// do not exist until after upload, and Gutenberg's block-comment markup is
/// assembled by <see cref="Services.GutenbergBuilder"/>, not inferred by the model.
/// </summary>
public sealed class GeneratedArticle
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>The H2 deck, 106-350 chars per the site's own article format.</summary>
    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = "";

    [JsonPropertyName("blocks")]
    public List<ArticleBlock> Blocks { get; set; } = [];

    [JsonPropertyName("imagePlacements")]
    public List<ImagePlacement> ImagePlacements { get; set; } = [];

    [JsonPropertyName("focusKeyword")]
    public string FocusKeyword { get; set; } = "";

    [JsonPropertyName("seoTitle")]
    public string SeoTitle { get; set; } = "";

    [JsonPropertyName("metaDescription")]
    public string MetaDescription { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

public sealed class ArticleBlock
{
    /// <summary>"heading3", "paragraph", or "list".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>Inner HTML for heading3/paragraph. May contain &lt;strong&gt;, &lt;em&gt;, &lt;a&gt;.</summary>
    [JsonPropertyName("html")]
    public string? Html { get; set; }

    /// <summary>List item texts, used only when Type == "list".</summary>
    [JsonPropertyName("items")]
    public List<string>? Items { get; set; }
}

public sealed class ImagePlacement
{
    /// <summary>Index into the operator's uploaded images, in upload order.</summary>
    [JsonPropertyName("imageIndex")]
    public int ImageIndex { get; set; }

    /// <summary>Insert after this many blocks (0 = right after the deck, before any block).</summary>
    [JsonPropertyName("afterBlockIndex")]
    public int AfterBlockIndex { get; set; }

    [JsonPropertyName("altText")]
    public string AltText { get; set; } = "";
}

/// <summary>
/// The union bounding box of every human face Gemini found in the featured
/// image, normalized to the 0..1 range (0,0 top-left, 1,1 bottom-right).
/// Used to steer <see cref="Services.ImageProcessor.ProcessFeatured"/>'s crop
/// so it never trims into a face; not persisted or sent to WordPress.
/// </summary>
public sealed class FaceDetectionResult
{
    [JsonPropertyName("hasFaces")]
    public bool HasFaces { get; set; }

    [JsonPropertyName("xMin")]
    public double XMin { get; set; }

    [JsonPropertyName("yMin")]
    public double YMin { get; set; }

    [JsonPropertyName("xMax")]
    public double XMax { get; set; }

    [JsonPropertyName("yMax")]
    public double YMax { get; set; }
}

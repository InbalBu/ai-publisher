namespace MekomonPublisher.Api.Models;

/// <summary>The Yoast fields Gemini writes for an article, in both AI and manual mode.</summary>
public sealed class SeoFields
{
    public string FocusKeyword { get; set; } = "";

    public string SeoTitle { get; set; } = "";

    public string MetaDescription { get; set; } = "";
}

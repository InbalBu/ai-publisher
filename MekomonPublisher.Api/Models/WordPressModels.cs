using System.Text.Json.Serialization;

namespace MekomonPublisher.Api.Models;

/// <summary>Wire shapes for the WordPress REST API. Only the fields we actually read or write.</summary>
public sealed class WpTerm
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";
}

public sealed class WpMedia
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = "";
}

public sealed class WpPost
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

/// <summary>Body for POST /wp/v2/posts. Yoast keys require the wp-bridge mu-plugin, see /wp-bridge.</summary>
public sealed class CreatePostBody
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("categories")]
    public int[] Categories { get; set; } = [];

    [JsonPropertyName("tags")]
    public int[] Tags { get; set; } = [];

    [JsonPropertyName("featured_media")]
    public int FeaturedMedia { get; set; }

    [JsonPropertyName("meta")]
    public Dictionary<string, string> Meta { get; set; } = [];
}

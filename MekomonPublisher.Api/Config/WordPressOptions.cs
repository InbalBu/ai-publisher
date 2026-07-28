namespace MekomonPublisher.Api.Config;

public sealed class WordPressOptions
{
    public const string SectionName = "WordPress";

    public string BaseUrl { get; set; } = "https://mekomonrishon.co.il";

    /// <summary>The dedicated ai-publisher user, Editor role. See README.</summary>
    public string Username { get; set; } = "";

    /// <summary>WordPress Application Password, set via user-secrets. Never committed.</summary>
    public string ApplicationPassword { get; set; } = "";

    public int RequestTimeoutSeconds { get; set; } = 15;

    public int MediaUploadTimeoutSeconds { get; set; } = 30;
}

namespace MekomonPublisher.Api.Config;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Set via user-secrets or environment variable, never committed.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Set this to whatever the current Gemini model id is at the time you deploy.
    /// Left blank on purpose: model names change and a wrong guess baked into
    /// source is worse than an obvious empty-string failure at startup.
    /// </summary>
    public string Model { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 75;

    public double Temperature { get; set; } = 0.4;
}

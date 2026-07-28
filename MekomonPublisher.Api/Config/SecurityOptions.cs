namespace MekomonPublisher.Api.Config;

/// <summary>
/// One shared operator credential - this app has one login shared between
/// whoever publishes articles (you, and now the WordPress manager you're
/// sharing the URL with), not a multi-user account system.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string OperatorUsername { get; set; } = "";

    /// <summary>
    /// Produced by <c>dotnet run -- hash-password &lt;password&gt;</c>, never the
    /// plain password itself. Set via user-secrets locally, an environment
    /// variable in production.
    /// </summary>
    public string OperatorPasswordHash { get; set; } = "";
}

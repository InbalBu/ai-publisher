using System.Net;

namespace MekomonPublisher.Api.Services;

/// <summary>Gemini answered with a non-success HTTP status. Mapped to a customer message by <see cref="PublishErrorMapper"/>.</summary>
public sealed class GeminiApiException(HttpStatusCode status, string body)
    : Exception($"Gemini API returned {(int)status}: {body}")
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>WordPress answered with a non-success HTTP status for one step of the publish.</summary>
public sealed class WordPressApiException(string action, HttpStatusCode status, string body)
    : Exception($"WordPress failed to {action}: {(int)status} {body}")
{
    public HttpStatusCode Status { get; } = status;
}

namespace MekomonPublisher.Api.Data;

/// <summary>
/// One row per publish attempt, success or failure. This is a plain log, not a
/// job queue: publishing happens synchronously in a single request, so there is
/// no in-flight state to track, only what happened afterwards.
/// </summary>
public sealed class PublishHistoryEntry
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool Success { get; set; }

    public string? Title { get; set; }

    public string? Url { get; set; }

    public string? Status { get; set; }

    public long ElapsedMs { get; set; }

    public string? Error { get; set; }
}

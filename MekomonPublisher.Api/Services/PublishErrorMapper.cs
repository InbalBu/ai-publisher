namespace MekomonPublisher.Api.Services;

/// <summary>
/// Turns any exception from a publish into a <see cref="PublishFailure"/> with
/// the customer message for what actually went wrong. Anything not recognized
/// falls back to <see cref="PublishCodes.Unknown"/>, with the full exception kept
/// as the detail for the log.
/// </summary>
public static class PublishErrorMapper
{
    /// <param name="requestCt">The request's own token. A cancel that comes from it means the browser went away, not a server fault.</param>
    public static PublishFailure Map(Exception ex, CancellationToken requestCt)
    {
        if (ex is PublishFailure failure)
        {
            return failure;
        }

        if (ex is GeminiApiException gemini)
        {
            return MapGemini(gemini);
        }

        if (ex is WordPressApiException wordPress)
        {
            return MapWordPress(wordPress);
        }

        if (ex is OperationCanceledException)
        {
            // A cancel from our own timeouts is a slow service. A cancel from the request is the client leaving.
            return requestCt.IsCancellationRequested
                ? new PublishFailure(PublishCodes.ClientClosed, ex.Message)
                : new PublishFailure(PublishCodes.Timeout, ex.Message);
        }

        if (ex is HttpRequestException)
        {
            return new PublishFailure(PublishCodes.Network, ex.ToString());
        }

        return new PublishFailure(PublishCodes.Unknown, ex.ToString());
    }

    private static PublishFailure MapGemini(GeminiApiException ex)
    {
        int status = (int)ex.Status;
        string code = status switch
        {
            429 => PublishCodes.GeminiBusy,
            401 or 403 => PublishCodes.GeminiAuth,
            400 => PublishCodes.GeminiRejected,
            >= 500 => PublishCodes.GeminiDown,
            _ => PublishCodes.Unknown,
        };

        return new PublishFailure(code, ex.Message);
    }

    private static PublishFailure MapWordPress(WordPressApiException ex)
    {
        int status = (int)ex.Status;
        string code = status switch
        {
            401 or 403 => PublishCodes.WpAuth,
            404 => PublishCodes.WpNotFound,
            413 => PublishCodes.WpFileTooLarge,
            400 => PublishCodes.WpRejected,
            429 => PublishCodes.WpBusy,
            >= 500 => PublishCodes.WpDown,
            _ => PublishCodes.Unknown,
        };

        return new PublishFailure(code, ex.Message);
    }
}

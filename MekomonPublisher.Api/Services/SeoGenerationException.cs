namespace MekomonPublisher.Api.Services;

/// <summary>
/// The Yoast fields (keyphrase, SEO title, meta description) could not be
/// generated in a usable form. Its message is shown to the operator as-is, so
/// it is written in Hebrew and names the step that failed. The technical
/// detail is kept in <see cref="Detail"/> for the log, not shown to anyone.
/// </summary>
public sealed class SeoGenerationException(string detail) : InvalidOperationException(CustomerMessage)
{
    public const string CustomerMessage =
        "לא הצלחנו ליצור את שדות ה-SEO לכתבה (ביטוי מפתח, כותרת SEO ותיאור מטא), ולכן הכתבה לא פורסמה. נסו לפרסם שוב.";

    public string Detail { get; } = detail;
}

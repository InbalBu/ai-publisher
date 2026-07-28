using System.Text.RegularExpressions;

namespace MekomonPublisher.Api.Services;

/// <summary>
/// Strips the handful of tags and attributes that would turn a generated
/// article into a stored XSS vector on a live public site. This is not a
/// general-purpose HTML sanitizer: Gemini's output is inner HTML for a
/// paragraph or heading (strong, em, a, br), and that is the only shape it is
/// ever supposed to take. Anything outside that shape is stripped, not escaped,
/// so the visible article still reads correctly.
/// </summary>
public static partial class HtmlSafety
{
    public static string Clean(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        string result = ScriptTag().Replace(html, "");
        result = StyleTag().Replace(result, "");
        result = DangerousTag().Replace(result, "");
        result = EventAttribute().Replace(result, "");
        result = JavascriptHref().Replace(result, "href=\"#\"");

        return result;
    }

    [GeneratedRegex(@"<script\b[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"<style\b[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleTag();

    [GeneratedRegex(@"</?(?:iframe|object|embed|form|input|svg)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DangerousTag();

    [GeneratedRegex(@"\son\w+\s*=\s*(""[^""]*""|'[^']*'|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventAttribute();

    [GeneratedRegex(@"href\s*=\s*(""|')javascript:[^""']*\1", RegexOptions.IgnoreCase)]
    private static partial Regex JavascriptHref();
}

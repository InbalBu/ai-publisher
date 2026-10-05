using System.Text;
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

    /// <summary>
    /// Turns any bare http(s) URL left in plain text into a real clickable
    /// link. Does not depend on Gemini choosing to wrap a URL in an anchor
    /// itself, which was never guaranteed: the prompt only said anchors are an
    /// allowed tag. Opens in a new tab since the target is always another site.
    /// Skips text already inside an anchor so an existing link's visible URL is
    /// never double-wrapped.
    /// </summary>
    public static string AutoLinkUrls(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        string[] parts = TagPattern().Split(html);
        var sb = new StringBuilder(html.Length + 32);
        var insideAnchor = false;

        for (var i = 0; i < parts.Length; i++)
        {
            // Split keeps the captured tags at odd indices; even indices are text between tags.
            if (i % 2 == 1)
            {
                if (parts[i].StartsWith("<a", StringComparison.OrdinalIgnoreCase))
                {
                    insideAnchor = true;
                }
                else if (parts[i].Equals("</a>", StringComparison.OrdinalIgnoreCase))
                {
                    insideAnchor = false;
                }

                sb.Append(parts[i]);
            }
            else
            {
                sb.Append(insideAnchor ? parts[i] : BareUrlPattern().Replace(parts[i], LinkifyMatch));
            }
        }

        return sb.ToString();
    }

    // Trailing punctuation is almost always sentence punctuation, not part of the URL.
    // Trim it off the link and append it after, so "...https://example.com." keeps the period out of the href.
    private static string LinkifyMatch(Match match)
    {
        string url = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '"', '\'');
        string trailing = match.Value[url.Length..];
        return $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{url}</a>{trailing}";
    }

    [GeneratedRegex(@"https?://[^\s<>""']+")]
    private static partial Regex BareUrlPattern();

    [GeneratedRegex(@"(<[^>]+>)")]
    private static partial Regex TagPattern();

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

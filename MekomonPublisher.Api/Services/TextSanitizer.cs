using System.Text;
using MekomonPublisher.Api.Models;

namespace MekomonPublisher.Api.Services;

/// <summary>
/// The site's articles are strictly Hebrew or English. Gemini has been
/// observed slipping a single stray character from an unrelated script
/// (seen: one Arabic character) into otherwise clean output. Rather than
/// trust the prompt alone to prevent this, every string Gemini returns is
/// swept here before it reaches WordPress, dropping any character that
/// isn't ASCII (English letters, digits, punctuation, whitespace - also
/// needed for Hebrew text) or in the Hebrew Unicode block.
/// </summary>
public static class TextSanitizer
{
    public static void Clean(GeneratedArticle article)
    {
        article.Title = Clean(article.Title);
        article.Subtitle = Clean(article.Subtitle);
        article.FocusKeyword = Clean(article.FocusKeyword);
        article.SeoTitle = Clean(article.SeoTitle);
        article.MetaDescription = Clean(article.MetaDescription);

        foreach (ArticleBlock block in article.Blocks)
        {
            block.Html = Clean(block.Html);
            if (block.Items is not null)
            {
                for (var i = 0; i < block.Items.Count; i++)
                {
                    block.Items[i] = Clean(block.Items[i]);
                }
            }
        }

        foreach (ImagePlacement placement in article.ImagePlacements)
        {
            placement.AltText = Clean(placement.AltText);
        }

        for (var i = 0; i < article.Tags.Count; i++)
        {
            article.Tags[i] = Clean(article.Tags[i]);
        }
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
    private static string? Clean(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c <= 0x007F || (c >= 0x0590 && c <= 0x05FF))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

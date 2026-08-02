namespace MekomonPublisher.Api.Config;

/// <summary>
/// Fixed facts about mekomonrishon.co.il's article format, taken from the site
/// analysis. These are real site data, not tunables, so they are plain
/// constants rather than configuration.
/// </summary>
public static class ArticleFormat
{
    /// <summary>The site's 15 top-level categories: id -> Hebrew name.</summary>
    public static readonly IReadOnlyDictionary<int, string> Categories = new Dictionary<int, string>
    {
        [27] = "חדשות",
        [47] = "אנשים",
        [1] = "כתבות מקומון ראשון",
        [44] = "מבלים בראשון",
        [46] = "רכילות",
        [42] = "תרבות ובידור",
        [43] = "ספורט",
        [45] = "עסקים בראשון",
        [4264] = "כושר ובריאות",
        [5220] = "צרכנות",
        [49] = "נדלן",
        [48] = "מתכון מנצח",
        [5221] = "חינוך",
        [3896] = "טיפ שווה זהב",
        [4065] = "שפת המוזיקה",
    };

    /// <summary>Tag IDs attached to every article: branding boilerplate.</summary>
    public static readonly int[] BoilerplateTagIds = [57, 73, 50, 62, 91, 88, 553];

    public const int SeoTitleMaxChars = 66;
    public const int MetaDescriptionMinChars = 120;
    public const int MetaDescriptionMaxChars = 155;
    public const int DeckMinChars = 106;
    public const int DeckMaxChars = 350;
    public const int SlugMaxEncodedChars = 200;

    public const int FeaturedWidth = 1200;
    public const int FeaturedHeight = 800;
    public const int FeaturedMaxBytes = 150_000;
    public const int BodyImageMaxWidth = 1200;
    public const int BodyImageMaxHeight = 900;
    public const int BodyImageMaxBytes = 500_000;
    public const int JpegQuality = 82;
}

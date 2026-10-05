namespace MekomonPublisher.Api.Services;

/// <summary>
/// A failed publish with a known cause. <see cref="Message"/> is shown to the
/// operator: it says what happened and what to do next, in Hebrew. <see cref="Code"/>
/// is shown next to it so support can find the case. <see cref="Detail"/> is for the log only.
/// </summary>
public sealed class PublishFailure(string code, string detail, string? customerMessage = null)
    : Exception(customerMessage ?? PublishMessages.For(code))
{
    public string Code { get; } = code;

    public string Detail { get; } = detail;
}

public static class PublishCodes
{
    // The operator's input.
    public const string TextShort = "TEXT_SHORT";
    public const string TextLong = "TEXT_LONG";
    public const string CategoryInvalid = "CATEGORY_INVALID";
    public const string StatusInvalid = "STATUS_INVALID";
    public const string NoImages = "NO_IMAGES";
    public const string FeaturedInvalid = "FEATURED_INVALID";
    public const string ManualFieldsMissing = "MANUAL_FIELDS_MISSING";
    public const string BadForm = "BAD_FORM";
    public const string ImageUnreadable = "IMAGE_UNREADABLE";

    // The session and the connection.
    public const string SessionExpired = "SESSION_EXPIRED";
    public const string RateLimited = "RATE_LIMITED";
    public const string RequestTooLarge = "REQUEST_TOO_LARGE";
    public const string Network = "NETWORK";
    public const string ClientClosed = "CLIENT_CLOSED";
    public const string ServerUnavailable = "SERVER_UNAVAILABLE";
    public const string Timeout = "TIMEOUT";

    // The AI service.
    public const string GeminiConfig = "GEMINI_CONFIG";
    public const string GeminiAuth = "GEMINI_AUTH";
    public const string GeminiBusy = "GEMINI_BUSY";
    public const string GeminiDown = "GEMINI_DOWN";
    public const string GeminiRejected = "GEMINI_REJECTED";
    public const string GeminiEmpty = "GEMINI_EMPTY";
    public const string GeminiTimeout = "GEMINI_TIMEOUT";
    public const string ArticleInvalid = "ARTICLE_INVALID";
    public const string SeoFailed = "SEO_FAILED";

    // The WordPress site.
    public const string WpAuth = "WP_AUTH";
    public const string WpNotFound = "WP_NOT_FOUND";
    public const string WpFileTooLarge = "WP_FILE_TOO_LARGE";
    public const string WpRejected = "WP_REJECTED";
    public const string WpBusy = "WP_BUSY";
    public const string WpDown = "WP_DOWN";

    public const string Unknown = "UNKNOWN";
}

/// <summary>
/// The customer-facing text for each <see cref="PublishCodes"/> value. Every
/// message names the cause and the next step, so an operator who sees it does
/// not need to ask why the article failed.
/// </summary>
public static class PublishMessages
{
    public static string For(string code) => code switch
    {
        PublishCodes.TextShort => "הטקסט קצר מדי. יש להזין לפחות 50 תווים כדי לפרסם כתבה.",
        PublishCodes.TextLong => "הטקסט ארוך מדי. קצרו אותו ונסו שוב.",
        PublishCodes.CategoryInvalid => "לא נבחרה קטגוריה תקינה. בחרו קטגוריה מהרשימה ונסו שוב.",
        PublishCodes.StatusInvalid => "סוג הפרסום שנבחר אינו תקין. רעננו את הדף ונסו שוב.",
        PublishCodes.NoImages => "לא הועלתה אף תמונה. העלו לפחות תמונה אחת ונסו שוב.",
        PublishCodes.FeaturedInvalid => "התמונה הראשית שנבחרה אינה תקינה. בחרו תמונה ראשית מחדש ונסו שוב.",
        PublishCodes.ManualFieldsMissing => "במצב בלי AI חובה למלא כותרת ראשית וכותרת משנה.",
        PublishCodes.BadForm => "הטופס לא נשלח כראוי. רעננו את הדף ונסו שוב.",
        PublishCodes.ImageUnreadable => "אחת התמונות לא נקראה כראוי. ודאו שהקובץ הוא תמונה תקינה (JPG או PNG) ונסו שוב.",

        PublishCodes.SessionExpired => "פג תוקף ההתחברות. התחברו מחדש ונסו שוב. הטופס נשמר.",
        PublishCodes.RateLimited => "נשלחו יותר מדי בקשות לפרסום בזמן קצר. המתינו כמה דקות ונסו שוב.",
        PublishCodes.RequestTooLarge => "הקבצים שנשלחו גדולים מדי. הקטינו את התמונות או העלו פחות תמונות ונסו שוב.",
        PublishCodes.Network => "בעיית תקשורת עם השרת. בדקו את החיבור לאינטרנט ונסו שוב.",
        PublishCodes.ClientClosed => "החיבור נותק לפני שהפרסום הסתיים (הדף נסגר או הרשת נפלה). הכתבה לא פורסמה, נסו שוב.",
        PublishCodes.ServerUnavailable => "השרת לא הגיב כרגע (תקלה זמנית). נסו שוב בעוד רגע.",
        PublishCodes.Timeout => "הפרסום לקח יותר מדי זמן ונעצר. נסו שוב. אם זה חוזר, העלו פחות תמונות או קצרו את הטקסט.",

        PublishCodes.GeminiConfig => "שירות ה-AI אינו מוגדר כראוי במערכת. יש לפנות למנהל המערכת.",
        PublishCodes.GeminiAuth => "שירות ה-AI דחה את מפתח הגישה של המערכת. יש לפנות למנהל המערכת.",
        PublishCodes.GeminiBusy => "שירות ה-AI עמוס כרגע ומגביל בקשות. נסו שוב בעוד כמה דקות.",
        PublishCodes.GeminiDown => "שירות ה-AI אינו זמין כרגע (תקלה זמנית אצל הספק). נסו שוב בעוד כמה דקות.",
        PublishCodes.GeminiRejected => "שירות ה-AI לא הצליח לעבד את הטקסט. נסו לנסח אותו מחדש או לקצר אותו, או לפרסם במצב בלי AI.",
        PublishCodes.GeminiEmpty => "שירות ה-AI החזיר תשובה ריקה לטקסט. נסו שוב.",
        PublishCodes.GeminiTimeout => "שירות ה-AI לקח יותר מדי זמן לכתוב את הכתבה. נסו שוב. אם זה חוזר, קצרו את הטקסט.",
        PublishCodes.ArticleInvalid => "ה-AI לא החזיר כתבה תקינה, גם אחרי שני ניסיונות. נסו שוב, לרוב ניסיון נוסף מצליח.",
        PublishCodes.SeoFailed => "לא הצלחנו ליצור את שדות ה-SEO לכתבה (ביטוי מפתח, כותרת SEO ותיאור מטא), ולכן הכתבה לא פורסמה. נסו לפרסם שוב.",

        PublishCodes.WpAuth => "אין לנו הרשאה לפרסם באתר מקומון ראשון. פרטי ההתחברות לאתר כנראה פגי תוקף. יש לפנות למנהל המערכת.",
        PublishCodes.WpNotFound => "כתובת האתר או הנתיב לפרסום לא נמצאו. יש לפנות למנהל המערכת.",
        PublishCodes.WpFileTooLarge => "אחת התמונות גדולה מדי עבור האתר. הקטינו את התמונות ונסו שוב.",
        PublishCodes.WpRejected => "האתר דחה את הכתבה או את התמונות (ייתכן שיש תוכן לא תקין). נסו שוב, ואם זה חוזר פנו לתמיכה עם קוד השגיאה.",
        PublishCodes.WpBusy => "האתר מגביל את כמות הבקשות כרגע. המתינו דקה ונסו שוב.",
        PublishCodes.WpDown => "אתר מקומון ראשון לא הגיב כראוי (תקלה זמנית באתר). נסו שוב בעוד כמה דקות.",

        _ => "משהו לא צפוי קרה בזמן הפרסום. נסו שוב. אם זה חוזר, פנו לתמיכה עם קוד השגיאה.",
    };
}

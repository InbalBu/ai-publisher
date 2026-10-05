using System.Diagnostics;
using MekomonPublisher.Api.Config;
using MekomonPublisher.Api.Data;
using MekomonPublisher.Api.Models;

namespace MekomonPublisher.Api.Services;

/// <summary>
/// Runs one publish attempt end to end, synchronously within a single request.
/// No job queue, no state machine: the whole point is that this finishes in
/// well under a minute, so a plain awaited pipeline with a loading spinner on
/// the frontend is simpler than a background worker and gives nothing up.
/// </summary>
public sealed class ArticlePublisher(
    GeminiService gemini,
    WordPressClient wordPress,
    ImageProcessor images,
    AppDbContext db,
    ILogger<ArticlePublisher> logger)
{
    /// <summary>
    /// Hard cap on raw article text, in characters. Generation time grows with
    /// the length of the text, and a long enough article runs past the Gemini
    /// timeout, so the cap is enforced here and mirrored in the frontend
    /// (web/src/App.tsx MAX_RAW_TEXT_LENGTH) - keep the two values equal.
    /// </summary>
    public const int MaxRawTextLength = 6000;

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var uploadedMediaIds = new List<int>();

        try
        {
            Validate(request);
            string categoryName = ArticleFormat.Categories[request.CategoryId];

            // Started together, not sequentially: image encoding and upload do not
            // need Gemini's output for bytes, only for alt text, which is patched
            // on afterwards. This overlap is most of where the speed comes from.
            // When UseAi is false there is nothing to await concurrently, but the
            // shape stays a Task so every line below - image alt text merging,
            // GutenbergBuilder.Build, tag resolution - is identical either way.
            Task<GeneratedArticle> generationTask = request.UseAi
                ? gemini.GenerateAsync(request.RawText, categoryName, request.Images.Count, ct)
                : Task.FromResult(BuildManualArticle(request));

            // Manual mode publishes the operator's text as typed, but the Yoast
            // fields are still written by AI from that text. Started here, with the
            // uploads, so it overlaps them. In AI mode they come with the article.
            Task<SeoFields>? manualSeoTask = request.UseAi
                ? null
                : gemini.GenerateSeoAsync(request.Title!, request.Subtitle!, request.RawText, categoryName, ct);

            Task<GutenbergBuilder.ImageInfo>[] bodyUploadTasks = request.Images
                .Select(image => UploadBodyImageAsync(image, uploadedMediaIds, ct))
                .ToArray();

            Task<WpMedia> featuredUploadTask = UploadFeaturedImageAsync(
                request.Images[request.FeaturedImageIndex], uploadedMediaIds, ct);

            await Task.WhenAll([.. bodyUploadTasks, featuredUploadTask]);
            GeneratedArticle article = await generationTask;

            if (manualSeoTask is not null)
            {
                SeoFields seo = await manualSeoTask;
                article.FocusKeyword = seo.FocusKeyword;
                article.SeoTitle = seo.SeoTitle;
                article.MetaDescription = seo.MetaDescription;
            }

            GutenbergBuilder.ImageInfo[] bodyImages = bodyUploadTasks.Select(t => t.Result).ToArray();
            await ApplyAltTextAsync(bodyImages, article.ImagePlacements, ct);
            bodyImages = MergeAltText(bodyImages, article.ImagePlacements);

            string content = GutenbergBuilder.Build(article, bodyImages);

            IReadOnlyList<int> topicalTagIds = await wordPress.ResolveOrCreateTagIdsAsync(article.Tags, ct);
            int[] allTagIds = ArticleFormat.BoilerplateTagIds.Concat(topicalTagIds).Distinct().ToArray();

            WpMedia featuredMedia = await featuredUploadTask;

            WpPost post = await wordPress.CreatePostAsync(new CreatePostBody
            {
                Title = HtmlSafety.Clean(article.Title),
                Content = content,
                Status = request.Status,
                Categories = [request.CategoryId],
                Tags = allTagIds,
                FeaturedMedia = featuredMedia.Id,
                Meta = new Dictionary<string, string>
                {
                    ["_yoast_wpseo_title"] = article.SeoTitle,
                    ["_yoast_wpseo_metadesc"] = article.MetaDescription,
                    ["_yoast_wpseo_focuskw"] = article.FocusKeyword,
                    // Pinned explicitly rather than left to Yoast's automatic
                    // featured-image fallback, so the Facebook/WhatsApp share
                    // preview always has an image regardless of that setting.
                    ["_yoast_wpseo_opengraph-image"] = featuredMedia.SourceUrl,
                    ["_yoast_wpseo_opengraph-image-id"] = featuredMedia.Id.ToString(),
                },
            }, ct);

            stopwatch.Stop();

            db.PublishHistory.Add(new PublishHistoryEntry
            {
                CreatedAtUtc = DateTime.UtcNow,
                Success = true,
                Title = article.Title,
                Url = post.Link,
                Status = post.Status,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            });
            await db.SaveChangesAsync(ct);

            return new PublishResult
            {
                Success = true,
                Title = article.Title,
                Url = post.Link,
                Status = post.Status,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            PublishFailure failure = PublishErrorMapper.Map(ex, ct);

            logger.LogError(ex, "Publish failed after {ElapsedMs}ms with {Code}", stopwatch.ElapsedMilliseconds, failure.Code);

            foreach (int mediaId in uploadedMediaIds)
            {
                await wordPress.DeleteMediaAsync(mediaId, CancellationToken.None);
            }

            db.PublishHistory.Add(new PublishHistoryEntry
            {
                CreatedAtUtc = DateTime.UtcNow,
                Success = false,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = $"{failure.Code}: {failure.Detail}",
            });
            await db.SaveChangesAsync(CancellationToken.None);

            return new PublishResult
            {
                Success = false,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = failure.Message,
                Code = failure.Code,
            };
        }
    }

    private static void Validate(PublishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RawText) || request.RawText.Length < 50)
        {
            throw new PublishFailure(PublishCodes.TextShort, $"RawText length {request.RawText.Length}.");
        }

        if (request.RawText.Length > MaxRawTextLength)
        {
            throw new PublishFailure(
                PublishCodes.TextLong,
                $"RawText length {request.RawText.Length}, limit {MaxRawTextLength}.",
                $"הטקסט ארוך מדי: {request.RawText.Length} תווים, המקסימום הוא {MaxRawTextLength:N0}. קצרו את הטקסט ונסו שוב.");
        }

        if (!ArticleFormat.Categories.ContainsKey(request.CategoryId))
        {
            throw new PublishFailure(PublishCodes.CategoryInvalid, $"Unknown category id {request.CategoryId}.");
        }

        if (request.Status is not ("publish" or "draft"))
        {
            throw new PublishFailure(PublishCodes.StatusInvalid, $"Status '{request.Status}'.");
        }

        if (request.Images.Count == 0)
        {
            throw new PublishFailure(PublishCodes.NoImages, "No images in the request.");
        }

        if (request.FeaturedImageIndex < 0 || request.FeaturedImageIndex >= request.Images.Count)
        {
            throw new PublishFailure(PublishCodes.FeaturedInvalid, $"Featured index {request.FeaturedImageIndex} of {request.Images.Count}.");
        }

        if (!request.UseAi &&
            (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Subtitle)))
        {
            throw new PublishFailure(PublishCodes.ManualFieldsMissing, "Title or subtitle blank with AI generation off.");
        }
    }

    /// <summary>
    /// The no-AI equivalent of <see cref="GeminiService.GenerateAsync"/>: builds
    /// the same <see cref="GeneratedArticle"/> shape entirely mechanically, from
    /// what the operator actually typed, so nothing downstream needs to know
    /// which path produced it. Title/subtitle are the operator's own words;
    /// the body is RawText split into paragraphs as-is, with no rewriting; tags
    /// are left empty (nothing infers topical tags without AI - the boilerplate
    /// tags every article gets are added separately, unaffected by this).
    /// </summary>
    private static GeneratedArticle BuildManualArticle(PublishRequest request)
    {
        List<ArticleBlock> blocks = SplitIntoParagraphBlocks(request.RawText);

        var article = new GeneratedArticle
        {
            Title = request.Title!.Trim(),
            Subtitle = request.Subtitle!.Trim(),
            Blocks = blocks,
            ImagePlacements = BuildEvenImagePlacements(request.Images.Count, blocks.Count),
            FocusKeyword = "",
            SeoTitle = request.Title.Trim(),
            MetaDescription = request.Subtitle.Trim(),
            Tags = [],
        };

        // Same Hebrew/ASCII sweep and title length cap the AI path gets -
        // operator-typed text is just as capable of carrying a stray
        // character or an over-length title as generated text is.
        TextSanitizer.Clean(article);
        return article;
    }

    /// <summary>
    /// Splits on blank lines first (the normal case for pasted, already-
    /// paragraphed text); falls back to single line breaks when the raw text
    /// has none, so a wall of single-newline-separated lines still becomes
    /// multiple blocks instead of one giant paragraph.
    /// </summary>
    private static List<ArticleBlock> SplitIntoParagraphBlocks(string rawText)
    {
        string[] paragraphs = System.Text.RegularExpressions.Regex.Split(rawText.Trim(), @"\r?\n\s*\r?\n")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        if (paragraphs.Length <= 1)
        {
            paragraphs = rawText.Split('\n')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
        }

        return paragraphs.Select(p => new ArticleBlock { Type = "paragraph", Html = p }).ToList();
    }

    /// <summary>
    /// Images 0 and 1 are placed automatically as the lead and second image by
    /// <see cref="GutenbergBuilder"/> regardless of AfterBlockIndex, exactly as
    /// in the AI path. Images 2 and up (roaming) are spread evenly across the
    /// body blocks instead of an AI judgment call on where they fit best.
    /// </summary>
    private static List<ImagePlacement> BuildEvenImagePlacements(int imageCount, int blockCount)
    {
        var placements = new List<ImagePlacement>();
        int roamingCount = Math.Max(0, imageCount - 2);

        for (var i = 0; i < imageCount; i++)
        {
            int afterBlockIndex = 0;
            if (i >= 2 && blockCount > 0)
            {
                int roamingIndex = i - 2;
                afterBlockIndex = (int)Math.Round((roamingIndex + 1) * (double)blockCount / (roamingCount + 1));
                afterBlockIndex = Math.Clamp(afterBlockIndex, 0, blockCount);
            }

            placements.Add(new ImagePlacement { ImageIndex = i, AfterBlockIndex = afterBlockIndex, AltText = "" });
        }

        return placements;
    }

    /// <summary>
    /// Uploads with an empty alt text placeholder; the real alt text is patched
    /// in once Gemini's response is available. The caption, in contrast, is
    /// known immediately: it comes only from what the operator typed, never
    /// from Gemini, so it is set here and never touched again. Array order is
    /// preserved by the caller (LINQ Select over an ordered source), so no
    /// explicit index needs to travel through this method.
    /// </summary>
    /// <summary>Runs one image conversion, turning any decode or resize failure into a readable image error.</summary>
    private static ProcessedImage ReadImage(Func<ProcessedImage> process, string fileName)
    {
        try
        {
            return process();
        }
        catch (Exception ex)
        {
            throw new PublishFailure(PublishCodes.ImageUnreadable, $"Could not process '{fileName}': {ex.Message}");
        }
    }

    private async Task<GutenbergBuilder.ImageInfo> UploadBodyImageAsync(
        UploadedImage image, List<int> uploadedMediaIds, CancellationToken ct)
    {
        ProcessedImage processed = ReadImage(() => images.ProcessBody(image.Bytes), image.FileName);
        WpMedia media = await wordPress.UploadMediaAsync(processed.Bytes, $"img-{Guid.NewGuid():N}.jpg", ct);

        lock (uploadedMediaIds)
        {
            uploadedMediaIds.Add(media.Id);
        }

        string? caption = string.IsNullOrWhiteSpace(image.Caption) ? null : image.Caption.Trim();
        return new GutenbergBuilder.ImageInfo(media.Id, media.SourceUrl, processed.Width, processed.Height, "", caption);
    }

    private async Task<WpMedia> UploadFeaturedImageAsync(
        UploadedImage image, List<int> uploadedMediaIds, CancellationToken ct)
    {
        FaceDetectionResult? faceRegion = await gemini.DetectFaceRegionAsync(image.Bytes, ct);
        ProcessedImage processed = ReadImage(() => images.ProcessFeatured(image.Bytes, faceRegion), image.FileName);
        WpMedia media = await wordPress.UploadMediaAsync(processed.Bytes, $"featured-{Guid.NewGuid():N}.jpg", ct);

        lock (uploadedMediaIds)
        {
            uploadedMediaIds.Add(media.Id);
        }

        return media;
    }

    /// <summary>
    /// Patches WordPress media metadata with Gemini's alt text and, if the
    /// operator supplied one, the caption set at upload time. Fires even when
    /// only a caption is present (no alt text from Gemini), so a real,
    /// operator-provided credit is never silently dropped.
    /// </summary>
    private async Task ApplyAltTextAsync(
        IReadOnlyList<GutenbergBuilder.ImageInfo> bodyImages,
        IReadOnlyList<ImagePlacement> placements,
        CancellationToken ct)
    {
        var tasks = new List<Task>();

        for (var i = 0; i < bodyImages.Count; i++)
        {
            string altText = placements.FirstOrDefault(p => p.ImageIndex == i)?.AltText ?? "";
            string? caption = bodyImages[i].Caption;

            if (string.IsNullOrWhiteSpace(altText) && string.IsNullOrWhiteSpace(caption))
            {
                continue;
            }

            tasks.Add(wordPress.SetMediaMetadataAsync(bodyImages[i].MediaId, altText, caption, ct));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>Merges in Gemini's alt text only. Caption was already set at upload time and is never overwritten here.</summary>
    private static GutenbergBuilder.ImageInfo[] MergeAltText(
        IReadOnlyList<GutenbergBuilder.ImageInfo> bodyImages, IReadOnlyList<ImagePlacement> placements)
    {
        return bodyImages
            .Select((image, i) =>
            {
                ImagePlacement? placement = placements.FirstOrDefault(p => p.ImageIndex == i);
                return placement is null ? image : image with { AltText = placement.AltText };
            })
            .ToArray();
    }
}

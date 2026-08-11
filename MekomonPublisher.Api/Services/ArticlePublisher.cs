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
            Task<GeneratedArticle> generationTask = gemini.GenerateAsync(
                request.RawText, categoryName, request.Images.Count, ct);

            Task<GutenbergBuilder.ImageInfo>[] bodyUploadTasks = request.Images
                .Select(image => UploadBodyImageAsync(image, uploadedMediaIds, ct))
                .ToArray();

            Task<WpMedia> featuredUploadTask = UploadFeaturedImageAsync(
                request.Images[request.FeaturedImageIndex], uploadedMediaIds, ct);

            await Task.WhenAll([.. bodyUploadTasks, featuredUploadTask]);
            GeneratedArticle article = await generationTask;

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
            logger.LogError(ex, "Publish failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            foreach (int mediaId in uploadedMediaIds)
            {
                await wordPress.DeleteMediaAsync(mediaId, CancellationToken.None);
            }

            db.PublishHistory.Add(new PublishHistoryEntry
            {
                CreatedAtUtc = DateTime.UtcNow,
                Success = false,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = ex.Message,
            });
            await db.SaveChangesAsync(CancellationToken.None);

            return new PublishResult
            {
                Success = false,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
    }

    private static void Validate(PublishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RawText) || request.RawText.Length < 50)
        {
            throw new InvalidOperationException("Article text is too short.");
        }

        if (!ArticleFormat.Categories.ContainsKey(request.CategoryId))
        {
            throw new InvalidOperationException($"Unknown category id {request.CategoryId}.");
        }

        if (request.Status is not ("publish" or "draft"))
        {
            throw new InvalidOperationException("Status must be 'publish' or 'draft'.");
        }

        if (request.Images.Count == 0)
        {
            throw new InvalidOperationException("At least one image is required.");
        }

        if (request.FeaturedImageIndex < 0 || request.FeaturedImageIndex >= request.Images.Count)
        {
            throw new InvalidOperationException("Featured image index is out of range.");
        }
    }

    /// <summary>
    /// Uploads with an empty alt text placeholder; the real alt text is patched
    /// in once Gemini's response is available. The caption, in contrast, is
    /// known immediately: it comes only from what the operator typed, never
    /// from Gemini, so it is set here and never touched again. Array order is
    /// preserved by the caller (LINQ Select over an ordered source), so no
    /// explicit index needs to travel through this method.
    /// </summary>
    private async Task<GutenbergBuilder.ImageInfo> UploadBodyImageAsync(
        UploadedImage image, List<int> uploadedMediaIds, CancellationToken ct)
    {
        ProcessedImage processed = images.ProcessBody(image.Bytes);
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
        ProcessedImage processed = images.ProcessFeatured(image.Bytes, faceRegion);
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

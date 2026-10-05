using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MekomonPublisher.Api.Models;

namespace MekomonPublisher.Api.Services;

/// <summary>
/// Thin wrapper over the WordPress REST API. BaseAddress and the Application
/// Password auth header are configured once, on registration, in Program.cs.
/// </summary>
public sealed class WordPressClient(HttpClient http)
{
    public async Task<WpMedia> UploadMediaAsync(byte[] bytes, string fileName, CancellationToken ct)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };

        HttpResponseMessage response = await http.PostAsync("wp-json/wp/v2/media", content, ct);
        await EnsureSuccessAsync(response, "upload media", ct);

        return (await response.Content.ReadFromJsonAsync<WpMedia>(cancellationToken: ct))!;
    }

    public async Task SetMediaMetadataAsync(int mediaId, string altText, string? caption, CancellationToken ct)
    {
        var body = new Dictionary<string, string> { ["alt_text"] = altText };
        if (!string.IsNullOrWhiteSpace(caption))
        {
            body["caption"] = caption;
        }

        HttpResponseMessage response = await http.PostAsJsonAsync($"wp-json/wp/v2/media/{mediaId}", body, ct);
        await EnsureSuccessAsync(response, $"set metadata on media {mediaId}", ct);
    }

    public async Task DeleteMediaAsync(int mediaId, CancellationToken ct)
    {
        // Best-effort cleanup on failure. Never throw: the operator's failure
        // message matters more than a media asset that will be visible and
        // deletable in wp-admin anyway.
        try
        {
            await http.DeleteAsync($"wp-json/wp/v2/media/{mediaId}?force=true", ct);
        }
        catch (HttpRequestException)
        {
            // Logged by the caller's catch block with full context; nothing more to do here.
        }
    }

    public async Task<IReadOnlyList<int>> ResolveOrCreateTagIdsAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        var ids = new List<int>();

        foreach (string name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            string trimmed = name.Trim();

            List<WpTerm>? found = await http.GetFromJsonAsync<List<WpTerm>>(
                $"wp-json/wp/v2/tags?search={Uri.EscapeDataString(trimmed)}&per_page=100", ct);

            WpTerm? exact = found?.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                ids.Add(exact.Id);
                continue;
            }

            HttpResponseMessage createResponse = await http.PostAsJsonAsync(
                "wp-json/wp/v2/tags", new { name = trimmed }, ct);

            // The search above only matches an exact, case-insensitive name;
            // WordPress's own duplicate check is stricter (e.g. it also
            // catches the tag Gemini names that happen to already be one of
            // the always-applied boilerplate tags). Rather than trust the
            // search result, treat "already exists" as success and reuse the
            // term_id WordPress reports, instead of failing the whole publish
            // over a tag that is, in effect, already there.
            if (createResponse.StatusCode == HttpStatusCode.BadRequest)
            {
                string errorBody = await createResponse.Content.ReadAsStringAsync(ct);
                int? existingTermId = TryExtractExistingTermId(errorBody);
                if (existingTermId is int termId)
                {
                    ids.Add(termId);
                    continue;
                }

                throw new WordPressApiException($"create tag '{trimmed}'", createResponse.StatusCode, errorBody);
            }

            await EnsureSuccessAsync(createResponse, $"create tag '{trimmed}'", ct);

            WpTerm created = (await createResponse.Content.ReadFromJsonAsync<WpTerm>(cancellationToken: ct))!;
            ids.Add(created.Id);
        }

        return ids.Distinct().ToList();
    }

    private static int? TryExtractExistingTermId(string errorBody)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(errorBody);
            JsonElement root = doc.RootElement;

            bool isTermExists = root.TryGetProperty("code", out JsonElement codeElement)
                && codeElement.GetString() == "term_exists";

            if (isTermExists
                && root.TryGetProperty("data", out JsonElement dataElement)
                && dataElement.TryGetProperty("term_id", out JsonElement termIdElement)
                && termIdElement.TryGetInt32(out int termId))
            {
                return termId;
            }
        }
        catch (JsonException)
        {
            // Not the shape we expect; let the caller treat this as a genuine failure.
        }

        return null;
    }

    public async Task<WpPost> CreatePostAsync(CreatePostBody body, CancellationToken ct)
    {
        HttpResponseMessage response = await http.PostAsJsonAsync("wp-json/wp/v2/posts", body, ct);
        await EnsureSuccessAsync(response, "create post", ct);

        WpPost post = (await response.Content.ReadFromJsonAsync<WpPost>(cancellationToken: ct))!;

        // The REST API's own "link" is the long UTF-8-encoded Hebrew-slug
        // permalink. The short "?p={id}" form works regardless of the site's
        // permalink structure and is what the operator actually wants to
        // share, so it replaces the long one here rather than downstream.
        post.Link = new Uri(http.BaseAddress!, $"?p={post.Id}").ToString();
        return post;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(ct);
        throw new WordPressApiException(action, response.StatusCode, body);
    }
}

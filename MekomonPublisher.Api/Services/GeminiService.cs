using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MekomonPublisher.Api.Config;
using MekomonPublisher.Api.Models;
using Microsoft.Extensions.Options;

namespace MekomonPublisher.Api.Services;

/// <summary>
/// Rewrites raw article text into the site's house style using Gemini's
/// structured output mode. Returns typed blocks, never HTML: see the
/// remarks on <see cref="GutenbergBuilder"/> for why.
/// </summary>
public sealed class GeminiService(HttpClient http, IOptions<GeminiOptions> options)
{
    private readonly GeminiOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GeneratedArticle> GenerateAsync(
        string rawText, string categoryName, int imageCount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Gemini:ApiKey is not configured. Set it via 'dotnet user-secrets set Gemini:ApiKey <key>'.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException(
                "Gemini:Model is not configured. Set it in appsettings.Development.json to the current Gemini model id.");
        }

        string? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            string prompt = BuildPrompt(rawText, categoryName, imageCount, lastError);
            string responseText = await CallGeminiAsync(prompt, ct);

            try
            {
                GeneratedArticle? article = JsonSerializer.Deserialize<GeneratedArticle>(responseText, JsonOptions);
                if (article is null || string.IsNullOrWhiteSpace(article.Title))
                {
                    lastError = "Response deserialized but Title was empty.";
                    continue;
                }

                return article;
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
            }
        }

        throw new InvalidOperationException($"Gemini returned an unusable response after 2 attempts: {lastError}");
    }

    private async Task<string> CallGeminiAsync(string prompt, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = prompt } },
                },
            },
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = _options.Temperature,
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = BuildResponseSchema(),
            },
        };

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent?key={_options.ApiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        HttpResponseMessage response = await http.SendAsync(request, linkedCts.Token);
        string responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini API returned {(int)response.StatusCode}: {responseBody}");
        }

        using JsonDocument doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()
            ?? throw new InvalidOperationException("Gemini response had no text content.");
    }

    private static string BuildPrompt(string rawText, string categoryName, int imageCount, string? repairNote)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are the house editor for מקומון ראשון, a Hebrew local news site. Rewrite the raw text below into the site's article style and return ONLY the JSON described by the response schema, nothing else.");
        sb.AppendLine();
        sb.AppendLine("House style rules, derived from the site's own published articles:");
        sb.AppendLine("- Hebrew, right-to-left. Keep any proper nouns (brand names, English phrases) as given.");
        sb.AppendLine("- 'subtitle' is the deck: one full lead sentence or two, 106 to 350 characters, not a short headline.");
        sb.AppendLine("- Body 'blocks': at most one 'heading3' block, used only as the lead paragraph immediately after the deck. Every other block is 'paragraph'. Do not use headings as a substitute for paragraphs.");
        sb.AppendLine("- Paragraphs that open with an attribution or a named source start with a bold lead, e.g. '<strong>שם, תפקיד, אמר</strong>: ...'.");
        sb.AppendLine("- Never use <blockquote>. Never use <h1> or <h4> or lower. Inline formatting is limited to <strong>, <em>, <a>, <br>.");
        sb.AppendLine("- 'metaDescription' is a genuine summary of 120 to 155 characters, not the title repeated.");
        sb.AppendLine("- 'seoTitle' is at most 66 characters and does not repeat the site name.");
        sb.AppendLine("- 'focusKeyword' is the one or two words naming the central person, business, or topic.");
        sb.AppendLine("- 'tags' is 1 to 2 topical tags naming the central entity. Do not include generic site or location tags; those are added automatically.");
        sb.AppendLine($"- The article will include exactly {imageCount} image(s), indexed 0 to {imageCount - 1} in upload order. Provide one 'imagePlacements' entry per image with a descriptive Hebrew 'altText'. Images 0 and 1 are always placed automatically as the lead and second image, so their 'afterBlockIndex' is ignored, but still give them alt text. For images with index 2 or higher, set 'afterBlockIndex' to place them naturally within the body (0 means right after the deck, before any block).");
        sb.AppendLine();
        sb.AppendLine($"Category: {categoryName}");
        sb.AppendLine();
        sb.AppendLine("Raw text:");
        sb.AppendLine(rawText);

        if (repairNote is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Your previous response was rejected: {repairNote}. Return valid JSON matching the schema exactly.");
        }

        return sb.ToString();
    }

    private static JsonObject BuildResponseSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["title"] = new JsonObject { ["type"] = "string" },
            ["subtitle"] = new JsonObject { ["type"] = "string" },
            ["blocks"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "heading3", "paragraph", "list" } },
                        ["html"] = new JsonObject { ["type"] = "string" },
                        ["items"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                    },
                    ["required"] = new JsonArray { "type" },
                },
            },
            ["imagePlacements"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["imageIndex"] = new JsonObject { ["type"] = "integer" },
                        ["afterBlockIndex"] = new JsonObject { ["type"] = "integer" },
                        ["altText"] = new JsonObject { ["type"] = "string" },
                        ["caption"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray { "imageIndex", "altText" },
                },
            },
            ["focusKeyword"] = new JsonObject { ["type"] = "string" },
            ["seoTitle"] = new JsonObject { ["type"] = "string" },
            ["metaDescription"] = new JsonObject { ["type"] = "string" },
            ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
        ["required"] = new JsonArray { "title", "subtitle", "blocks", "imagePlacements", "focusKeyword", "seoTitle", "metaDescription", "tags" },
    };
}

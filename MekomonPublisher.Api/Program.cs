using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using MekomonPublisher.Api.Config;
using MekomonPublisher.Api.Data;
using MekomonPublisher.Api.Models;
using MekomonPublisher.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// `dotnet run -- hash-password <password>` prints a hash and exits, never
// starting the web server. This is how you produce the value that goes into
// Security:OperatorPasswordHash - the plain password itself is never stored
// anywhere, in this app or in your shell history if you avoid pasting it
// into a command a second time.
if (args is ["hash-password", var passwordToHash])
{
    var hasher = new PasswordHasher<object>();
    Console.WriteLine(hasher.HashPassword(new object(), passwordToHash));
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<WordPressOptions>(builder.Configuration.GetSection(WordPressOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=App_Data/mekomon-publisher.db"));

builder.Services.AddSingleton<ImageProcessor>();
builder.Services.AddScoped<ArticlePublisher>();
builder.Services.AddSingleton<PasswordHasher<object>>();

builder.Services.AddHttpClient<GeminiService>();

builder.Services.AddHttpClient<WordPressClient>((sp, client) =>
{
    WordPressOptions wp = sp.GetRequiredService<IOptions<WordPressOptions>>().Value;
    client.BaseAddress = new Uri(wp.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(wp.MediaUploadTimeoutSeconds);

    // HttpClient sends no User-Agent at all unless one is set. This host's
    // LiteSpeed layer 403s requests with no/unrecognized User-Agent before
    // they ever reach WordPress (confirmed while analysing the site's own
    // article format), so every request needs to look like a real browser.
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");

    if (!string.IsNullOrWhiteSpace(wp.Username) && !string.IsNullOrWhiteSpace(wp.ApplicationPassword))
    {
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{wp.Username}:{wp.ApplicationPassword}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "mekomon_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // "SameAsRequest" rather than "Always": this still needs to work over
        // plain http://localhost in local dev, and becomes Secure-only
        // automatically the moment it's actually served over HTTPS in production.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        // This is an API behind a SPA, not a server-rendered site: on missing
        // auth we want a plain 401 the frontend can react to, not a redirect
        // to an HTML login page that doesn't exist at this route.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    // Once this is on a public URL, the login endpoint is the only thing an
    // attacker can reach without a session. 5 attempts/minute/IP is enough
    // friction to make brute-forcing impractical without getting in your way.
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

WebApplication app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));

using (IServiceScope scope = app.Services.CreateScope())
{
    // A single-user personal tool does not need migration ceremony:
    // EnsureCreated is the officially recommended approach when there is no
    // team to coordinate schema changes across.
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/login", async (
    LoginRequest body,
    IOptions<SecurityOptions> securityOptions,
    PasswordHasher<object> hasher,
    HttpContext http) =>
{
    SecurityOptions security = securityOptions.Value;
    if (string.IsNullOrWhiteSpace(security.OperatorUsername) || string.IsNullOrWhiteSpace(security.OperatorPasswordHash))
    {
        return Results.Problem("Login is not configured on the server.", statusCode: StatusCodes.Status500InternalServerError);
    }

    bool usernameMatches = string.Equals(body.Username, security.OperatorUsername, StringComparison.Ordinal);
    PasswordVerificationResult passwordResult = hasher.VerifyHashedPassword(
        new object(), security.OperatorPasswordHash, body.Password);

    if (!usernameMatches || passwordResult == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, security.OperatorUsername)],
        CookieAuthenticationDefaults.AuthenticationScheme);

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.NoContent();
})
.RequireRateLimiting("login");

app.MapPost("/api/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});

app.MapGet("/api/auth/me", (HttpContext http) =>
    Results.Ok(new { username = http.User.Identity!.Name }))
    .RequireAuthorization();

app.MapGet("/api/categories", () =>
    Results.Ok(ArticleFormat.Categories.Select(kv => new { id = kv.Key, name = kv.Value })))
    .RequireAuthorization();

app.MapGet("/api/history", async (AppDbContext db) =>
    Results.Ok(await db.PublishHistory
        .OrderByDescending(h => h.CreatedAtUtc)
        .Take(20)
        .ToListAsync()))
    .RequireAuthorization();

app.MapPost("/api/publish", async (HttpRequest request, ArticlePublisher publisher, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data." });
    }

    IFormCollection form = await request.ReadFormAsync(ct);

    if (!int.TryParse(form["categoryId"], out int categoryId) ||
        !int.TryParse(form["featuredImageIndex"], out int featuredImageIndex))
    {
        return Results.BadRequest(new { error = "categoryId and featuredImageIndex must be integers." });
    }

    var images = new List<UploadedImage>();
    foreach (IFormFile file in form.Files)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        images.Add(new UploadedImage { FileName = file.FileName, Bytes = buffer.ToArray() });
    }

    var publishRequest = new PublishRequest
    {
        RawText = form["rawText"].ToString(),
        CategoryId = categoryId,
        Status = form["status"].ToString(),
        FeaturedImageIndex = featuredImageIndex,
        Images = images,
    };

    PublishResult result = await publisher.PublishAsync(publishRequest, ct);
    return Results.Ok(result);
})
.RequireAuthorization();

app.MapFallbackToFile("index.html");

app.Run();

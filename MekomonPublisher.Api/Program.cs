using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.Xml.Linq;
using MekomonPublisher.Api.Config;
using MekomonPublisher.Api.Data;
using MekomonPublisher.Api.Models;
using MekomonPublisher.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

// `dotnet run -- generate-dp-key` prints one Data Protection key as an XML
// blob and exits. Put the printed value verbatim into the
// Security:DataProtectionKeyXml env var in production (Security__DataProtectionKeyXml
// on Render). Generate this once and never regenerate it casually - doing so
// invalidates every previously-issued login cookie, same as the bug this exists
// to fix.
if (args is ["generate-dp-key"])
{
    var repo = new CapturingXmlRepository();
    ServiceProvider keyGenServices = new ServiceCollection()
        .AddDataProtection()
        .AddKeyManagementOptions(o => o.XmlRepository = repo)
        .Services
        .BuildServiceProvider();

    keyGenServices.GetRequiredService<IKeyManager>()
        .CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(50));

    Console.WriteLine(repo.Captured!.ToString(SaveOptions.DisableFormatting));
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<WordPressOptions>(builder.Configuration.GetSection(WordPressOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")
        ?? "Data Source=App_Data/mekomon-publisher.db"));

// Without this, the auth cookie's encryption key ring lives only in the
// current container's memory/ephemeral disk. On a host with no persistent
// disk (this app's Render plan), every restart - a redeploy, a health-check
// restart, or the free tier simply spinning down after idle and waking back
// up - recreates the filesystem from scratch, generates a brand new key ring,
// and every already-issued login cookie stops decrypting: the operator gets
// bounced with a bare 401 on their very next request, mid-article, for no
// visible reason. An environment variable is the one thing that does survive
// all of those, so when Security:DataProtectionKeyXml is set (see
// `dotnet run -- generate-dp-key`), pin the key ring to that single fixed key
// instead of trying to persist a rotating one to a disk that won't be there
// next time. Falls back to file-system persistence for local dev, where the
// disk really is persistent.
string? keyXml = builder.Configuration["Security:DataProtectionKeyXml"];
IDataProtectionBuilder dataProtection = builder.Services.AddDataProtection().SetApplicationName("MekomonPublisher");

if (!string.IsNullOrWhiteSpace(keyXml))
{
    dataProtection
        .AddKeyManagementOptions(o => o.XmlRepository = new FixedKeyXmlRepository(XElement.Parse(keyXml)))
        .DisableAutomaticKeyGeneration();
}
else
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
}

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

// Render (and most PaaS hosts) sit in front of this app as a reverse proxy,
// so Kestrel would otherwise see every request as coming from the proxy's
// internal IP, making any per-IP logic below silently useless. Trusting the
// forwarded headers here is what lets RemoteIpAddress reflect the real
// visitor. KnownProxies/KnownNetworks are cleared because Render's edge
// isn't a fixed, allowlist-able address the way an on-prem load balancer
// would be; Kestrel itself is never directly reachable from the internet
// here, so trusting the one hop in front of it is the standard tradeoff.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    // Partitioned per client IP, not global: a fixed window limiter added via
    // AddFixedWindowLimiter (no partition key) is a single shared bucket for
    // every visitor combined, which both throttles legitimate users
    // needlessly and is trivially exhausted by one attacker to lock everyone
    // else out. AddPolicy + RateLimitPartition gives each IP its own bucket.
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // A compromised or just-buggy session shouldn't be able to burn through
    // the Gemini budget or flood WordPress with drafts at wire speed. This is
    // generous enough that no normal single-operator workflow ever notices it.
    options.AddPolicy("publish", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
        }));

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

// Must run before anything that reads the client's address (rate limiting,
// logging, auth) so those see the real visitor, not Render's proxy.
app.UseForwardedHeaders();

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

    // One "imageCaptions" value per image, in the same order the files were
    // appended, sent even when empty so the two lists always line up by index.
    Microsoft.Extensions.Primitives.StringValues captions = form["imageCaptions"];

    var images = new List<UploadedImage>();
    for (var i = 0; i < form.Files.Count; i++)
    {
        IFormFile file = form.Files[i];
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        string? caption = i < captions.Count && !string.IsNullOrWhiteSpace(captions[i]) ? captions[i] : null;
        images.Add(new UploadedImage { FileName = file.FileName, Bytes = buffer.ToArray(), Caption = caption });
    }

    var publishRequest = new PublishRequest
    {
        RawText = form["rawText"].ToString(),
        CategoryId = categoryId,
        Status = form["status"].ToString(),
        FeaturedImageIndex = featuredImageIndex,
        Images = images,
        // Missing/unparseable defaults to true so older clients (and any
        // request that simply omits the field) keep today's AI behavior.
        UseAi = !bool.TryParse(form["useAi"], out bool useAi) || useAi,
        Title = form["title"].ToString(),
        Subtitle = form["subtitle"].ToString(),
    };

    PublishResult result = await publisher.PublishAsync(publishRequest, ct);
    return Results.Ok(result);
})
.RequireAuthorization()
.RequireRateLimiting("publish");

app.MapFallbackToFile("index.html");

app.Run();

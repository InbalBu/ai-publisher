# Mekomon AI Publisher

Turns raw Hebrew copy plus a folder of photos into a published WordPress
article on [mekomonrishon.co.il](https://mekomonrishon.co.il) in under a
minute. A shared tool behind a single login, run locally or deployed behind
a public URL.

## How it's built

One backend project, one frontend project, no job queue, one shared operator
login. A single form submits a publish request; the server processes images,
calls Gemini, uploads to WordPress, and returns a result while the browser
waits with a spinner. That is the whole shape.

```
MekomonPublisher.Api/   ASP.NET Core 8 minimal API
  Config/                Gemini/WordPress/Security options, fixed site data (categories, tags)
  Models/                Request/response DTOs
  Data/                  One SQLite table: publish history
  Services/
    GeminiService.cs      Calls Gemini, returns structured JSON blocks (not HTML,
                           since attachment IDs don't exist until after upload)
    ImageProcessor.cs     Resizes/compresses via ImageSharp
    WordPressClient.cs    Talks to the WordPress REST API
    GutenbergBuilder.cs   Assembles the final block markup from blocks + media IDs
    HtmlSafety.cs          Strips script/iframe/on* from Gemini's output before
                           it goes into a live public post
    ArticlePublisher.cs   Orchestrates the above, synchronously, per request
  Program.cs               Also doubles as a CLI: `dotnet run -- hash-password <pw>`

web/                    React + Vite + TypeScript + MUI
  src/LoginPage.tsx      Gate shown when there's no session
  src/App.tsx            The publish form, shown once logged in

wp-bridge/
  mekomon-ai-bridge.php  mu-plugin: exposes 3 Yoast meta keys for REST write

Dockerfile              One image: API + built SPA, same origin, no CORS needed
```

WordPress remains the source of truth for articles. The local SQLite database
(`App_Data/mekomon-publisher.db`, created automatically) holds nothing but a
log of past publish attempts: title, URL, status, elapsed time, and the error
if one failed.

## Prerequisites

- .NET 8 SDK
- Node 18+
- A WordPress user with the Editor role and an Application Password
  (Users -> Profile -> Application Passwords)
- A Gemini API key
- Docker, only if you're building the deployable image

## Setup

1. Copy `wp-bridge/mekomon-ai-bridge.php` to `wp-content/mu-plugins/` on the
   WordPress site. Without it, SEO fields will silently fail to save (Yoast's
   meta keys are not exposed for REST write by default).

2. Configure secrets locally (never committed):

   ```bash
   cd MekomonPublisher.Api
   dotnet user-secrets set "Gemini:ApiKey" "<your key>"
   dotnet user-secrets set "WordPress:Username" "<your wp username>"
   dotnet user-secrets set "WordPress:ApplicationPassword" "<xxxx xxxx xxxx xxxx xxxx xxxx>"
   ```

3. Set the Gemini model. This is left blank in `appsettings.json` on purpose:
   model names change, and a wrong guess baked into source is worse than an
   obvious failure. Put the current model id in
   `MekomonPublisher.Api/appsettings.Development.json`:

   ```json
   { "Gemini": { "Model": "<current gemini model id>" } }
   ```

4. Set the operator login. There is one shared username/password for whoever
   uses this tool - not a per-person account system. Generate the hash first
   (this prints a hash to your terminal, never stores the plain password
   anywhere):

   ```bash
   dotnet run -- hash-password "<your chosen password>"
   ```

   Then:

   ```bash
   dotnet user-secrets set "Security:OperatorUsername" "<your chosen username>"
   dotnet user-secrets set "Security:OperatorPasswordHash" "<the hash just printed>"
   ```

## Run locally

Two terminals:

```bash
cd MekomonPublisher.Api && dotnet run    # http://localhost:5199
cd web && npm install && npm run dev     # http://localhost:5173
```

Open `http://localhost:5173`. Vite proxies `/api/*` to the backend, so there
is no CORS configuration anywhere, in dev or in production.

## Deploying so you can share a URL

The whole app is one Docker image (API + built SPA, same origin). Recommended
host: **Render** - connect a GitHub repo, point it at the `Dockerfile`, set
environment variables in its dashboard, done. Steps:

1. Push this repo to GitHub.
2. On Render: **New -> Web Service**, connect the repo, environment = Docker.
   It will find the root `Dockerfile` automatically.
3. Set these environment variables in Render's dashboard (same keys as the
   user-secrets above, just with `__` instead of `:`):

   ```
   Gemini__ApiKey
   Gemini__Model
   WordPress__Username
   WordPress__ApplicationPassword
   Security__OperatorUsername
   Security__OperatorPasswordHash
   ```

4. Render exposes the container on the port declared by `EXPOSE 8080` in the
   Dockerfile automatically. If it doesn't pick this up on your account, set
   the service's port to `8080` explicitly in its dashboard.
5. **Persistent storage is optional and costs extra.** Render's free tier has
   an ephemeral filesystem - the SQLite history log resets on every redeploy.
   That's harmless (WordPress stays the real source of truth for articles),
   but if you want the log to survive, attach a persistent disk on a paid
   plan, mount it at `/app/App_Data`, and everything already points there.
6. Once deployed, share the Render URL with your WordPress manager along with
   the operator username/password from step 4 of Setup above. HTTPS is
   automatic on Render, which matters here: the session cookie is configured
   to go Secure-only the moment it's served over HTTPS.

Verified locally before writing these steps: built the actual Docker image,
ran the actual container, confirmed the SPA serves, `/api/categories` returns
`401` with no session, login succeeds and sets a cookie, and the same request
then returns `200` with the session cookie attached. That's the exact
artifact Render would build and run, tested end to end.

## What was verified

- Backend and frontend build clean, no warnings.
- Full publish pipeline confirmed against the real WordPress site and a real
  Gemini call: image upload, alt-text patching, tag resolve/create, post
  creation all succeeded with real HTTP `201`/`200` responses, logged to
  local history, round-tripped through the actual browser UI. ~22 seconds
  end to end, well under the 1-minute target.
- Login flow verified directly against the running API: unauthenticated
  requests get `401`, wrong password gets `401`, correct password sets a
  session cookie, that cookie then unlocks the protected routes, logout
  actually invalidates the session (not just client-side), and the login
  endpoint's rate limiter kicks in and returns `429` after a handful of
  rapid attempts.
- The Dockerfile builds and the resulting container was smoke-tested for
  real (see the deployment section above) - not just written and assumed
  to work.

**Bug found and fixed during browser testing:** the main textarea caused
React's "Maximum update depth exceeded" on every keystroke, because a
`slotProps` object literal was being recreated on every render and MUI's
autosize textarea reacted badly to the changing reference. Fixed by removing
it; the page-level `dir="rtl"` is sufficient for correct text direction.

**Host quirk found and fixed:** `HttpClient` sends no `User-Agent` header
unless one is set explicitly, and mekomonrishon.co.il's LiteSpeed layer 403s
requests with no/unrecognized User-Agent before they ever reach WordPress.
Fixed by setting a realistic browser User-Agent on the WordPress HTTP client.

## Known simplifications (deliberate)

- **One shared login, not a user system.** There's exactly one
  username/password, configured server-side. No registration, no per-user
  roles, no password reset flow - if you need to rotate it, generate a new
  hash and update the environment variable.
- **No job queue.** Publishing blocks the request until it finishes. That is
  the point: the whole system exists to make that finish in under a minute.
- **No slug generation.** WordPress derives it from the title itself
  (`sanitize_title`), so we let it, rather than reimplementing that logic.
- **History is a log, not a source of truth.** If it disagrees with
  WordPress, WordPress wins. It may also be wiped on redeploy depending on
  your hosting plan (see the deployment section) - by design, not a bug.

## Conventions

- Never use an em dash or en dash anywhere, including code comments and UI copy.
- Package versions are pinned explicitly (see the ImageSharp note below).

## A licensing note

`SixLabors.ImageSharp` 4.x requires a paid commercial license as of this
writing. This project pins **3.1.11**, the latest release still under the
free Six Labors Split License tier, which also has the CVEs from earlier
3.1.x versions patched. If you ever bump this package, check the license
terms first.

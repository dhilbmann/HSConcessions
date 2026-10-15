using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Web.Api;

public static class PublicApi
{
    public static void MapPublicApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/public");

        api.MapGet("/state", async (PublicStateService svc, CancellationToken ct) =>
            Results.Json(await svc.BuildPublicAsync(ct), JsonStore.JsonOptions));

        api.MapGet("/display/{standId}", (string standId, string? layout, PublicStateService svc) =>
            svc.BuildDisplay(standId, layout) is { } state
                ? Results.Json(state, JsonStore.JsonOptions)
                : Results.NotFound(new { error = "Stand not found." }));
    }

    public static void MapThemeAndPages(this WebApplication app)
    {
        app.MapGet("/theme.css", (HttpContext ctx, ThemeService theme) =>
        {
            var (css, etag) = theme.ThemeCss();
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.ETag = etag;
            if (ctx.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Text(css, "text/css; charset=utf-8");
        });

        app.MapGet("/manifest.json", (HttpContext ctx, ThemeService theme) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            return Results.Text(theme.Manifest(), "application/manifest+json; charset=utf-8");
        });

        app.MapGet("/favicon.ico", (HttpContext ctx, JsonStore store, ImageService images) =>
        {
            var fav = store.Read<SiteSettings>(DataFileNames.Settings).Images.Favicon;
            ctx.Response.Headers.CacheControl = "no-cache";
            var path = fav is null ? null : Path.Combine(images.ImagesPath, fav + ".ico");
            return path is not null && File.Exists(path) ? Results.File(path, "image/x-icon") : Results.NotFound();
        });

        app.MapGet("/", (HttpContext ctx, PageRenderer pages) => pages.Render(ctx, "index.html"));

        app.MapGet("/display/{slug}", (string slug, HttpContext ctx, JsonStore store, PageRenderer pages) =>
            FindStand(store, slug) is not null ? pages.Render(ctx, "display.html") : pages.NotFound(ctx));

        // Stand pages: /{slug}. Static files and other routes take precedence.
        app.MapGet("/{slug}", (string slug, HttpContext ctx, JsonStore store, PageRenderer pages) =>
            FindStand(store, slug) is not null ? pages.Render(ctx, "index.html") : pages.NotFound(ctx))
            .WithOrder(int.MaxValue);
    }

    private static Stand? FindStand(JsonStore store, string slug) =>
        store.Read<StandsFile>(DataFileNames.Stands).Stands
            .FirstOrDefault(s => !s.Archived && string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Serves index.html / display.html with instance values substituted (title, meta), never cached.</summary>
public sealed class PageRenderer(IWebHostEnvironment env, JsonStore store)
{
    public IResult Render(HttpContext ctx, string page, int status = 200)
    {
        var path = Path.Combine(env.WebRootPath, page);
        var html = File.ReadAllText(path);
        var s = store.Read<SiteSettings>(DataFileNames.Settings);
        var site = PublicStateService.Site(s);
        var fav = s.Images.Favicon;
        var icons = fav is null
            ? ""
            : $"<link rel=\"icon\" href=\"/favicon.ico\" sizes=\"any\"><link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"/images/{fav}-32.png\"><link rel=\"apple-touch-icon\" href=\"/images/{fav}-180.png\">";
        html = html
            .Replace("{{siteTitle}}", Enc(s.SiteTitle))
            .Replace("{{siteShortName}}", Enc(s.SiteShortName))
            .Replace("{{description}}", Enc($"{s.SiteTitle} — {s.SchoolName} concessions menu"))
            .Replace("{{themeColor}}", Enc(ColorUtil.Normalize(s.Colors.Primary, "#1F3A5F")))
            .Replace("{{logoUrl}}", Enc(site.LogoUrl))
            .Replace("{{icons}}", icons)
            .Replace("{{version}}", AppVersion.Value);
        NoStore(ctx.Response);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    }

    public IResult NotFound(HttpContext ctx) => Render(ctx, "404.html", StatusCodes.Status404NotFound);

    private static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);

    public static void NoStore(HttpResponse r)
    {
        r.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        r.Headers.Pragma = "no-cache";
        r.Headers.Expires = "0";
    }
}

/// <summary>
/// Cache-busting version for static CSS/JS (served with a one-day cache). It is a hash of the files' contents,
/// so a style- or script-only change gets a new URL and reaches phones and TVs right away.
/// </summary>
public static class AppVersion
{
    public static string Value { get; private set; } =
        File.GetLastWriteTimeUtc(typeof(AppVersion).Assembly.Location).Ticks.ToString("x");

    public static void Initialize(string webRootPath) => Value = Compute(webRootPath);

    public static string Compute(string webRootPath)
    {
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var dir in new[] { "css", "js" })
        {
            var root = Path.Combine(webRootPath, dir);
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                sha.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(webRootPath, file).Replace('\\', '/')));
                sha.AppendData(File.ReadAllBytes(file));
            }
        }
        return Convert.ToHexString(sha.GetHashAndReset())[..12].ToLowerInvariant();
    }
}

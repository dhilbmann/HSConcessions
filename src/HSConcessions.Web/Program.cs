using System.Security.Claims;
using HSConcessions.Web;
using System.Threading.RateLimiting;
using HSConcessions.Web.Api;
using HSConcessions.Web.Auth;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Optional .env file next to the app (never committed) for OAuth secrets.
DotEnv.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"), builder.Configuration);

// ---------- options ----------
var storage = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new();
storage.DataPath = Rooted(storage.DataPath);
storage.ImagesPath = Rooted(storage.ImagesPath);
storage.LogsPath = Rooted(storage.LogsPath);
storage.BackupsPath = Rooted(storage.BackupsPath);
var instance = builder.Configuration.GetSection("Instance").Get<InstanceOptions>() ?? new();
var seed = builder.Configuration.GetSection("Seed").Get<SeedOptions>() ?? new();
if (!string.IsNullOrWhiteSpace(seed.SamplesPath)) seed.SamplesPath = Rooted(seed.SamplesPath);
var features = builder.Configuration.GetSection("Features").Get<FeatureOptions>() ?? new();
var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new();
var demo = builder.Configuration.GetSection("Demo").Get<DemoOptions>() ?? new();
var hosting = builder.Configuration.GetSection("Hosting").Get<HostingOptions>() ?? new();
// One-click sign-in exists only in Development or on an explicitly enabled public demo.
auth.DevLogin.RestrictTo(builder.Environment.IsDevelopment() || demo.Enabled);

string Rooted(string p) => Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(builder.Environment.ContentRootPath, p));

Directory.CreateDirectory(storage.LogsPath);

// ---------- logging ----------
builder.Host.UseSerilog((ctx, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Map(
        le => le.Timestamp.ToString("yyyy-MM-dd"),
        (date, wt) => wt.File(Path.Combine(storage.LogsPath, $"{instance.Name}-app-{date}.log"),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
            shared: true),
        sinkMapCountLimit: 2));

// ---------- services ----------
builder.Services.AddSingleton(storage);
builder.Services.AddSingleton(instance);
builder.Services.AddSingleton(seed);
builder.Services.AddSingleton(features);
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton(demo);
builder.Services.AddSingleton(new JsonStore(storage.DataPath));
builder.Services.AddSingleton<TimeService>();
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<ImageService>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<QrService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<SeedService>();
builder.Services.AddSingleton<StandService>();
builder.Services.AddSingleton<PublicStateService>();
builder.Services.AddSingleton<PageRenderer>();
builder.Services.AddSingleton<ICalendarProvider, ManualCalendarProvider>();
builder.Services.AddSingleton<ICalendarProvider, ICalFeedCalendarProvider>();
builder.Services.AddSingleton<CalendarService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddHostedService<BackupHostedService>();
builder.Services.AddHostedService<RetentionService>();
if (demo.Enabled) builder.Services.AddHostedService<DemoResetService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("ical", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("HSConcessions/1.0");
});

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "RequestVerificationToken";
    o.Cookie.Name = $".{instance.Name}.af";
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.AddAppAuthentication(auth, instance);

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin", Perm.Policy(Functions.ViewDashboard));
    o.Conventions.AllowAnonymousToPage("/Admin/Login");
    o.Conventions.AllowAnonymousToPage("/Admin/Denied");
    o.Conventions.AllowAnonymousToPage("/Admin/Forbidden");
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Key(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(Key(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));

    // Reads are not limited; writes are limited per signed-in user (or IP).
    o.AddPolicy("adminWrite", ctx =>
    {
        if (HttpMethods.IsGet(ctx.Request.Method)) return RateLimitPartition.GetNoLimiter("read");
        var who = ctx.User.FindFirstValue(ClaimTypes.Email) ?? Key(ctx);
        return RateLimitPartition.GetFixedWindowLimiter(who,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1) });
    });

    // The OIDC callback paths are handled by middleware, so limit them globally.
    var callbackPaths = AuthSetup.ResolveProviders(auth).Values.Select(p => p.CallbackPath).ToList();
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var path = ctx.Request.Path.Value ?? "";
        if (callbackPaths.Any(c => path.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
            return RateLimitPartition.GetFixedWindowLimiter("cb:" + Key(ctx),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) });
        if (path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(ctx.Request.Method))
            return RateLimitPartition.GetFixedWindowLimiter("adminpost:" + Key(ctx),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) });
        return RateLimitPartition.GetNoLimiter("none");
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    if (hosting.TrustForwardedHeaders)
    {
        // Platform load balancers aren't on loopback; trust their headers so HTTPS is detected correctly.
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    }
    // Otherwise only loopback is trusted (the default): Caddy on the same host.
});

// The app listens on plain HTTP behind a proxy (Caddy or a hosting platform) that serves HTTPS on 443.
// Without this, HTTPS redirection can't find a port and plain-HTTP requests aren't redirected.
builder.Services.AddHttpsRedirection(o => o.HttpsPort = builder.Configuration.GetValue<int?>("Hosting:HttpsPort") ?? 443);

builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = false;
});

var app = builder.Build();
AppVersion.Initialize(app.Environment.WebRootPath);

// ---------- first-run seed ----------
await app.Services.GetRequiredService<SeedService>().EnsureSeededAsync();

// ---------- pipeline ----------
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSerilogRequestLogging(o =>
{
    // Request summaries for admin endpoints only.
    o.GetLevel = (ctx, _, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
        : ctx.Request.Path.StartsWithSegments("/admin") || ctx.Request.Path.StartsWithSegments("/api/admin") ? LogEventLevel.Information
        : LogEventLevel.Verbose;
});

// Menu / price / status data is never cached.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/admin"))
        PageRenderer.NoStore(ctx.Response);
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});

// Never serve index.html/display.html directly as static files (they are rendered with instance values).
app.Use(async (ctx, next) =>
{
    var p = ctx.Request.Path.Value ?? "";
    if (p.Equals("/index.html", StringComparison.OrdinalIgnoreCase) || p.Equals("/display.html", StringComparison.OrdinalIgnoreCase) || p.Equals("/404.html", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.Redirect("/");
        return;
    }
    await next();
});

// Content-hashed images: cache forever.
Directory.CreateDirectory(storage.ImagesPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storage.ImagesPath),
    RequestPath = "/images",
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        if (ctx.File.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:";
    },
});

// Static CSS/JS/fonts: versioned query string; cache one day.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = ctx.File.Name.EndsWith(".woff2")
            ? "public, max-age=31536000, immutable"
            : "public, max-age=86400";
    },
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/error", () => Results.Problem("Something went wrong.")).ExcludeFromDescription();
app.MapAuthEndpoints(auth);
app.MapPublicApi();
app.MapAdminApi();
app.MapRazorPages();
app.MapThemeAndPages();

Log.Information("HSConcessions instance {Instance} starting; data at {DataPath}", instance.Name, storage.DataPath);
app.Run();

public partial class Program;

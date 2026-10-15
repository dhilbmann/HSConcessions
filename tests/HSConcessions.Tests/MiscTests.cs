using System.Text;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;

namespace HSConcessions.Tests;

public static class TestPaths
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HSConcessions.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
        }
    }
}

public class MiscTests
{
    [Theory]
    [InlineData("single", "auto", "rotate", "single")]   // URL preview wins
    [InlineData(null, "auto", "rotate", "auto")]         // then the stand's override
    [InlineData(null, null, "single", "single")]         // then the site default
    [InlineData("bogus", "nope", null, "rotate")]        // invalid values fall back to rotate
    public void Display_layout_resolution(string? query, string? stand, string? site, string expected)
    {
        Assert.Equal(expected, DisplayLayouts.Resolve(query, stand, site));
    }

    [Fact]
    public async Task Audit_log_keeps_the_first_record_of_each_day()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hsc-audit-" + Guid.NewGuid().ToString("n"));
        try
        {
            var audit = new AuditService(new StorageOptions { LogsPath = dir }, new InstanceOptions { Name = "t" },
                new Microsoft.AspNetCore.Http.HttpContextAccessor(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditService>.Instance);
            await audit.LogAsync("OpenStand", "Stand", "football", "Football");
            await audit.LogAsync("CloseStand", "Stand", "football", "Football");
            var file = Directory.GetFiles(dir).Single();
            Assert.NotEqual(0xEF, File.ReadAllBytes(file)[0]); // no byte-order mark
            var records = audit.Query(new AuditQuery(), TimeService.ZoneFor("America/New_York"));
            Assert.Equal(new[] { "CloseStand", "OpenStand" }, records.Select(r => r.Action));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Static_asset_version_changes_when_only_css_or_js_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "hsc-www-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, "css"));
        Directory.CreateDirectory(Path.Combine(root, "js"));
        try
        {
            File.WriteAllText(Path.Combine(root, "css", "a.css"), ".x{margin:0}");
            File.WriteAllText(Path.Combine(root, "js", "a.js"), "var a=1;");
            var v1 = HSConcessions.Web.Api.AppVersion.Compute(root);
            Assert.Equal(v1, HSConcessions.Web.Api.AppVersion.Compute(root));
            File.WriteAllText(Path.Combine(root, "css", "a.css"), ".x{margin:1px}");
            Assert.NotEqual(v1, HSConcessions.Web.Api.AppVersion.Compute(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Menu_updated_text_shows_time_today_and_date_otherwise()
    {
        var ny = TimeService.ZoneFor("America/New_York");
        var now = new DateTime(2026, 10, 15, 14, 0, 0, DateTimeKind.Utc);                 // 10:00 AM EDT
        Assert.Equal("Menu updated 9:15 AM", PublicStateService.UpdatedText(new DateTime(2026, 10, 15, 13, 15, 0, DateTimeKind.Utc), ny, now));
        Assert.Equal("Menu updated Oct 14, 8:38 PM", PublicStateService.UpdatedText(new DateTime(2026, 10, 15, 0, 38, 0, DateTimeKind.Utc), ny, now));
    }

    [Fact]
    public void Templates_fill_all_tokens()
    {
        var s = new SiteSettings { SchoolName = "Test High", MascotName = "Owls", SiteTitle = "Owl Eats" };
        Assert.Equal("Football 6:00 PM Test High Owls Owl Eats",
            TextTemplates.Fill("{stand} {time} {school} {mascot} {site}", s, "Football", "6:00 PM"));
    }

    [Fact]
    public void Svg_sanitizer_removes_scripts_handlers_and_external_refs()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" onload="alert(1)">
              <script>alert(1)</script>
              <foreignObject><div>x</div></foreignObject>
              <image xlink:href="https://evil.example/x.png" width="10" height="10"/>
              <use href="#ok"/>
              <rect id="ok" width="10" height="10" style="fill:url(https://evil.example/f)"/>
            </svg>
            """;
        var clean = Encoding.UTF8.GetString(ImageService.SanitizeSvg(Encoding.UTF8.GetBytes(svg)));
        Assert.DoesNotContain("script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreignObject", clean);
        Assert.DoesNotContain("evil.example", clean);
        Assert.Contains("href=\"#ok\"", clean);
    }

    [Fact]
    public void Svg_with_dtd_is_rejected()
    {
        var svg = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>";
        Assert.Throws<ImageValidationException>(() => ImageService.SanitizeSvg(Encoding.UTF8.GetBytes(svg)));
    }

    [Fact]
    public void Magic_bytes_are_checked()
    {
        Assert.Equal("png", ImageService.Sniff(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Assert.Equal("jpeg", ImageService.Sniff(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.Null(ImageService.Sniff(Encoding.ASCII.GetBytes("MZ\x90 not an image")));
    }

    [Fact]
    public async Task Catalog_images_are_resized_to_800_webp_with_thumbnail()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hsc-img-" + Guid.NewGuid().ToString("n"));
        try
        {
            var svc = new ImageService(new StorageOptions { ImagesPath = dir });
            using var bmp = new SkiaSharp.SKBitmap(2000, 1000);
            using (var canvas = new SkiaSharp.SKCanvas(bmp)) canvas.Clear(SkiaSharp.SKColors.OrangeRed);
            using var png = SkiaSharp.SKImage.FromBitmap(bmp).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            var id = await svc.SaveCatalogImageAsync(new MemoryStream(png.ToArray()), "image/png");

            Assert.StartsWith("img-", id);
            using var main = SkiaSharp.SKCodec.Create(Path.Combine(dir, id + ".webp"));
            Assert.Equal(SkiaSharp.SKEncodedImageFormat.Webp, main.EncodedFormat);
            Assert.Equal(800, Math.Max(main.Info.Width, main.Info.Height));
            using var thumb = SkiaSharp.SKCodec.Create(Path.Combine(dir, id + "-300.webp"));
            Assert.Equal(300, Math.Max(thumb.Info.Width, thumb.Info.Height));

            // Same content → same content-hashed name.
            var again = await svc.SaveCatalogImageAsync(new MemoryStream(png.ToArray()), "image/png");
            Assert.Equal(id, again);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Non_images_are_rejected()
    {
        var svc = new ImageService(new StorageOptions { ImagesPath = Path.Combine(Path.GetTempPath(), "hsc-img-" + Guid.NewGuid().ToString("n")) });
        await Assert.ThrowsAsync<ImageValidationException>(() =>
            svc.SaveCatalogImageAsync(new MemoryStream(Encoding.ASCII.GetBytes("<html>nope</html>")), "image/png"));
    }

    [Fact]
    public void Retention_deletes_logs_older_than_90_days()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hsc-logs-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            File.WriteAllText(Path.Combine(dir, "x-app-2026-07-15.log"), "old");      // 91 days
            File.WriteAllText(Path.Combine(dir, "x-audit-2026-07-16.jsonl"), "keep");  // 90 days
            File.WriteAllText(Path.Combine(dir, "x-app-2026-10-14.log"), "today");
            var removed = RetentionService.Prune(dir, now, 90);
            Assert.Equal(1, removed);
            Assert.False(File.Exists(Path.Combine(dir, "x-app-2026-07-15.log")));
            Assert.True(File.Exists(Path.Combine(dir, "x-audit-2026-07-16.jsonl")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ICal_parser_reads_utc_tzid_and_folded_lines()
    {
        var ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:1\r\nDTSTART:20261016T230000Z\r\nSUMMARY:Varsity Football vs. Cent\r\n ral\r\nLOCATION:Home\r\nEND:VEVENT\r\n" +
                  "BEGIN:VEVENT\r\nUID:2\r\nDTSTART;TZID=America/New_York:20261023T180000\r\nSUMMARY:JV at Riverside\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var games = ICalFeedCalendarProvider.Parse(ics, TimeService.ZoneFor("America/New_York"));
        Assert.Equal(2, games.Count);
        Assert.Equal(new DateTime(2026, 10, 16, 23, 0, 0, DateTimeKind.Utc), games[0].StartsAt);
        Assert.Equal("Varsity Football vs. Central", games[0].Title);
        Assert.Equal(new DateTime(2026, 10, 23, 22, 0, 0), games[1].StartsAt); // 6 PM EDT = 22:00 UTC
        Assert.False(games[1].Home);
    }

    [Fact]
    public void Theme_css_contains_only_settings_driven_values()
    {
        var css = ThemeService.BuildCss(new SiteSettings { Colors = new BrandColors { Primary = "#123456" } });
        Assert.Contains("--color-primary: #123456;", css);
        Assert.Contains("--font-display: 'Oswald'", css);
        Assert.Contains("/fonts/oswald-700.woff2", css);
    }

    [Fact]
    public void No_school_specific_values_in_src()
    {
        var src = Path.Combine(TestPaths.RepoRoot, "src");
        var banned = new[] { "Wildcat", "Mountain View", "wildcatsnacks", "861F41", "E5751F" };
        var hits = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}data{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}backups{Path.DirectorySeparatorChar}") &&
                        !f.EndsWith(".woff2"))
            .SelectMany(f => banned.Where(b => File.ReadAllText(f).Contains(b, StringComparison.OrdinalIgnoreCase)).Select(b => $"{f}: {b}"))
            .ToList();
        Assert.Empty(hits);
    }
}

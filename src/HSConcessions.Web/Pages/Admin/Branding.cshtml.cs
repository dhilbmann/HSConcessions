using System.Text.Json;
using HSConcessions.Web.Models;
using HSConcessions.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HSConcessions.Web.Pages.Admin;

/// <summary>Portable branding block for export / import between instances. Images travel as data URIs.</summary>
public sealed class BrandingExport
{
    public int Version { get; set; } = 1;
    public BrandColors Colors { get; set; } = new();
    public BrandFonts Fonts { get; set; } = new();
    public DisplayText Text { get; set; } = new();
    public string? LogoDataUri { get; set; }
    public string? MascotDataUri { get; set; }
    public string? TvBackgroundDataUri { get; set; }
}

public sealed class BrandingModel : AdminPageModel
{
    protected override string? RequiredFunction => Functions.EditSettings;

    public sealed class Input
    {
        public BrandColors Colors { get; set; } = new();
        public bool LowSameAsAccent { get; set; }
        public BrandFonts Fonts { get; set; } = new();
        public DisplayText Text { get; set; } = new();
    }

    [BindProperty] public Input Form { get; set; } = new();
    public SiteView Site { get; private set; } = null!;

    public void OnGet()
    {
        var s = Settings;
        Form = new Input
        {
            Colors = s.Colors,
            LowSameAsAccent = string.IsNullOrWhiteSpace(s.Colors.LowBadge),
            Fonts = s.Fonts,
            Text = s.Text,
        };
        if (Form.LowSameAsAccent) Form.Colors.LowBadge = s.Colors.Accent;
        Site = PublicStateService.Site(s);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var c = Form.Colors;
        foreach (var (label, value) in new[] { ("Primary", c.Primary), ("Accent", c.Accent), ("Dark", c.Dark), ("Light", c.Light), ("Stamp", c.Stamp) })
            if (!ColorUtil.IsHex(value)) throw new ValidationException($"{label} color must be a hex value like #1F3A5F.");
        if (!Form.LowSameAsAccent && !ColorUtil.IsHex(c.LowBadge)) throw new ValidationException("Low badge color must be a hex value.");
        if (BundledFonts.Display.All(f => f.Family != Form.Fonts.Display)) throw new ValidationException("Choose a display font from the list.");
        if (BundledFonts.Body.All(f => f.Family != Form.Fonts.Body)) throw new ValidationException("Choose a body font from the list.");

        SiteSettings? before = null;
        await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s =>
        {
            before = Clone(s);
            s.Colors = new BrandColors
            {
                Primary = c.Primary.ToUpperInvariant(), Accent = c.Accent.ToUpperInvariant(), Dark = c.Dark.ToUpperInvariant(),
                Light = c.Light.ToUpperInvariant(), Stamp = c.Stamp.ToUpperInvariant(),
                LowBadge = Form.LowSameAsAccent ? null : c.LowBadge!.ToUpperInvariant(),
            };
            s.Fonts = new BrandFonts { Display = Form.Fonts.Display, Body = Form.Fonts.Body };
            s.Text = CleanText(Form.Text);
        });
        await Audit.LogAsync("UpdateBranding", "Settings", "branding", "Branding", SettingsDiff.Diff(before!, Store.Read<SiteSettings>(DataFileNames.Settings)));
        return Done("Branding saved. Phones and TVs pick it up on their next refresh.");
    }

    public async Task<IActionResult> OnPostUploadAsync(string kind, IFormFile? file)
    {
        if (file is null || file.Length == 0) throw new ValidationException("Choose a file.");
        if (file.Length > ImageService.MaxUploadBytes) throw new ValidationException("Image is larger than 15 MB.");
        var images = Svc<ImageService>();
        await using var stream = file.OpenReadStream();
        string? before = null, after;
        switch (kind)
        {
            case "logo":
                after = await images.SaveLogoAsync(stream, file.ContentType);
                var fav = Settings.Images.FaviconUploaded ? null : await images.GenerateFaviconsFromFileAsync(Path.Combine(images.ImagesPath, after));
                await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s =>
                {
                    before = s.Images.Logo;
                    s.Images.Logo = after;
                    if (fav is not null) s.Images.Favicon = fav;
                });
                break;
            case "mascot":
                after = await images.SaveBrandImageAsync(stream, file.ContentType, "mascot", 1200);
                await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s => { before = s.Images.Mascot; s.Images.Mascot = after; });
                break;
            case "tvBackground":
                after = await images.SaveBrandImageAsync(stream, file.ContentType, "tvbg", 1920);
                await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s => { before = s.Images.TvBackground; s.Images.TvBackground = after; });
                break;
            case "favicon":
                after = await images.SaveFaviconSourceAsync(stream, file.ContentType);
                await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s => { before = s.Images.Favicon; s.Images.Favicon = after; s.Images.FaviconUploaded = true; });
                break;
            default:
                throw new ValidationException("Unknown image type.");
        }
        await Audit.LogAsync("UpdateBranding", "Settings", "images." + kind, "Branding image: " + kind, new { from = before, to = after });
        return Done("Image updated.");
    }

    public async Task<IActionResult> OnPostRemoveAsync(string kind)
    {
        var images = Svc<ImageService>();
        string? before = null;
        string? regenerated = null;
        if (kind is "logo" or "favicon")
        {
            // Fall back to favicons generated from the remaining logo (or the bundled placeholder).
            var logo = kind == "logo" ? null : Settings.Images.Logo;
            var path = logo is not null ? Path.Combine(images.ImagesPath, logo) : Path.Combine(Svc<IWebHostEnvironment>().WebRootPath, "img", "placeholder-logo.svg");
            if (kind == "favicon" || !Settings.Images.FaviconUploaded) regenerated = await images.GenerateFaviconsFromFileAsync(path);
        }
        await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s =>
        {
            switch (kind)
            {
                case "logo": before = s.Images.Logo; s.Images.Logo = null; if (regenerated is not null) s.Images.Favicon = regenerated; break;
                case "mascot": before = s.Images.Mascot; s.Images.Mascot = null; break;
                case "tvBackground": before = s.Images.TvBackground; s.Images.TvBackground = null; break;
                case "favicon": before = s.Images.Favicon; s.Images.Favicon = regenerated; s.Images.FaviconUploaded = false; break;
                default: throw new ValidationException("Unknown image type.");
            }
        });
        await Audit.LogAsync("UpdateBranding", "Settings", "images." + kind, "Branding image removed: " + kind, new { from = before, to = (string?)null });
        return Done("Image removed.");
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        SiteSettings? before = null;
        var defaults = new SiteSettings();
        await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s =>
        {
            before = Clone(s);
            s.Colors = defaults.Colors;
            s.Fonts = defaults.Fonts;
            s.Text = defaults.Text;
        });
        await Audit.LogAsync("UpdateBranding", "Settings", "branding", "Branding reset to defaults", SettingsDiff.Diff(before!, Store.Read<SiteSettings>(DataFileNames.Settings)));
        return Done("Colors, fonts, and text reset to the defaults. Uploaded images were kept.");
    }

    public IActionResult OnGetExport()
    {
        var s = Settings;
        var images = Svc<ImageService>();
        string? DataUri(string? file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            var path = Path.Combine(images.ImagesPath, file);
            if (!System.IO.File.Exists(path)) return null;
            var mime = file.EndsWith(".svg") ? "image/svg+xml" : file.EndsWith(".png") ? "image/png" : "image/webp";
            return $"data:{mime};base64,{Convert.ToBase64String(System.IO.File.ReadAllBytes(path))}";
        }
        var export = new BrandingExport
        {
            Colors = s.Colors, Fonts = s.Fonts, Text = s.Text,
            LogoDataUri = DataUri(s.Images.Logo), MascotDataUri = DataUri(s.Images.Mascot), TvBackgroundDataUri = DataUri(s.Images.TvBackground),
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(export, JsonStore.JsonOptions);
        return File(json, "application/json", $"branding-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    public async Task<IActionResult> OnPostImportAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) throw new ValidationException("Choose a branding JSON file.");
        if (file.Length > 50 * 1024 * 1024) throw new ValidationException("That file is too large.");
        BrandingExport? import;
        try
        {
            await using var s = file.OpenReadStream();
            import = await JsonSerializer.DeserializeAsync<BrandingExport>(s, JsonStore.JsonOptions);
        }
        catch (JsonException) { throw new ValidationException("That isn't a valid branding file."); }
        if (import is null) throw new ValidationException("That isn't a valid branding file.");

        var c = import.Colors;
        if (!new[] { c.Primary, c.Accent, c.Dark, c.Light, c.Stamp }.All(ColorUtil.IsHex) || (!string.IsNullOrEmpty(c.LowBadge) && !ColorUtil.IsHex(c.LowBadge)))
            throw new ValidationException("The branding file has invalid colors.");
        var display = BundledFonts.Display.Any(f => f.Family == import.Fonts.Display) ? import.Fonts.Display : new BrandFonts().Display;
        var body = BundledFonts.Body.Any(f => f.Family == import.Fonts.Body) ? import.Fonts.Body : new BrandFonts().Body;

        var images = Svc<ImageService>();
        async Task<string?> FromDataUri(string? uri, Func<Stream, Task<string>> save)
        {
            if (string.IsNullOrEmpty(uri)) return null;
            var comma = uri.IndexOf(',');
            if (!uri.StartsWith("data:") || comma < 0 || !uri[..comma].EndsWith(";base64")) throw new ValidationException("The branding file has an invalid image.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(uri[(comma + 1)..]); }
            catch (FormatException) { throw new ValidationException("The branding file has an invalid image."); }
            return await save(new MemoryStream(bytes));
        }
        var logo = await FromDataUri(import.LogoDataUri, st => images.SaveLogoAsync(st, null));
        var mascot = await FromDataUri(import.MascotDataUri, st => images.SaveBrandImageAsync(st, null, "mascot", 1200));
        var tvbg = await FromDataUri(import.TvBackgroundDataUri, st => images.SaveBrandImageAsync(st, null, "tvbg", 1920));
        var fav = logo is not null && !Settings.Images.FaviconUploaded ? await images.GenerateFaviconsFromFileAsync(Path.Combine(images.ImagesPath, logo)) : null;

        SiteSettings? before = null;
        await Store.UpdateAsync<SiteSettings>(DataFileNames.Settings, s =>
        {
            before = Clone(s);
            s.Colors = new BrandColors { Primary = c.Primary, Accent = c.Accent, Dark = c.Dark, Light = c.Light, Stamp = c.Stamp, LowBadge = string.IsNullOrEmpty(c.LowBadge) ? null : c.LowBadge };
            s.Fonts = new BrandFonts { Display = display, Body = body };
            s.Text = CleanText(import.Text ?? new DisplayText());
            if (logo is not null) s.Images.Logo = logo;
            if (mascot is not null) s.Images.Mascot = mascot;
            if (tvbg is not null) s.Images.TvBackground = tvbg;
            if (fav is not null) s.Images.Favicon = fav;
        });
        await Audit.LogAsync("UpdateBranding", "Settings", "branding", "Branding imported", SettingsDiff.Diff(before!, Store.Read<SiteSettings>(DataFileNames.Settings)));
        return Done("Branding imported.");
    }

    private static SiteSettings Clone(SiteSettings s) =>
        JsonSerializer.Deserialize<SiteSettings>(JsonSerializer.Serialize(s, JsonStore.JsonOptions), JsonStore.JsonOptions)!;

    private static DisplayText CleanText(DisplayText t)
    {
        var d = new DisplayText();
        static string V(string? v, string fallback, int max) => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim()[..Math.Min(v.Trim().Length, max)];
        return new DisplayText
        {
            Closed = V(t.Closed, d.Closed, 120),
            OpeningAt = V(t.OpeningAt, d.OpeningAt, 120),
            OpeningSoon = V(t.OpeningSoon, d.OpeningSoon, 120),
            SoldOutStamp = V(t.SoldOutStamp, d.SoldOutStamp, 20),
            LowBadge = V(t.LowBadge, d.LowBadge, 24),
            SingleStandBanner = V(t.SingleStandBanner, d.SingleStandBanner, 160),
            NoStandsOpen = V(t.NoStandsOpen, d.NoStandsOpen, 160),
            Footer = (t.Footer ?? "").Trim(),
        };
    }
}

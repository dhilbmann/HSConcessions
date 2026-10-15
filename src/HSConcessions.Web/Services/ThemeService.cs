using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HSConcessions.Web.Models;

namespace HSConcessions.Web.Services;

/// <summary>Bundled, self-hosted fonts (wwwroot/fonts). Settings choose from this list.</summary>
public static class BundledFonts
{
    public sealed record FontInfo(string Family, string File, int[] Weights, string Fallback);

    public static readonly FontInfo[] Display =
    {
        new("Oswald", "oswald", new[] { 400, 700 }, "'Arial Narrow', Arial, sans-serif"),
        new("Bebas Neue", "bebas-neue", new[] { 400 }, "'Arial Narrow', Arial, sans-serif"),
        new("Anton", "anton", new[] { 400 }, "Impact, 'Arial Narrow', sans-serif"),
        new("Roboto Condensed", "roboto-condensed", new[] { 400, 700 }, "'Arial Narrow', Arial, sans-serif"),
    };

    public static readonly FontInfo[] Body =
    {
        new("Inter", "inter", new[] { 400, 700 }, "system-ui, -apple-system, 'Segoe UI', Arial, sans-serif"),
        new("Roboto", "roboto", new[] { 400, 700 }, "system-ui, -apple-system, 'Segoe UI', Arial, sans-serif"),
        new("Open Sans", "open-sans", new[] { 400, 700 }, "system-ui, -apple-system, 'Segoe UI', Arial, sans-serif"),
    };

    public static FontInfo? Find(string? family) =>
        Display.Concat(Body).FirstOrDefault(f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Hex color math: parse, mix, WCAG contrast.</summary>
public static class ColorUtil
{
    public static bool IsHex(string? s) => s is not null && Regex.IsMatch(s, "^#([0-9a-fA-F]{6})$");

    public static string Normalize(string? s, string fallback) => IsHex(s) ? s!.ToUpperInvariant() : fallback;

    public static (double r, double g, double b) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        return (int.Parse(h[..2], NumberStyles.HexNumber), int.Parse(h[2..4], NumberStyles.HexNumber), int.Parse(h[4..6], NumberStyles.HexNumber));
    }

    public static string Hex(double r, double g, double b) =>
        $"#{Clamp(r):X2}{Clamp(g):X2}{Clamp(b):X2}";

    private static int Clamp(double v) => (int)Math.Round(Math.Clamp(v, 0, 255));

    /// <summary>Mix <paramref name="a"/> toward <paramref name="b"/> by <paramref name="t"/> (0..1).</summary>
    public static string Mix(string a, string b, double t)
    {
        var (r1, g1, b1) = Rgb(a);
        var (r2, g2, b2) = Rgb(b);
        return Hex(r1 + (r2 - r1) * t, g1 + (g2 - g1) * t, b1 + (b2 - b1) * t);
    }

    public static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Ch(double c) { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(r) + 0.7152 * Ch(g) + 0.0722 * Ch(b);
    }

    public static double Contrast(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Whichever of light/dark reads better on <paramref name="bg"/>.</summary>
    public static string ReadableOn(string bg, string light, string dark) =>
        Contrast(bg, light) >= Contrast(bg, dark) ? light : dark;
}

/// <summary>Generates /theme.css and /manifest.json from settings.</summary>
public sealed class ThemeService(JsonStore store)
{
    public (string css, string etag) ThemeCss()
    {
        var s = store.Read<SiteSettings>(DataFileNames.Settings);
        var css = BuildCss(s);
        return (css, ETag(css));
    }

    public static string ETag(string content) =>
        "\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..20].ToLowerInvariant() + "\"";

    public static string BuildCss(SiteSettings s)
    {
        var primary = ColorUtil.Normalize(s.Colors.Primary, "#1F3A5F");
        var accent = ColorUtil.Normalize(s.Colors.Accent, "#F2A900");
        var dark = ColorUtil.Normalize(s.Colors.Dark, "#000000");
        var light = ColorUtil.Normalize(s.Colors.Light, "#FFFFFF");
        var stamp = ColorUtil.Normalize(s.Colors.Stamp, "#C8102E");
        var low = ColorUtil.Normalize(string.IsNullOrWhiteSpace(s.Colors.LowBadge) ? accent : s.Colors.LowBadge, accent);

        var display = BundledFonts.Find(s.Fonts.Display) ?? BundledFonts.Display[0];
        var body = BundledFonts.Find(s.Fonts.Body) ?? BundledFonts.Body[0];

        var sb = new StringBuilder();
        foreach (var font in new[] { display, body }.Distinct())
        {
            foreach (var w in font.Weights)
            {
                sb.Append("@font-face{font-family:'").Append(font.Family).Append("';font-style:normal;font-weight:").Append(w)
                  .Append(";font-display:swap;src:url('/fonts/").Append(font.File).Append('-').Append(w).Append(".woff2') format('woff2');}\n");
            }
        }

        var tvBg = string.IsNullOrEmpty(s.Images.TvBackground) ? "none" : $"url('{ImageService.FileUrl(s.Images.TvBackground)}')";

        sb.Append(":root{\n");
        Var(sb, "color-primary", primary);
        Var(sb, "color-primary-dark", ColorUtil.Mix(primary, "#000000", 0.35));
        Var(sb, "color-primary-light", ColorUtil.Mix(primary, "#FFFFFF", 0.85));
        Var(sb, "color-on-primary", ColorUtil.ReadableOn(primary, light, dark));
        Var(sb, "color-accent", accent);
        Var(sb, "color-accent-light", ColorUtil.Mix(accent, "#FFFFFF", 0.75));
        Var(sb, "color-accent-dark", ColorUtil.Mix(accent, "#000000", 0.3));
        Var(sb, "color-on-accent", ColorUtil.ReadableOn(accent, light, dark));
        Var(sb, "color-dark", dark);
        Var(sb, "color-light", light);
        Var(sb, "color-stamp", stamp);
        Var(sb, "color-low", low);
        Var(sb, "color-on-low", ColorUtil.ReadableOn(low, light, dark));
        Var(sb, "font-display", $"'{display.Family}', {display.Fallback}");
        Var(sb, "font-body", $"'{body.Family}', {body.Fallback}");
        Var(sb, "tv-background-image", tvBg);
        // Public page surface colors (light scheme).
        Var(sb, "page-bg", ColorUtil.Mix(light, primary, 0.04));
        Var(sb, "page-surface", light);
        Var(sb, "page-text", ColorUtil.Mix(dark, light, 0.1));
        Var(sb, "page-muted", ColorUtil.Mix(dark, light, 0.45));
        Var(sb, "page-border", ColorUtil.Mix(light, dark, 0.12));
        Var(sb, "page-price", PriceOnLight(accent, primary, light, dark));
        sb.Append("}\n");

        // Dark mode derived from the same palette.
        sb.Append("@media (prefers-color-scheme: dark){:root:not(.force-light){\n");
        Var(sb, "page-bg", ColorUtil.Mix(dark, primary, 0.12));
        Var(sb, "page-surface", ColorUtil.Mix(dark, primary, 0.22));
        Var(sb, "page-text", ColorUtil.Mix(light, dark, 0.06));
        Var(sb, "page-muted", ColorUtil.Mix(light, dark, 0.4));
        Var(sb, "page-border", ColorUtil.Mix(dark, light, 0.18));
        Var(sb, "page-price", ColorUtil.Contrast(accent, dark) >= 3 ? accent : ColorUtil.Mix(accent, light, 0.45));
        Var(sb, "color-primary-light", ColorUtil.Mix(primary, dark, 0.6));
        sb.Append("}}\n");
        return sb.ToString();
    }

    /// <summary>Accent if it reads on the light page; otherwise primary; otherwise a darkened accent.</summary>
    public static string PriceOnLight(string accent, string primary, string light, string dark) =>
        ColorUtil.Contrast(accent, light) >= 3 ? accent
        : ColorUtil.Contrast(primary, light) >= 4.5 ? primary
        : ColorUtil.Mix(accent, dark, 0.45);

    private static void Var(StringBuilder sb, string name, string value) =>
        sb.Append("  --").Append(name).Append(": ").Append(value).Append(";\n");

    public string Manifest()
    {
        var s = store.Read<SiteSettings>(DataFileNames.Settings);
        var icons = new List<object>();
        if (s.Images.Favicon is { } fav)
        {
            icons.Add(new { src = $"/images/{fav}-192.png", sizes = "192x192", type = "image/png" });
            icons.Add(new { src = $"/images/{fav}-512.png", sizes = "512x512", type = "image/png" });
        }
        var manifest = new
        {
            name = s.SiteTitle,
            short_name = s.SiteShortName,
            start_url = "/",
            display = "standalone",
            background_color = ColorUtil.Normalize(s.Colors.Light, "#FFFFFF"),
            theme_color = ColorUtil.Normalize(s.Colors.Primary, "#1F3A5F"),
            icons,
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Contrast pairs checked by the branding editor.</summary>
    public static IEnumerable<(string label, string fg, string bg, double ratio)> ContrastPairs(BrandColors c)
    {
        var low = string.IsNullOrWhiteSpace(c.LowBadge) ? c.Accent : c.LowBadge;
        var pairs = new (string, string, string)[]
        {
            ("Light text on primary", c.Light, c.Primary),
            ("Light text on dark", c.Light, c.Dark),
            ("Accent price on dark", c.Accent, c.Dark),
            ("Dark text on light", c.Dark, c.Light),
            ("Badge text on low badge", ColorUtil.ReadableOn(low!, c.Light, c.Dark), low!),
        };
        foreach (var (label, fg, bg) in pairs)
            if (ColorUtil.IsHex(fg) && ColorUtil.IsHex(bg))
                yield return (label, fg, bg, ColorUtil.Contrast(fg, bg));
    }
}

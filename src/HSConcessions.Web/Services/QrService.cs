using HSConcessions.Web.Models;
using QRCoder;
using SkiaSharp;

namespace HSConcessions.Web.Services;

/// <summary>QR codes for the site base URL and each stand slug, built from settings.siteBaseUrl.</summary>
public sealed class QrService(JsonStore store)
{
    public sealed record QrTarget(string Key, string Label, string Url);

    public List<QrTarget> Targets()
    {
        var settings = store.Read<SiteSettings>(DataFileNames.Settings);
        var baseUrl = settings.SiteBaseUrl.TrimEnd('/');
        var list = new List<QrTarget> { new("site", "Whole site", baseUrl + "/") };
        foreach (var s in store.Read<StandsFile>(DataFileNames.Stands).Stands.Where(s => !s.Archived).OrderBy(s => s.SortOrder))
            list.Add(new QrTarget(s.Slug, s.Name + " stand", $"{baseUrl}/{s.Slug}"));
        return list;
    }

    public QrTarget? Find(string key) =>
        Targets().FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Exactly 1024×1024 PNG.</summary>
    public static byte[] Png(string url, int size = 1024)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var modules = data.ModuleMatrix.Count;
        var ppm = Math.Max(1, size / modules);
        var raw = new PngByteQRCode(data).GetGraphic(ppm);
        using var bmp = SKBitmap.Decode(raw);
        var dst = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Clear(SKColors.White);
            using var img = SKImage.FromBitmap(bmp);
            // Nearest-neighbour scaling keeps module edges crisp.
            canvas.DrawImage(img, new SKRect(0, 0, size, size), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }
        using var outImg = SKImage.FromBitmap(dst);
        using var enc = outImg.Encode(SKEncodedImageFormat.Png, 100);
        dst.Dispose();
        return enc.ToArray();
    }

    public static string Svg(string url)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        return new SvgQRCode(data).GetGraphic(16);
    }
}

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HSConcessions.Web.Models;
using SkiaSharp;

namespace HSConcessions.Web.Services;

public sealed class ImageValidationException(string message) : Exception(message);

/// <summary>
/// Validates, re-encodes and stores images under content-hashed names. Originals are never kept or served.
/// </summary>
public sealed class ImageService
{
    public const long MaxUploadBytes = 15 * 1024 * 1024;
    private const int CatalogMaxEdge = 800;
    private const int ThumbEdge = 300;
    private static readonly int[] FaviconSizes = { 16, 32, 180, 192, 512 };

    private readonly string _imagesPath;

    public ImageService(StorageOptions storage)
    {
        _imagesPath = Path.GetFullPath(storage.ImagesPath);
        Directory.CreateDirectory(_imagesPath);
    }

    public string ImagesPath => _imagesPath;

    public static string CatalogUrl(string? imageId) => imageId is null ? "" : $"/images/{imageId}.webp";
    public static string ThumbUrl(string? imageId) => imageId is null ? "" : $"/images/{imageId}-{ThumbEdge}.webp";
    public static string FileUrl(string? fileName) => string.IsNullOrEmpty(fileName) ? "" : $"/images/{fileName}";

    // ---------- catalog pictures ----------

    /// <summary>Auto-orient, resize to 800px max edge, WebP q80, plus a 300px thumbnail. Returns the image id.</summary>
    public async Task<string> SaveCatalogImageAsync(Stream upload, string? contentType)
    {
        var bytes = await ReadLimitedAsync(upload);
        var kind = Sniff(bytes);
        if (kind is not ("jpeg" or "png" or "webp")) throw new ImageValidationException("Upload a JPEG, PNG, or WebP image.");
        CheckDeclaredType(contentType, kind);

        using var oriented = DecodeOriented(bytes);
        using var main = ResizeToFit(oriented, CatalogMaxEdge);
        var mainBytes = EncodeWebp(main, 80);
        var id = "img-" + Hash(mainBytes);
        var mainPath = Path.Combine(_imagesPath, id + ".webp");
        if (!File.Exists(mainPath))
        {
            using var thumb = ResizeToFit(oriented, ThumbEdge);
            await WriteFileAtomic(Path.Combine(_imagesPath, $"{id}-{ThumbEdge}.webp"), EncodeWebp(thumb, 80));
            await WriteFileAtomic(mainPath, mainBytes);
        }
        return id;
    }

    // ---------- branding ----------

    /// <summary>Logo: PNG/WebP re-encoded to PNG (max 1024px); SVG sanitized. Returns the stored file name.</summary>
    public async Task<string> SaveLogoAsync(Stream upload, string? contentType)
    {
        var bytes = await ReadLimitedAsync(upload);
        var kind = Sniff(bytes);
        if (kind == "svg")
        {
            var clean = SanitizeSvg(bytes);
            var name = "logo-" + Hash(clean) + ".svg";
            await WriteFileAtomic(Path.Combine(_imagesPath, name), clean);
            return name;
        }
        if (kind is not ("png" or "webp" or "jpeg")) throw new ImageValidationException("Logo must be PNG, SVG, or WebP.");
        CheckDeclaredType(contentType, kind);
        using var bmp = DecodeOriented(bytes);
        using var sized = ResizeToFit(bmp, 1024);
        var png = EncodePng(sized);
        var file = "logo-" + Hash(png) + ".png";
        await WriteFileAtomic(Path.Combine(_imagesPath, file), png);
        return file;
    }

    /// <summary>Mascot / TV background: re-encoded to WebP (keeps transparency).</summary>
    public async Task<string> SaveBrandImageAsync(Stream upload, string? contentType, string prefix, int maxEdge)
    {
        var bytes = await ReadLimitedAsync(upload);
        var kind = Sniff(bytes);
        if (kind is not ("jpeg" or "png" or "webp")) throw new ImageValidationException("Upload a JPEG, PNG, or WebP image.");
        CheckDeclaredType(contentType, kind);
        using var bmp = DecodeOriented(bytes);
        using var sized = ResizeToFit(bmp, maxEdge);
        var webp = EncodeWebp(sized, 85);
        var file = $"{prefix}-{Hash(webp)}.webp";
        await WriteFileAtomic(Path.Combine(_imagesPath, file), webp);
        return file;
    }

    /// <summary>Uploaded favicon source image (PNG/WebP/JPEG/SVG) → favicon set. Returns the set's base name.</summary>
    public async Task<string> SaveFaviconSourceAsync(Stream upload, string? contentType)
    {
        var bytes = await ReadLimitedAsync(upload);
        var kind = Sniff(bytes);
        if (kind == "svg") return await GenerateFaviconsFromBitmapAsync(RasterizeSvg(SanitizeSvg(bytes), 512));
        if (kind is not ("png" or "webp" or "jpeg")) throw new ImageValidationException("Favicon must be PNG, SVG, or WebP.");
        CheckDeclaredType(contentType, kind);
        return await GenerateFaviconsFromBitmapAsync(DecodeOriented(bytes));
    }

    /// <summary>Generates 16/32/180/192/512 PNGs and favicon.ico from a stored logo (or bundled placeholder file).</summary>
    public async Task<string> GenerateFaviconsFromFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var bmp = Sniff(bytes) == "svg" ? RasterizeSvg(bytes, 512) : DecodeOriented(bytes);
        return await GenerateFaviconsFromBitmapAsync(bmp);
    }

    private async Task<string> GenerateFaviconsFromBitmapAsync(SKBitmap source)
    {
        using (source)
        {
            using var square = PadToSquare(source);
            var pngs = new Dictionary<int, byte[]>();
            foreach (var size in FaviconSizes)
            {
                using var resized = Resize(square, size, size);
                pngs[size] = EncodePng(resized);
            }
            var baseName = "fav-" + Hash(pngs[512]);
            foreach (var (size, data) in pngs)
                await WriteFileAtomic(Path.Combine(_imagesPath, $"{baseName}-{size}.png"), data);
            await WriteFileAtomic(Path.Combine(_imagesPath, baseName + ".ico"), BuildIco(pngs[16], pngs[32]));
            return baseName;
        }
    }

    // ---------- helpers ----------

    public static string? Sniff(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        var head = System.Text.Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 1024)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (head.StartsWith("<!--") && head.Contains("<svg", StringComparison.OrdinalIgnoreCase)))
            return "svg";
        return null;
    }

    private static void CheckDeclaredType(string? contentType, string kind)
    {
        if (string.IsNullOrEmpty(contentType) || contentType == "application/octet-stream") return;
        var ok = kind switch
        {
            "jpeg" => contentType is "image/jpeg" or "image/jpg" or "image/pjpeg",
            "png" => contentType == "image/png",
            "webp" => contentType == "image/webp",
            _ => true,
        };
        if (!ok) throw new ImageValidationException("The file type does not match its contents.");
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream s)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await s.ReadAsync(buffer)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > MaxUploadBytes) throw new ImageValidationException("Image is larger than 15 MB.");
        }
        if (ms.Length == 0) throw new ImageValidationException("The file is empty.");
        return ms.ToArray();
    }

    /// <summary>Decodes and applies the EXIF orientation so the stored image is upright.</summary>
    private static SKBitmap DecodeOriented(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new ImageValidationException("The image could not be read.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 80_000_000)
            throw new ImageValidationException("The image dimensions are not supported.");
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bmp.Dispose();
            throw new ImageValidationException("The image could not be decoded.");
        }
        return ApplyOrigin(bmp, codec.EncodedOrigin);
    }

    private static SKBitmap ApplyOrigin(SKBitmap bmp, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return bmp;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w = swap ? bmp.Height : bmp.Width;
        var h = swap ? bmp.Width : bmp.Height;
        var rotated = new SKBitmap(new SKImageInfo(w, h, bmp.ColorType, bmp.AlphaType));
        using (var canvas = new SKCanvas(rotated))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, w / 2f, 0); break;
                case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, w / 2f, h / 2f); break;
                case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, h / 2f); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(w, h); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, h); canvas.RotateDegrees(270); break;
            }
            using var img = SKImage.FromBitmap(bmp);
            canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        bmp.Dispose();
        return rotated;
    }

    private static SKBitmap ResizeToFit(SKBitmap src, int maxEdge)
    {
        var longEdge = Math.Max(src.Width, src.Height);
        if (longEdge <= maxEdge) return src.Copy();
        var scale = (double)maxEdge / longEdge;
        return Resize(src, Math.Max(1, (int)Math.Round(src.Width * scale)), Math.Max(1, (int)Math.Round(src.Height * scale)));
    }

    private static SKBitmap Resize(SKBitmap src, int w, int h)
    {
        var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);
        using var img = SKImage.FromBitmap(src);
        canvas.DrawImage(img, new SKRect(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return dst;
    }

    private static SKBitmap PadToSquare(SKBitmap src)
    {
        var size = Math.Max(src.Width, src.Height);
        var dst = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);
        using var img = SKImage.FromBitmap(src);
        canvas.DrawImage(img, (size - src.Width) / 2f, (size - src.Height) / 2f, new SKSamplingOptions(SKFilterMode.Nearest));
        return dst;
    }

    private static byte[] EncodeWebp(SKBitmap bmp, int quality)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Webp, quality);
        return data.ToArray();
    }

    private static byte[] EncodePng(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKBitmap RasterizeSvg(byte[] svgBytes, int size)
    {
        using var ms = new MemoryStream(svgBytes);
        using var svg = new Svg.Skia.SKSvg();
        var picture = svg.Load(ms) ?? throw new ImageValidationException("The SVG could not be rendered.");
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ImageValidationException("The SVG has no size.");
        var scale = size / Math.Max(bounds.Width, bounds.Height);
        var w = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var h = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        return bmp;
    }

    private static readonly HashSet<string> ForbiddenSvgElements = new(StringComparer.OrdinalIgnoreCase)
        { "script", "foreignObject", "iframe", "object", "embed", "audio", "video", "handler", "listener", "set", "animate" };

    /// <summary>Removes scripts, event handlers, and any external reference from an SVG.</summary>
    public static byte[] SanitizeSvg(byte[] bytes)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new ImageValidationException("The SVG is not valid XML (DTDs are not allowed).");
        }
        if (doc.Root is null || doc.Root.Name.LocalName != "svg") throw new ImageValidationException("Not an SVG image.");

        foreach (var el in doc.Descendants().Where(e => ForbiddenSvgElements.Contains(e.Name.LocalName)).ToList())
            el.Remove();

        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "style").ToList())
            if (Regex.IsMatch(style.Value, @"@import|url\(\s*['""]?(?!#)", RegexOptions.IgnoreCase))
                style.Remove();

        foreach (var el in doc.Root.DescendantsAndSelf())
        {
            foreach (var attr in el.Attributes().ToList())
            {
                var name = attr.Name.LocalName;
                var value = attr.Value.Trim();
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase)) { attr.Remove(); continue; }
                if (name is "href" or "src")
                {
                    var safe = value.StartsWith('#') ||
                               Regex.IsMatch(value, @"^data:image/(png|jpeg|webp|gif);base64,", RegexOptions.IgnoreCase);
                    if (!safe) attr.Remove();
                    continue;
                }
                if (Regex.IsMatch(value, @"url\(\s*['""]?(?!#)", RegexOptions.IgnoreCase) ||
                    value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
                {
                    attr.Remove();
                }
            }
        }

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings { OmitXmlDeclaration = false, Encoding = new System.Text.UTF8Encoding(false) }))
            doc.Save(writer);
        return ms.ToArray();
    }

    /// <summary>ICO container holding PNG-compressed 16 and 32 px images.</summary>
    private static byte[] BuildIco(params byte[][] pngs)
    {
        var sizes = new[] { 16, 32 };
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)pngs.Length);
        var offset = 6 + 16 * pngs.Length;
        for (var i = 0; i < pngs.Length; i++)
        {
            w.Write((byte)sizes[i]);
            w.Write((byte)sizes[i]);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
        return ms.ToArray();
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..16].ToLowerInvariant();

    private static async Task WriteFileAtomic(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fs.WriteAsync(data);
            fs.Flush(true);
        }
        File.Move(tmp, path, true);
    }
}

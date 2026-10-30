using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace UuDok.WinHelper;

public static class PdfRaster
{
    public static async Task<List<string>> Render(string pdf, string outDir, int dpi)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdf));
        var doc = await PdfDocument.LoadFromFileAsync(file);
        var res = new List<string>();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            using var page = doc.GetPage(i);
            var png = Path.Combine(Path.GetFullPath(outDir), $"page-{i + 1:D3}.png");
            using (var fs = File.Create(png))
            using (var ras = fs.AsRandomAccessStream())
            {
                var opt = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Round(page.Size.Width * dpi / 96.0),
                    BitmapEncoderId = BitmapEncoder.PngEncoderId,
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                };
                await page.RenderToStreamAsync(ras, opt);
            }
            res.Add(png);
        }
        return res;
    }
}

public sealed record OcrWord(string Text, double X, double Y, double W, double H);
public sealed record OcrLine(string Text, double X, double Y, double W, double H, List<OcrWord> Words);
public sealed record OcrResultDto(string Engine, string Language, string Text, int ImageWidth, int ImageHeight, double? Angle, List<OcrLine> Lines,
    List<string> AvailableLanguages);

public sealed record OcrBatchItem(string Id, string Path);
public sealed record OcrBatchResult(string Id, string? Language, List<OcrLine> Lines, bool Agreement, int Width, int Height, string? Error, string? SecondText = null);

/// <summary>Lokal OCR med Windows.Media.Ocr (ingen nettverk). Krever installert OCR-språkpakke.</summary>
public static class WinOcr
{
    /// <summary>
    /// OCR av mange bilder i én prosess. Hvert bilde leses to ganger (original og forstørret variant);
    /// «Agreement» betyr at begge ga samme tekst. Windows-OCR gir ingen sikkerhetsverdi per ord, så dette
    /// samsvaret brukes som ett av flere usikkerhetssignaler.
    /// </summary>
    public static async Task<List<OcrBatchResult>> Batch(string manifest, string? lang)
    {
        var items = System.Text.Json.JsonSerializer.Deserialize<List<OcrBatchItem>>(File.ReadAllText(manifest),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var res = new List<OcrBatchResult>();
        foreach (var it in items)
        {
            try
            {
                var a = await Recognize(it.Path, lang, 1.0);
                var b = await Recognize(it.Path, lang, 1.6);
                static string N(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                res.Add(new OcrBatchResult(it.Id, a.Language, a.Lines, N(a.Text) == N(b.Text), a.ImageWidth, a.ImageHeight, null, b.Text));
            }
            catch (Exception ex) { res.Add(new OcrBatchResult(it.Id, null, [], false, 0, 0, ex.Message)); }
        }
        return res;
    }

    public static Task<OcrResultDto> Recognize(string imagePath, string? lang) => Recognize(imagePath, lang, 1.0);

    public static async Task<OcrResultDto> Recognize(string imagePath, string? lang, double upscale)
    {
        var avail = OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();
        OcrEngine? engine = null;
        foreach (var tag in new[] { lang, "nb-NO", "nb", "no", "nn-NO", "en-US", "en-GB" }.Where(t => t != null))
        {
            var l = new Language(tag!);
            if (OcrEngine.IsLanguageSupported(l)) { engine = OcrEngine.TryCreateFromLanguage(l); if (engine != null) break; }
        }
        engine ??= OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Ingen OCR-språk er installert i Windows.");

        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(imagePath));
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        var dec = await BitmapDecoder.CreateAsync(stream);
        var w = dec.PixelWidth; var h = dec.PixelHeight;
        // OCR-motoren har maks bildestørrelse; skaler ned ved behov.
        var max = OcrEngine.MaxImageDimension;
        var scale = Math.Min(upscale, (double)max / Math.Max(w, h));
        var transform = new BitmapTransform { ScaledWidth = (uint)(w * scale), ScaledHeight = (uint)(h * scale), InterpolationMode = BitmapInterpolationMode.Fant };
        using var bmp = await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var r = await engine.RecognizeAsync(bmp);
        var lines = r.Lines.Select(l =>
        {
            var ws = l.Words.Select(x => new OcrWord(x.Text, x.BoundingRect.X / scale, x.BoundingRect.Y / scale,
                x.BoundingRect.Width / scale, x.BoundingRect.Height / scale)).ToList();
            double x0 = ws.Min(q => q.X), y0 = ws.Min(q => q.Y), x1 = ws.Max(q => q.X + q.W), y1 = ws.Max(q => q.Y + q.H);
            return new OcrLine(l.Text, x0, y0, x1 - x0, y1 - y0, ws);
        }).ToList();
        return new OcrResultDto("Windows.Media.Ocr", engine.RecognizerLanguage.LanguageTag, r.Text, (int)w, (int)h, r.TextAngle, lines, avail);
    }
}

using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace UuDok.Tools;

/// <summary>Syntetiske testbilder. Ingen ekte personer eller ekte undervisningsmateriale.</summary>
public static class Images
{
    public static void WriteAll(string dir)
    {
        Directory.CreateDirectory(dir);
        Save(KiTitleImage("Fotosyntese", "Slik lager plantene næring av sollys", skew: 0), Path.Combine(dir, "ki-tittelbilde.png"));
        Save(KiTitleImage("Vannets kretsløp", "Fordamping, kondensasjon og nedbør", skew: -0.18f), Path.Combine(dir, "ki-tittelbilde-skjev.png"));
        Save(Chart(), Path.Combine(dir, "graf-elever.png"));
        Save(Screenshot(), Path.Combine(dir, "skjermbilde-lagre.png"));
        Save(Qr("https://example.org/uu-test/oppgave-3"), Path.Combine(dir, "qr-oppgave.png"));
        Save(Tiny(), Path.Combine(dir, "lite-uskarpt.png"));
        Save(Divider(), Path.Combine(dir, "dekor-linje.png"));
        Save(Illustration(), Path.Combine(dir, "illustrasjon-celle.png"));
    }

    static void Save(SKBitmap bmp, string path)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        bmp.Dispose();
    }

    static SKFont Font(float size, bool bold = false) =>
        new(SKTypeface.FromFamilyName("Segoe UI", bold ? SKFontStyle.Bold : SKFontStyle.Normal), size);

    /// <summary>Etterligner et KI-generert lysbildebilde med tittel og undertekst bakt inn i pikslene.</summary>
    static SKBitmap KiTitleImage(string title, string subtitle, float skew)
    {
        var bmp = new SKBitmap(1920, 1080);
        using var c = new SKCanvas(bmp);
        using (var bg = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1920, 1080),
                   [new SKColor(0x0B, 0x3D, 0x2E), new SKColor(0x2E, 0x8B, 0x57)], SKShaderTileMode.Clamp) })
            c.DrawRect(0, 0, 1920, 1080, bg);
        using (var sun = new SKPaint { Color = new SKColor(0xFF, 0xD7, 0x4A), IsAntialias = true })
            c.DrawCircle(1600, 230, 140, sun);
        using (var leaf = new SKPaint { Color = new SKColor(0x7C, 0xC5, 0x76), IsAntialias = true })
            for (int i = 0; i < 7; i++) c.DrawOval(new SKRect(200 + i * 210, 760 + (i % 2) * 40, 380 + i * 210, 980), leaf);
        c.Save();
        if (skew != 0) c.Skew(skew, 0);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        c.DrawText(title, 160 - skew * 300, 360, SKTextAlign.Left, Font(150, bold: true), white);
        c.DrawText(subtitle, 165 - skew * 450, 480, SKTextAlign.Left, Font(64), white);
        c.Restore();
        return bmp;
    }

    static SKBitmap Chart()
    {
        var bmp = new SKBitmap(1200, 800);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        using var ax = new SKPaint { Color = SKColors.Black, StrokeWidth = 3, IsAntialias = true };
        using var txt = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        c.DrawLine(120, 680, 1120, 680, ax); c.DrawLine(120, 680, 120, 100, ax);
        c.DrawText("Antall elever per trinn", 600, 70, SKTextAlign.Center, Font(40, true), txt);
        string[] lab = ["8. trinn", "9. trinn", "10. trinn"]; int[] val = [24, 31, 27];
        using var bar = new SKPaint { Color = new SKColor(0x1F, 0x5F, 0xAD) };
        for (int i = 0; i < 3; i++)
        {
            float x = 220 + i * 300, h = val[i] * 16;
            c.DrawRect(x, 680 - h, 180, h, bar);
            c.DrawText(lab[i], x + 90, 730, SKTextAlign.Center, Font(32), txt);
            c.DrawText(val[i].ToString(), x + 90, 670 - h, SKTextAlign.Center, Font(32), txt);
        }
        return bmp;
    }

    static SKBitmap Screenshot()
    {
        var bmp = new SKBitmap(1280, 800);
        using var c = new SKCanvas(bmp);
        c.Clear(new SKColor(0xF3, 0xF3, 0xF3));
        using var bar = new SKPaint { Color = new SKColor(0x2B, 0x57, 0x9A) };
        c.DrawRect(0, 0, 1280, 60, bar);
        using var w = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var k = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        c.DrawText("Fil   Rediger   Vis   Hjelp", 24, 42, SKTextAlign.Left, Font(28), w);
        c.DrawText("Dokument1 – endringer er ikke lagret", 40, 140, SKTextAlign.Left, Font(30), k);
        using var btn = new SKPaint { Color = new SKColor(0xDD, 0xDD, 0xDD) };
        c.DrawRect(40, 200, 200, 70, btn);
        c.DrawText("Lagre", 140, 248, SKTextAlign.Center, Font(32, true), k);
        using var red = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 8, IsAntialias = true };
        c.DrawRoundRect(28, 188, 224, 94, 12, 12, red); // utheving = visuell informasjon
        return bmp;
    }

    static SKBitmap Qr(string text)
    {
        var writer = new ZXing.BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 600, Height = 600, Margin = 2 }
        };
        var pd = writer.Write(text);
        var bmp = new SKBitmap(pd.Width, pd.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(pd.Pixels, 0, bmp.GetPixels(), pd.Pixels.Length);
        return bmp;
    }

    static SKBitmap Tiny()
    {
        var bmp = new SKBitmap(40, 30);
        using var c = new SKCanvas(bmp);
        c.Clear(new SKColor(0x88, 0x99, 0xAA));
        using var p = new SKPaint { Color = SKColors.Black };
        c.DrawText("Oppg", 2, 20, SKTextAlign.Left, Font(10), p);
        return bmp;
    }

    static SKBitmap Divider()
    {
        var bmp = new SKBitmap(1600, 40);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1600, 0),
            [SKColors.Transparent, new SKColor(0xC0, 0x8A, 0x2B), SKColors.Transparent], SKShaderTileMode.Clamp) };
        c.DrawRect(0, 15, 1600, 10, p);
        return bmp;
    }

    static SKBitmap Illustration()
    {
        var bmp = new SKBitmap(1000, 1000);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        using var cell = new SKPaint { Color = new SKColor(0xB5, 0xE3, 0xA8), IsAntialias = true };
        using var nuc = new SKPaint { Color = new SKColor(0x6A, 0x3D, 0x9A), IsAntialias = true };
        using var line = new SKPaint { Color = SKColors.Black, StrokeWidth = 4, Style = SKPaintStyle.Stroke, IsAntialias = true };
        c.DrawRoundRect(100, 150, 800, 700, 120, 120, cell);
        c.DrawRoundRect(100, 150, 800, 700, 120, 120, line);
        c.DrawCircle(500, 500, 130, nuc);
        using var k = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        c.DrawText("Cellekjerne", 700, 120, SKTextAlign.Center, Font(44), k);
        c.DrawLine(640, 135, 560, 400, line);
        c.DrawText("Cellevegg", 230, 940, SKTextAlign.Center, Font(44), k);
        c.DrawLine(230, 900, 160, 800, line);
        return bmp;
    }
}

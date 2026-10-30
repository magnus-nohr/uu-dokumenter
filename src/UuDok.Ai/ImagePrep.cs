using SkiaSharp;
using UuDok.Core;
using ZXing;
using ZXing.Common;

namespace UuDok.Ai;

public sealed record PreparedImage(byte[] DisplayedPng, byte[]? OriginalPng, int Width, int Height, bool CropRemovesMuch);

/// <summary>Lager bildet slik det faktisk vises (beskjæring fra a:srcRect) og nedskalert til rimelig størrelse.</summary>
public static class ImagePrep
{
    public const int MaxSide = 1568;

    public static PreparedImage? Prepare(byte[] imageBytes, Crop? crop)
    {
        using var bmp = SKBitmap.Decode(imageBytes);
        if (bmp == null) return null;                   // f.eks. EMF/WMF – kan ikke tolkes lokalt
        var rect = new SKRectI(0, 0, bmp.Width, bmp.Height);
        bool much = false;
        if (crop != null)
        {
            int l = (int)(bmp.Width * Math.Clamp(crop.L, 0, 100000) / 100000.0), r = (int)(bmp.Width * Math.Clamp(crop.R, 0, 100000) / 100000.0);
            int t = (int)(bmp.Height * Math.Clamp(crop.T, 0, 100000) / 100000.0), b = (int)(bmp.Height * Math.Clamp(crop.B, 0, 100000) / 100000.0);
            if (bmp.Width - l - r > 4 && bmp.Height - t - b > 4) rect = new SKRectI(l, t, bmp.Width - r, bmp.Height - b);
            much = (double)rect.Width * rect.Height / ((double)bmp.Width * bmp.Height) < 0.7;
        }
        using var cropped = new SKBitmap(rect.Width, rect.Height);
        bmp.ExtractSubset(cropped, rect);
        var displayed = Encode(Scale(cropped));
        return new PreparedImage(displayed, much ? Encode(Scale(bmp)) : null, rect.Width, rect.Height, much);
    }

    private static SKBitmap Scale(SKBitmap b)
    {
        var s = Math.Min(1.0, (double)MaxSide / Math.Max(b.Width, b.Height));
        if (s >= 1.0) return b.Copy();
        return b.Resize(new SKImageInfo((int)(b.Width * s), (int)(b.Height * s)), new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    private static byte[] Encode(SKBitmap b)
    {
        using var img = SKImage.FromBitmap(b);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Lokal QR-dekoding. Adressen åpnes aldri.</summary>
    public static string? DecodeQr(byte[] png)
    {
        using var bmp = SKBitmap.Decode(png);
        if (bmp == null) return null;
        var rgb = new byte[bmp.Width * bmp.Height * 3];
        int i = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                // Gjennomsiktig = hvit bakgrunn
                double a = c.Alpha / 255.0;
                rgb[i++] = (byte)(c.Red * a + 255 * (1 - a)); rgb[i++] = (byte)(c.Green * a + 255 * (1 - a)); rgb[i++] = (byte)(c.Blue * a + 255 * (1 - a));
            }
        var reader = new BarcodeReaderGeneric { Options = new DecodingOptions { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true } };
        var res = reader.Decode(new RGBLuminanceSource(rgb, bmp.Width, bmp.Height, RGBLuminanceSource.BitmapFormat.RGB24));
        return res?.Text;
    }
}

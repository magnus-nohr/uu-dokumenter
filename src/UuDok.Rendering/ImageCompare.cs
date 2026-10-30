using SkiaSharp;

namespace UuDok.Rendering;

public sealed record ImageDiff(bool SameSize, int Width, int Height, long DifferentPixels, double DifferentRatio,
    int MaxChannelDelta, (int X, int Y, int W, int H)? BoundingBox);

/// <summary>
/// Toleranser for visuell sammenligning. Kalibreres mot ufarlig renderingsstøy ved å rendre
/// SAMME fil to ganger (se bevis 7 i docs/TEKNISKE-BEVIS.md).
/// </summary>
public sealed record VisualTolerance(int ChannelTolerance = 8, double MaxDifferentRatio = 0.0005)
{
    public static VisualTolerance Default { get; } = new();
    public bool Accepts(ImageDiff d) => d.SameSize && d.DifferentRatio <= MaxDifferentRatio;
}

public static class ImageCompare
{
    public static ImageDiff Compare(string a, string b, int channelTolerance = 8, string? diffOut = null)
    {
        using var ba = SKBitmap.Decode(a) ?? throw new InvalidDataException($"Kan ikke lese {a}");
        using var bb = SKBitmap.Decode(b) ?? throw new InvalidDataException($"Kan ikke lese {b}");
        if (ba.Width != bb.Width || ba.Height != bb.Height)
            return new ImageDiff(false, ba.Width, ba.Height, (long)ba.Width * ba.Height, 1, 255, null);
        long diff = 0; int max = 0; int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        using var outBmp = diffOut != null ? new SKBitmap(ba.Width, ba.Height) : null;
        for (int y = 0; y < ba.Height; y++)
            for (int x = 0; x < ba.Width; x++)
            {
                var ca = ba.GetPixel(x, y); var cb = bb.GetPixel(x, y);
                int d = Math.Max(Math.Max(Math.Abs(ca.Red - cb.Red), Math.Abs(ca.Green - cb.Green)), Math.Max(Math.Abs(ca.Blue - cb.Blue), Math.Abs(ca.Alpha - cb.Alpha)));
                if (d > max) max = d;
                if (d > channelTolerance)
                {
                    diff++;
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                    outBmp?.SetPixel(x, y, SKColors.Red);
                }
                else outBmp?.SetPixel(x, y, new SKColor(ca.Red, ca.Green, ca.Blue, 60));
            }
        if (outBmp != null && diffOut != null)
        {
            using var img = SKImage.FromBitmap(outBmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(diffOut, data.ToArray());
        }
        return new ImageDiff(true, ba.Width, ba.Height, diff, (double)diff / ((long)ba.Width * ba.Height), max,
            x1 < 0 ? null : (x0, y0, x1 - x0 + 1, y1 - y0 + 1));
    }
}

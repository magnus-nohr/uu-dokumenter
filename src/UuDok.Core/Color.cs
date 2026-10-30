using System.Globalization;

namespace UuDok.Core;

/// <summary>WCAG-kontrastberegning (relativ luminans, sRGB).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb? TryParse(string? hex)
    {
        if (hex is null || hex.Length != 6) return null;
        return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? new Rgb((byte)(v >> 16), (byte)(v >> 8 & 0xFF), (byte)(v & 0xFF)) : null;
    }
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    public double Luminance()
    {
        static double Ch(byte c) { var s = c / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(R) + 0.7152 * Ch(G) + 0.0722 * Ch(B);
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        double la = a.Luminance(), lb = b.Luminance();
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>DrawingML lumMod/lumOff (verdier i 1/1000 prosent) via HSL-luminans.</summary>
    public Rgb ApplyLum(int? lumMod, int? lumOff)
    {
        if (lumMod is null && lumOff is null) return this;
        var (h, s, l) = ToHsl();
        l = l * (lumMod ?? 100000) / 100000.0 + (lumOff ?? 0) / 100000.0;
        return FromHsl(h, s, Math.Clamp(l, 0, 1));
    }

    public Rgb Tint(int? tint, int? shade)
    {
        double r = R, g = G, b = B;
        if (tint is { } t) { var f = t / 100000.0; r = 255 - (255 - r) * f; g = 255 - (255 - g) * f; b = 255 - (255 - b) * f; }
        if (shade is { } s) { var f = s / 100000.0; r *= f; g *= f; b *= f; }
        return new Rgb((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
    }

    private (double h, double s, double l) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, l = (max + min) / 2;
        if (max == min) s = 0;
        else
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    private static Rgb FromHsl(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0) r = g = b = l;
        else
        {
            static double Hue(double p, double q, double t)
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 1.0 / 2) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3); g = Hue(p, q, h); b = Hue(p, q, h - 1.0 / 3);
        }
        return new Rgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    /// <summary>Words w:highlight-navn.</summary>
    public static Rgb? FromHighlight(string? name) => name switch
    {
        "yellow" => new(255, 255, 0), "green" => new(0, 255, 0), "cyan" => new(0, 255, 255), "magenta" => new(255, 0, 255),
        "blue" => new(0, 0, 255), "red" => new(255, 0, 0), "darkBlue" => new(0, 0, 128), "darkCyan" => new(0, 128, 128),
        "darkGreen" => new(0, 128, 0), "darkMagenta" => new(128, 0, 128), "darkRed" => new(128, 0, 0), "darkYellow" => new(128, 128, 0),
        "darkGray" => new(128, 128, 128), "lightGray" => new(192, 192, 192), "black" => Black, "white" => White, _ => null
    };
}

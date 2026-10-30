using System.Text;
using System.Xml.Linq;

namespace UuDok.Core.Opc;

/// <summary>
/// Leser faktiske data fra en diagramdel (c:chartSpace): tittel, type, akser og serier fra hurtigbufferen.
/// Brukes som grunnlag for beskrivelser, slik at tall aldri gjettes fra piksler.
/// </summary>
public static class ChartReader
{
    public sealed record Series(string Name, List<string> Categories, List<string> Values);
    public sealed record ChartData(string? Title, string Kind, List<string> AxisTitles, List<Series> Series)
    {
        public string Summary(int maxPoints = 12)
        {
            var sb = new StringBuilder();
            sb.Append($"Diagramtype: {Kind}. ");
            if (Title != null) sb.Append($"Tittel: «{Title}». ");
            if (AxisTitles.Count > 0) sb.Append($"Aksetitler: {string.Join(", ", AxisTitles.Select(a => "«" + a + "»"))}. ");
            foreach (var s in Series)
            {
                sb.Append($"Serie «{s.Name}»: ");
                sb.Append(string.Join("; ", s.Values.Take(maxPoints).Select((v, i) => (i < s.Categories.Count ? s.Categories[i] + " = " : "") + v)));
                if (s.Values.Count > maxPoints) sb.Append($"; … ({s.Values.Count} punkter)");
                sb.Append(". ");
            }
            return sb.ToString().Trim();
        }
    }

    public static ChartData? Read(byte[] chartXml)
    {
        var x = SafeXml.Load(chartXml);
        var chart = x.Root?.Element(Ns.C + "chart");
        if (chart == null) return null;
        string? title = TextOf(chart.Element(Ns.C + "title"));
        var plot = chart.Element(Ns.C + "plotArea");
        var kindEl = plot?.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Chart"));
        var kind = kindEl?.Name.LocalName switch
        {
            "barChart" => (string?)kindEl.Element(Ns.C + "barDir")?.Attribute("val") == "bar" ? "liggende stolpediagram" : "søylediagram",
            "lineChart" => "linjediagram", "pieChart" or "pie3DChart" => "sektordiagram", "doughnutChart" => "smultringdiagram",
            "areaChart" => "arealdiagram", "scatterChart" => "punktdiagram", "radarChart" => "radardiagram", "bar3DChart" => "søylediagram (3D)",
            { } n => n, null => "ukjent"
        };
        var axes = plot?.Elements().Where(e => e.Name.LocalName.EndsWith("Ax")).Select(a => TextOf(a.Element(Ns.C + "title"))).Where(t => t != null).Cast<string>().ToList() ?? [];
        var series = new List<Series>();
        foreach (var ser in kindEl?.Elements(Ns.C + "ser") ?? [])
        {
            var name = ser.Element(Ns.C + "tx")?.Descendants(Ns.C + "v").FirstOrDefault()?.Value ?? "Serie";
            var cats = Points(ser.Element(Ns.C + "cat") ?? ser.Element(Ns.C + "xVal"));
            var vals = Points(ser.Element(Ns.C + "val") ?? ser.Element(Ns.C + "yVal"));
            series.Add(new Series(name, cats, vals));
        }
        return new ChartData(title, kind, axes, series);
    }

    private static List<string> Points(XElement? e)
    {
        if (e == null) return [];
        var cache = e.Descendants().FirstOrDefault(d => d.Name.LocalName is "numCache" or "strCache");
        if (cache == null) return [];
        return cache.Elements(Ns.C + "pt").OrderBy(p => (int?)p.Attribute("idx") ?? 0).Select(p => p.Element(Ns.C + "v")?.Value ?? "").ToList();
    }

    private static string? TextOf(XElement? title)
    {
        if (title == null) return null;
        var t = string.Concat(title.Descendants(Ns.A + "t").Select(z => z.Value));
        if (t.Length == 0) t = title.Descendants(Ns.C + "v").FirstOrDefault()?.Value ?? "";
        return t.Length == 0 ? null : t;
    }
}

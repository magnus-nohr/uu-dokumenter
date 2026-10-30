using System.Globalization;
using System.Xml.Linq;

namespace UuDok.Core.Opc;

/// <summary>
/// Felles lokatorer for analyse og patching. Elementer inne i mc:Fallback telles aldri
/// (de er alternative representasjoner av samme innhold).
/// Formater:
///   nv:{id}        p:cNvPr / pic:cNvPr i PPTX (unik per lysbilde)
///   docPr:{id}:{n} wp:docPr i DOCX (n = forekomst, fordi ID-er kan være duplisert)
///   p:{n}          n-te w:p i delen
///   tbl:{n}        n-te tabell (w:tbl eller a:tbl)
///   hl:{n}         n-te hyperkobling (w:hyperlink eller a:hlinkClick)
///   vml:{n}        n-te v:shape med v:imagedata
///   part           hele delen
/// </summary>
public static class Locators
{
    public static bool InFallback(XElement e) =>
        e.Ancestors().Any(a => a.Name == Ns.Mc + "Fallback");

    public static IEnumerable<XElement> Live(XDocument d, XName name) =>
        d.Descendants(name).Where(e => !InFallback(e));

    public static IEnumerable<XElement> LiveNonVisualProps(XDocument d) =>
        d.Descendants().Where(e => e.Name.LocalName == "cNvPr" && (e.Name.Namespace == Ns.P || e.Name.Namespace == Ns.Pic || e.Name.Namespace == Ns.A || e.Name.Namespace == Ns.Wps || e.Name.Namespace == Ns.Wpg))
            .Where(e => !InFallback(e));

    public static string ForNv(int id) => $"nv:{id}";
    public static string ForDocPr(int id, int occurrence) => $"docPr:{id}:{occurrence}";
    public static string ForIndex(string kind, int n) => $"{kind}:{n}";

    public static XElement? Resolve(XDocument d, string locator)
    {
        var parts = locator.Split(':');
        switch (parts[0])
        {
            case "nv":
            {
                var id = parts[1];
                var hits = LiveNonVisualProps(d).Where(e => (string?)e.Attribute("id") == id).Take(2).ToList();
                return hits.Count == 1 ? hits[0] : null;   // tvetydig → ingen treff
            }
            case "docPr":
            {
                var id = parts[1];
                var occ = int.Parse(parts[2], CultureInfo.InvariantCulture);
                return Live(d, Ns.Wp + "docPr").Where(e => (string?)e.Attribute("id") == id).ElementAtOrDefault(occ);
            }
            case "p": return Live(d, Ns.W + "p").ElementAtOrDefault(Idx(parts));
            case "tbl":
                return d.Descendants().Where(e => (e.Name == Ns.W + "tbl" || e.Name == Ns.A + "tbl") && !InFallback(e)).ElementAtOrDefault(Idx(parts));
            case "hl":
                return d.Descendants().Where(e => (e.Name == Ns.W + "hyperlink" || e.Name == Ns.A + "hlinkClick") && !InFallback(e)).ElementAtOrDefault(Idx(parts));
            case "vml":
                return d.Descendants(Ns.V + "shape").Where(e => e.Element(Ns.V + "imagedata") != null && !InFallback(e)).ElementAtOrDefault(Idx(parts));
            case "part": return d.Root;
            default: return null;
        }
    }

    private static int Idx(string[] parts) => int.Parse(parts[1], CultureInfo.InvariantCulture);

    public static string XPathOf(XElement e)
    {
        var segs = new List<string>();
        for (var cur = e; cur != null; cur = cur.Parent)
        {
            var same = cur.Parent?.Elements(cur.Name).ToList();
            var idx = same == null ? 1 : same.IndexOf(cur) + 1;
            segs.Add($"{Prefix(cur)}{cur.Name.LocalName}[{idx}]");
        }
        segs.Reverse();
        return "/" + string.Join('/', segs);
    }

    private static string Prefix(XElement e)
    {
        var p = e.GetPrefixOfNamespace(e.Name.Namespace);
        return string.IsNullOrEmpty(p) ? "" : p + ":";
    }
}

using System.Xml.Linq;
using UuDok.Core.Opc;

namespace UuDok.Patching;

public enum DiffKind { AttributeAdded, AttributeRemoved, AttributeChanged, ElementAdded, ElementRemoved, TextChanged, ElementRenamed }

/// <summary>Én semantisk forskjell. <see cref="Element"/> peker inn i det NYE treet (for fjerninger: det gamle).</summary>
public sealed record XmlDiff(DiffKind Kind, string Path, XElement Element, XName? Attribute = null, string? Old = null, string? New = null)
{
    public override string ToString() => $"{Kind} {Path}{(Attribute != null ? "/@" + Attribute.LocalName : "")} «{Old}» → «{New}»";
}

/// <summary>
/// Sammenligner to XML-trær semantisk: navnerom-URI + lokalnavn, attributter uavhengig av rekkefølge,
/// tekstinnhold. Prefiks og navneromsdeklarasjoner regnes ikke som innhold.
/// </summary>
public static class SemanticDiff
{
    public static List<XmlDiff> Compare(XDocument a, XDocument b)
    {
        var res = new List<XmlDiff>();
        if (a.Root == null || b.Root == null) throw new ArgumentException("Tomt dokument.");
        CompareElement(a.Root, b.Root, res);
        return res;
    }

    private static void CompareElement(XElement a, XElement b, List<XmlDiff> res)
    {
        if (a.Name != b.Name)
        {
            res.Add(new XmlDiff(DiffKind.ElementRenamed, Locators.XPathOf(b), b, Old: a.Name.ToString(), New: b.Name.ToString()));
            return;
        }
        var aa = a.Attributes().Where(x => !x.IsNamespaceDeclaration).ToDictionary(x => x.Name, x => x.Value);
        var ba = b.Attributes().Where(x => !x.IsNamespaceDeclaration).ToDictionary(x => x.Name, x => x.Value);
        foreach (var (k, v) in aa)
        {
            if (!ba.TryGetValue(k, out var nv)) res.Add(new XmlDiff(DiffKind.AttributeRemoved, Locators.XPathOf(b), b, k, v));
            else if (nv != v) res.Add(new XmlDiff(DiffKind.AttributeChanged, Locators.XPathOf(b), b, k, v, nv));
        }
        foreach (var (k, v) in ba)
            if (!aa.ContainsKey(k)) res.Add(new XmlDiff(DiffKind.AttributeAdded, Locators.XPathOf(b), b, k, null, v));

        CompareChildren(a, b, res);
    }

    private sealed record Item(XNode Node, string Sig, int Hash);

    private static List<Item> Items(XElement e) =>
        e.Nodes().Where(n => n is XElement or XText).Select(n => n switch
        {
            XElement x => new Item(x, "E:" + x.Name + IdentityKey(x), DeepHash(x)),
            XText t => new Item(t, "T", t.Value.GetHashCode()),
            _ => throw new InvalidOperationException()
        }).ToList();

    private static void CompareChildren(XElement a, XElement b, List<XmlDiff> res)
    {
        var ai = Items(a); var bi = Items(b);
        int pre = 0;
        while (pre < ai.Count && pre < bi.Count && ai[pre].Hash == bi[pre].Hash && ai[pre].Sig == bi[pre].Sig && DeepEq(ai[pre].Node, bi[pre].Node)) pre++;
        int suf = 0;
        while (suf < ai.Count - pre && suf < bi.Count - pre &&
               ai[^(suf + 1)].Hash == bi[^(suf + 1)].Hash && DeepEq(ai[^(suf + 1)].Node, bi[^(suf + 1)].Node)) suf++;

        var am = ai.GetRange(pre, ai.Count - pre - suf);
        var bm = bi.GetRange(pre, bi.Count - pre - suf);
        if (am.Count == 0 && bm.Count == 0) return;

        // LCS på signatur (navn) i midtpartiet – små områder i praksis.
        var pairs = Lcs(am, bm);
        int ia = 0, ib = 0;
        foreach (var (pa, pb) in pairs.Append((am.Count, bm.Count)))
        {
            for (; ia < pa; ia++) Removed(am[ia].Node, b, res);
            for (; ib < pb; ib++) Added(bm[ib].Node, res, b);
            if (pa < am.Count && pb < bm.Count)
            {
                var (x, y) = (am[pa].Node, bm[pb].Node);
                if (x is XElement ex && y is XElement ey) CompareElement(ex, ey, res);
                else if (x is XText tx && y is XText ty && tx.Value != ty.Value)
                    res.Add(new XmlDiff(DiffKind.TextChanged, Locators.XPathOf(b), b, Old: tx.Value, New: ty.Value));
                ia++; ib++;
            }
        }
    }

    private static void Removed(XNode n, XElement newParent, List<XmlDiff> res)
    {
        if (n is XElement e) res.Add(new XmlDiff(DiffKind.ElementRemoved, Locators.XPathOf(newParent) + "/" + e.Name.LocalName, e, Old: e.Name.ToString()));
        else if (n is XText t && !string.IsNullOrEmpty(t.Value)) res.Add(new XmlDiff(DiffKind.TextChanged, Locators.XPathOf(newParent), newParent, Old: t.Value, New: ""));
    }

    private static void Added(XNode n, List<XmlDiff> res, XElement parent)
    {
        if (n is XElement e) res.Add(new XmlDiff(DiffKind.ElementAdded, Locators.XPathOf(e), e, New: e.Name.ToString()));
        else if (n is XText t && !string.IsNullOrEmpty(t.Value)) res.Add(new XmlDiff(DiffKind.TextChanged, Locators.XPathOf(parent), parent, Old: "", New: t.Value));
    }

    private static List<(int, int)> Lcs(List<Item> a, List<Item> b)
    {
        if ((long)a.Count * b.Count > 4_000_000)
        {
            // Nødløsning for svært store avvik: posisjonsvis. Gir mange diff-er, som verifikasjonen avviser.
            var n = Math.Min(a.Count, b.Count);
            return Enumerable.Range(0, n).Where(i => a[i].Sig == b[i].Sig).Select(i => (i, i)).ToList();
        }
        var dp = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
            for (int j = b.Count - 1; j >= 0; j--)
                dp[i, j] = a[i].Sig == b[j].Sig ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var res = new List<(int, int)>();
        for (int i = 0, j = 0; i < a.Count && j < b.Count;)
        {
            if (a[i].Sig == b[j].Sig) { res.Add((i, j)); i++; j++; }
            else if (dp[i + 1, j] >= dp[i, j + 1]) i++;
            else j++;
        }
        return res;
    }

    /// <summary>
    /// Identitet for figurer i et figurtre (cNvPr-id), slik at en flytting vises som fjernet + lagt til
    /// og ikke som mange attributtendringer mellom ulike figurer.
    /// </summary>
    public static string IdentityKey(XElement x)
    {
        if (x.Name.Namespace != Ns.P || x.Name.LocalName is not ("sp" or "pic" or "grpSp" or "graphicFrame" or "cxnSp")) return "";
        var nv = x.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("nv"))?.Element(Ns.P + "cNvPr");
        return nv == null ? "" : "#" + (string?)nv.Attribute("id");
    }

    public static int DeepHash(XElement e)
    {
        var h = new HashCode();
        h.Add(e.Name);
        foreach (var at in e.Attributes().Where(x => !x.IsNamespaceDeclaration).OrderBy(x => x.Name.ToString(), StringComparer.Ordinal))
        { h.Add(at.Name); h.Add(at.Value); }
        foreach (var n in e.Nodes())
            if (n is XElement c) h.Add(DeepHash(c));
            else if (n is XText t) h.Add(t.Value);
        return h.ToHashCode();
    }

    // Hash er allerede sammenlignet av kalleren; tekst sammenlignes eksakt. For elementer godtas
    // hash-likhet (32-bit, per prosess) – en eventuell kollisjon kan bare skjule en endring i et
    // område uten planlagte endringer, og fanges da av Open XML-validering og rendering.
    private static bool DeepEq(XNode x, XNode y) => x switch
    {
        XElement ex when y is XElement ey => ex.Name == ey.Name,
        XText tx when y is XText ty => tx.Value == ty.Value,
        _ => false
    };
}

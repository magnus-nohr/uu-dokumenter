using System.Globalization;
using System.Xml.Linq;
using UuDok.Core;
using UuDok.Core.Opc;

namespace UuDok.Patching;

public sealed class PreconditionFailedException(string message) : Exception(message);

/// <summary>
/// Hva én operasjon har lov til å endre. Verifikasjonen avviser alle forskjeller utenfor dette.
/// </summary>
public sealed class Allowance
{
    public HashSet<(XElement, XName)> Attributes { get; } = new(new RefAttrComparer());
    public HashSet<XElement> AddedElements { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<XElement> TextOf { get; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>Flyttede elementer: tillater «fjernet» for et element med identisk innhold og «lagt til» for selve elementet.</summary>
    public HashSet<XElement> Moved { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<int> MovedHashes { get; } = [];
    public HashSet<string> MovedKeys { get; } = [];

    public bool Covers(XmlDiff d) => d.Kind switch
    {
        DiffKind.AttributeAdded or DiffKind.AttributeChanged or DiffKind.AttributeRemoved
            => d.Attribute != null && Attributes.Contains((d.Element, d.Attribute)),
        DiffKind.ElementAdded => AddedElements.Contains(d.Element) || Moved.Contains(d.Element),
        // Identitetsnøkkel dekker tilfellet der den flyttede figuren også fikk en annen tillatt endring
        // (hver enkelt endring er allerede kontrollert mot sin egen tillatelse da den ble utført).
        DiffKind.ElementRemoved => MovedHashes.Contains(SemanticDiff.DeepHash(d.Element)) ||
                                   (SemanticDiff.IdentityKey(d.Element) is { Length: > 0 } k && MovedKeys.Contains(k)),
        DiffKind.TextChanged => TextOf.Contains(d.Element),
        _ => false
    };

    public void Merge(Allowance o)
    {
        foreach (var x in o.Attributes) Attributes.Add(x);
        foreach (var x in o.AddedElements) AddedElements.Add(x);
        foreach (var x in o.TextOf) TextOf.Add(x);
        foreach (var x in o.Moved) Moved.Add(x);
        foreach (var x in o.MovedHashes) MovedHashes.Add(x);
        foreach (var x in o.MovedKeys) MovedKeys.Add(x);
    }

    private sealed class RefAttrComparer : IEqualityComparer<(XElement, XName)>
    {
        public bool Equals((XElement, XName) a, (XElement, XName) b) => ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;
        public int GetHashCode((XElement, XName) x) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(x.Item1), x.Item2);
    }
}

/// <summary>Målrettede endringer. Hver metode kontrollerer forutsetninger og returnerer det den har lov til å endre.</summary>
public static class PatchOps
{
    public static Allowance Apply(PlannedChange c, XDocument doc, XDocument original, PatchEnv env)
    {
        var target = Locators.Resolve(doc, c.Target.Locator)
            ?? throw new PreconditionFailedException("Objektet finnes ikke lenger (eller er tvetydig).");
        if (c.ExpectedFingerprint != null)
        {
            var orig = Locators.Resolve(original, c.Target.Locator);
            if (orig == null || SafeXml.Fingerprint(orig) != c.ExpectedFingerprint)
                throw new PreconditionFailedException("Objektet er endret siden analysen.");
        }
        return c.Kind switch
        {
            ChangeKind.SetAltText => SetAltText(target, c.Value, c.OldValue),
            ChangeKind.SetDecorative => SetDecorative(target),
            ChangeKind.SetOutlineLevel => SetOutlineLevel(target, int.Parse(c.Value, CultureInfo.InvariantCulture)),
            ChangeKind.SetDocumentTitle => SetCoreTitle(doc, c.Value),
            ChangeKind.SetDocumentLanguage => SetDocxDefaultLanguage(doc, c.Value),
            ChangeKind.AddHiddenSlideTitle => AddHiddenSlideTitle(doc, c.Value, env),
            ChangeKind.AddHiddenSlideText => AddHiddenSlideText(doc, c.Value, c.Params, env),
            ChangeKind.MarkTableHeaderRow => MarkDocxTableHeader(target),
            ChangeKind.MoveToReadingStart => MoveToReadingStart(target),
            ChangeKind.SetVisibleLinkText => SetVisibleLinkText(target, c.Value, c.OldValue),
            ChangeKind.SetTextColor => SetDocxParagraphColor(target, c.Value, c.OldValue),
            _ => throw new PreconditionFailedException("Ukjent endringstype.")
        };
    }

    // ---------- Alternativ tekst ----------
    public static Allowance SetAltText(XElement target, string text, string? expectedOld)
    {
        var al = new Allowance();
        if (text.Length == 0) throw new PreconditionFailedException("Tom alternativ tekst skrives ikke.");
        if (target.Name == Ns.V + "shape")
        {
            Check(expectedOld, (string?)target.Attribute("alt"));
            target.SetAttributeValue("alt", text);
            al.Attributes.Add((target, "alt"));
            return al;
        }
        if (target.Name.LocalName is not ("cNvPr" or "docPr"))
            throw new PreconditionFailedException("Objektet støtter ikke alternativ tekst.");
        Check(expectedOld, (string?)target.Attribute("descr"));
        if (IsDecorative(target)) throw new PreconditionFailedException("Objektet er markert som dekorativt; beskrivelse legges ikke til.");
        target.SetAttributeValue("descr", text);
        al.Attributes.Add((target, "descr"));

        // DOCX: hold pic:cNvPr i samme tegning i takt dersom den allerede har descr.
        if (target.Name == Ns.Wp + "docPr")
        {
            var inline = target.Parent;
            foreach (var pic in inline?.Descendants(Ns.Pic + "cNvPr") ?? [])
                if (pic.Attribute("descr") != null && !Locators.InFallback(pic))
                {
                    pic.SetAttributeValue("descr", text);
                    al.Attributes.Add((pic, "descr"));
                }
        }
        return al;
    }

    private static void Check(string? expectedOld, string? actual)
    {
        if ((expectedOld ?? "") != (actual ?? ""))
            throw new PreconditionFailedException("Eksisterende alternativ tekst er endret siden analysen.");
    }

    public static bool IsDecorative(XElement nv) =>
        nv.Element(Ns.A + "extLst")?.Elements(Ns.A + "ext")
          .Any(x => (string?)x.Attribute("uri") == Ns.DecorativeExtUri &&
                    (string?)x.Element(Ns.Adec + "decorative")?.Attribute("val") is "1" or "true") == true;

    // ---------- Dekorativ ----------
    public static Allowance SetDecorative(XElement target)
    {
        if (target.Name.LocalName is not ("cNvPr" or "docPr")) throw new PreconditionFailedException("Objektet kan ikke markeres som dekorativt.");
        if (!string.IsNullOrWhiteSpace((string?)target.Attribute("descr")))
            throw new PreconditionFailedException("Objektet har en beskrivelse; den fjernes ikke automatisk.");
        if (IsDecorative(target)) throw new PreconditionFailedException("Allerede dekorativt.");
        var al = new Allowance();
        var ext = new XElement(Ns.A + "ext", new XAttribute("uri", Ns.DecorativeExtUri),
            new XElement(Ns.Adec + "decorative", new XAttribute(XNamespace.Xmlns + "adec", Ns.Adec.NamespaceName), new XAttribute("val", "1")));
        var extLst = target.Element(Ns.A + "extLst");
        if (extLst == null)
        {
            extLst = new XElement(Ns.A + "extLst", ext);
            target.Add(extLst);                       // extLst er siste barn i CT_NonVisualDrawingProps
            al.AddedElements.Add(extLst);
        }
        else
        {
            extLst.Add(ext);
            al.AddedElements.Add(ext);
        }
        return al;
    }

    // ---------- Overskriftsnivå uten synlig endring ----------
    private static readonly string[] PPrAfterOutline = ["divId", "cnfStyle", "rPr", "sectPr", "pPrChange"];

    public static Allowance SetOutlineLevel(XElement p, int level)
    {
        if (p.Name != Ns.W + "p") throw new PreconditionFailedException("Ikke et avsnitt.");
        if (level is < 1 or > 9) throw new PreconditionFailedException("Ugyldig nivå.");
        var al = new Allowance();
        var pPr = p.Element(Ns.W + "pPr");
        if (pPr == null)
        {
            pPr = new XElement(Ns.W + "pPr");
            p.AddFirst(pPr);
            var lvl0 = new XElement(Ns.W + "outlineLvl", new XAttribute(Ns.W + "val", level - 1));
            pPr.Add(lvl0);
            al.AddedElements.Add(pPr);
            return al;
        }
        if (pPr.Element(Ns.W + "outlineLvl") != null) throw new PreconditionFailedException("Avsnittet har allerede et dispositionsnivå.");
        var lvl = new XElement(Ns.W + "outlineLvl", new XAttribute(Ns.W + "val", level - 1));
        var after = pPr.Elements().FirstOrDefault(e => e.Name.Namespace == Ns.W && PPrAfterOutline.Contains(e.Name.LocalName));
        if (after != null) after.AddBeforeSelf(lvl); else pPr.Add(lvl);
        al.AddedElements.Add(lvl);
        return al;
    }

    // ---------- Dokumenttittel (docProps/core.xml) ----------
    public static Allowance SetCoreTitle(XDocument core, string title)
    {
        var root = core.Root!;
        if (root.Name != Ns.Cp + "coreProperties") throw new PreconditionFailedException("Uventet core.xml.");
        var al = new Allowance();
        var t = root.Element(Ns.Dc + "title");
        if (t == null)
        {
            t = new XElement(Ns.Dc + "title", title);
            root.AddFirst(t);
            al.AddedElements.Add(t);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(t.Value)) throw new PreconditionFailedException("Dokumentet har allerede en tittel.");
            t.Value = title;
            al.TextOf.Add(t);
        }
        return al;
    }

    // ---------- Standardspråk (DOCX styles.xml) ----------
    public static Allowance SetDocxDefaultLanguage(XDocument styles, string lang)
    {
        var root = styles.Root!;
        if (root.Name != Ns.W + "styles") throw new PreconditionFailedException("Uventet styles.xml.");
        var al = new Allowance();
        var dd = root.Element(Ns.W + "docDefaults");
        if (dd == null) { dd = new XElement(Ns.W + "docDefaults"); root.AddFirst(dd); al.AddedElements.Add(dd); }
        var rprd = dd.Element(Ns.W + "rPrDefault");
        if (rprd == null) { rprd = new XElement(Ns.W + "rPrDefault"); dd.AddFirst(rprd); if (!al.AddedElements.Contains(dd)) al.AddedElements.Add(rprd); }
        var rpr = rprd.Element(Ns.W + "rPr");
        if (rpr == null) { rpr = new XElement(Ns.W + "rPr"); rprd.Add(rpr); if (!al.AddedElements.Contains(dd) && !al.AddedElements.Contains(rprd)) al.AddedElements.Add(rpr); }
        var l = rpr.Element(Ns.W + "lang");
        if (l == null)
        {
            l = new XElement(Ns.W + "lang", new XAttribute(Ns.W + "val", lang));
            // w:lang kommer etter de fleste rPr-barn; bare eastAsianLayout, specVanish, oMath og rPrChange følger.
            var after = rpr.Elements().FirstOrDefault(e => e.Name.LocalName is "eastAsianLayout" or "specVanish" or "oMath" or "rPrChange");
            if (after != null) after.AddBeforeSelf(l); else rpr.Add(l);
            if (!al.AddedElements.Contains(dd) && !al.AddedElements.Contains(rprd) && !al.AddedElements.Contains(rpr)) al.AddedElements.Add(l);
        }
        else
        {
            if (!string.IsNullOrEmpty((string?)l.Attribute(Ns.W + "val"))) throw new PreconditionFailedException("Standardspråk er allerede satt.");
            l.SetAttributeValue(Ns.W + "val", lang);
            al.Attributes.Add((l, Ns.W + "val"));
        }
        return al;
    }

    // ---------- Skjult lysbildetittel ----------
    public static Allowance AddHiddenSlideTitle(XDocument slide, string text, PatchEnv env)
    {
        var spTree = slide.Root?.Element(Ns.P + "cSld")?.Element(Ns.P + "spTree")
            ?? throw new PreconditionFailedException("Lysbildet mangler figurtre.");
        bool hasTitle = spTree.Descendants(Ns.P + "ph").Any(ph => (string?)ph.Attribute("type") is "title" or "ctrTitle");
        if (hasTitle) throw new PreconditionFailedException("Lysbildet har allerede en tittelplassholder.");
        if (env.SlideHeightEmu <= 0) throw new PreconditionFailedException("Lysbildestørrelse ukjent.");

        var maxId = slide.Descendants().Where(e => e.Name.LocalName == "cNvPr")
            .Select(e => int.TryParse((string?)e.Attribute("id"), out var i) ? i : 0).DefaultIfEmpty(1).Max();
        var id = maxId + 1;
        // Plassert rett over lysbildets øvre kant: utenfor visning, lysbildefremvisning og utskrift.
        long h = Math.Max(457200, env.SlideHeightEmu / 8);
        long w = env.SlideWidthEmu > 0 ? env.SlideWidthEmu : 9144000;
        var sp = new XElement(Ns.P + "sp",
            new XElement(Ns.P + "nvSpPr",
                new XElement(Ns.P + "cNvPr", new XAttribute("id", id), new XAttribute("name", $"Tittel {id} (skjult)")),
                new XElement(Ns.P + "cNvSpPr", new XElement(Ns.A + "spLocks", new XAttribute("noGrp", "1"))),
                new XElement(Ns.P + "nvPr", new XElement(Ns.P + "ph", new XAttribute("type", "title")))),
            new XElement(Ns.P + "spPr",
                new XElement(Ns.A + "xfrm",
                    new XElement(Ns.A + "off", new XAttribute("x", 0), new XAttribute("y", -(h + 91440))),
                    new XElement(Ns.A + "ext", new XAttribute("cx", w), new XAttribute("cy", h)))),
            new XElement(Ns.P + "txBody",
                new XElement(Ns.A + "bodyPr"),
                new XElement(Ns.A + "lstStyle"),
                new XElement(Ns.A + "p",
                    new XElement(Ns.A + "r",
                        new XElement(Ns.A + "rPr", new XAttribute("lang", env.Language ?? "nb-NO"), new XAttribute("dirty", "0")),
                        new XElement(Ns.A + "t", text)))));
        // Først i figurtreet: først i leserekkefølgen, nederst i lagrekkefølgen (ingen overlapping fordi den er utenfor lysbildet).
        var grpSpPr = spTree.Element(Ns.P + "grpSpPr") ?? throw new PreconditionFailedException("Figurtreet mangler grpSpPr.");
        grpSpPr.AddAfterSelf(sp);
        var al = new Allowance();
        al.AddedElements.Add(sp);
        return al;
    }

    // ---------- Tittel og brødtekst fra tekst i et bilde ----------
    /// <summary>
    /// Legger inn ekte tittel- og brødtekstplassholdere med teksten som bare finnes i et bilde (typisk KI-genererte
    /// lysbildebilder). Første linje i <paramref name="value"/> er tittel, resten er brødtekst.
    /// Med <c>behind</c> (lokator til bildet) plasseres feltene innenfor bildets ramme og nederst i lagrekkefølgen,
    /// altså bak bildet; ellers over og under lysbildet. Feltene legges først i figurtreet, så de leses før bildet.
    /// Tekst klippes mot feltet (vertOverflow/horzOverflow="clip"), slik at den aldri flyter ut av bildets ramme.
    /// </summary>
    public static Allowance AddHiddenSlideText(XDocument slide, string value, IReadOnlyDictionary<string, string> prm, PatchEnv env)
    {
        var spTree = slide.Root?.Element(Ns.P + "cSld")?.Element(Ns.P + "spTree")
            ?? throw new PreconditionFailedException("Lysbildet mangler figurtre.");
        if (spTree.Descendants(Ns.P + "ph").Any(ph => (string?)ph.Attribute("type") is "title" or "ctrTitle"))
            throw new PreconditionFailedException("Lysbildet har allerede en tittelplassholder.");
        if (env.SlideHeightEmu <= 0 || env.SlideWidthEmu <= 0) throw new PreconditionFailedException("Lysbildestørrelse ukjent.");
        var lines = value.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
        var title = lines.FirstOrDefault() ?? "";
        var body = lines.Skip(1).Where(l => l.Length > 0).ToList();
        if (title.Length == 0) throw new PreconditionFailedException("Tittelen er tom.");

        long W = env.SlideWidthEmu, H = env.SlideHeightEmu;
        (long x, long y, long cx, long cy) titleBox, bodyBox;
        if (prm.TryGetValue("behind", out var behindLoc))
        {
            var nv = Locators.Resolve(slide, behindLoc) ?? throw new PreconditionFailedException("Bildet teksten skal ligge bak, finnes ikke.");
            var pic = nv.Parent?.Parent;
            if (pic?.Parent != spTree) throw new PreconditionFailedException("Bildet ligger i en gruppe; teksten legges ikke bak det.");
            var xfrm = pic.Element(Ns.P + "spPr")?.Element(Ns.A + "xfrm") ?? throw new PreconditionFailedException("Bildet har ingen plassering.");
            if (xfrm.Attribute("rot") != null && (string?)xfrm.Attribute("rot") != "0") throw new PreconditionFailedException("Bildet er rotert.");
            long px = (long)xfrm.Element(Ns.A + "off")!.Attribute("x")!, py = (long)xfrm.Element(Ns.A + "off")!.Attribute("y")!;
            long pcx = (long)xfrm.Element(Ns.A + "ext")!.Attribute("cx")!, pcy = (long)xfrm.Element(Ns.A + "ext")!.Attribute("cy")!;
            titleBox = (px + pcx / 20, py + pcy / 20, pcx * 9 / 10, pcy / 5);
            bodyBox = (px + pcx / 20, py + pcy * 3 / 10, pcx * 9 / 10, pcy * 3 / 5);
        }
        else
        {
            long th = Math.Max(457200, H / 8), bh = Math.Max(914400, H / 3);
            titleBox = (0, -(th + 91440), W, th);           // over lysbildet
            bodyBox = (0, H + 91440, W, bh);                // under lysbildet
        }

        var maxId = slide.Descendants().Where(e => e.Name.LocalName == "cNvPr")
            .Select(e => int.TryParse((string?)e.Attribute("id"), out var i) ? i : 0).DefaultIfEmpty(1).Max();
        var usedIdx = spTree.Descendants(Ns.P + "ph").Select(ph => int.TryParse((string?)ph.Attribute("idx"), out var i) ? i : -1).ToHashSet();
        var bodyIdx = usedIdx.Contains(1) ? usedIdx.Max() + 1 : 1;
        var lang = env.Language ?? "nb-NO";

        XElement Shape(int id, string name, XElement ph, (long x, long y, long cx, long cy) b, IEnumerable<XElement> paras) =>
            new(Ns.P + "sp",
                new XElement(Ns.P + "nvSpPr",
                    new XElement(Ns.P + "cNvPr", new XAttribute("id", id), new XAttribute("name", name)),
                    new XElement(Ns.P + "cNvSpPr", new XElement(Ns.A + "spLocks", new XAttribute("noGrp", "1"))),
                    new XElement(Ns.P + "nvPr", ph)),
                new XElement(Ns.P + "spPr",
                    new XElement(Ns.A + "xfrm",
                        new XElement(Ns.A + "off", new XAttribute("x", b.x), new XAttribute("y", b.y)),
                        new XElement(Ns.A + "ext", new XAttribute("cx", b.cx), new XAttribute("cy", b.cy)))),
                new XElement(Ns.P + "txBody",
                    new XElement(Ns.A + "bodyPr", new XAttribute("vertOverflow", "clip"), new XAttribute("horzOverflow", "clip"), new XAttribute("wrap", "square")),
                    new XElement(Ns.A + "lstStyle"),
                    paras));

        XElement Run(string t) => new(Ns.A + "r", new XElement(Ns.A + "rPr", new XAttribute("lang", lang), new XAttribute("dirty", "0")), new XElement(Ns.A + "t", t));

        var al = new Allowance();
        var titleSp = Shape(maxId + 1, $"Tittel {maxId + 1} (fra bildet)", new XElement(Ns.P + "ph", new XAttribute("type", "title")), titleBox,
            [new XElement(Ns.A + "p", Run(title))]);
        var grpSpPr = spTree.Element(Ns.P + "grpSpPr") ?? throw new PreconditionFailedException("Figurtreet mangler grpSpPr.");
        grpSpPr.AddAfterSelf(titleSp);
        al.AddedElements.Add(titleSp);
        if (body.Count > 0)
        {
            var bodySp = Shape(maxId + 2, $"Brødtekst {maxId + 2} (fra bildet)",
                new XElement(Ns.P + "ph", new XAttribute("type", "body"), new XAttribute("idx", bodyIdx)), bodyBox,
                body.Select(l => new XElement(Ns.A + "p",
                    new XElement(Ns.A + "pPr", new XAttribute("marL", 0), new XAttribute("indent", 0), new XElement(Ns.A + "buNone")), Run(l))));
            titleSp.AddAfterSelf(bodySp);
            al.AddedElements.Add(bodySp);
        }
        return al;
    }

    // ---------- Leserekkefølge (PPTX) ----------
    /// <summary>
    /// Flytter en toppnivåfigur først i figurtreet. Kalleren (planleggeren) har kontrollert at figuren ikke
    /// overlapper noen figur den passerer; visuell sammenligning bekrefter etterpå.
    /// </summary>
    public static Allowance MoveToReadingStart(XElement cNvPr)
    {
        var shape = cNvPr.Parent?.Parent;
        var tree = shape?.Parent;
        if (shape == null || tree?.Name != Ns.P + "spTree") throw new PreconditionFailedException("Bare figurer på øverste nivå kan flyttes.");
        var grpSpPr = tree.Element(Ns.P + "grpSpPr") ?? throw new PreconditionFailedException("Figurtreet mangler grpSpPr.");
        if (grpSpPr.ElementsAfterSelf().FirstOrDefault() == shape) throw new PreconditionFailedException("Figuren leses allerede først.");
        var al = new Allowance();
        al.MovedHashes.Add(SemanticDiff.DeepHash(shape));
        al.MovedKeys.Add(SemanticDiff.IdentityKey(shape));
        shape.Remove();
        grpSpPr.AddAfterSelf(shape);
        al.Moved.Add(shape);
        return al;
    }

    // ---------- Synlig lenketekst (bare «Tillat synlige forbedringer») ----------
    public static Allowance SetVisibleLinkText(XElement link, string text, string? expectedOld)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new PreconditionFailedException("Tom lenketekst.");
        var al = new Allowance();
        List<XElement> ts;
        if (link.Name == Ns.W + "hyperlink") ts = link.Descendants(Ns.W + "t").ToList();
        else if (link.Name == Ns.A + "hlinkClick" && link.Parent?.Parent?.Name == Ns.A + "r") ts = link.Parent.Parent.Elements(Ns.A + "t").ToList();
        else throw new PreconditionFailedException("Lenketeksten kan ikke endres for denne typen lenke.");
        if (ts.Count == 0) throw new PreconditionFailedException("Lenken har ingen tekst.");
        if (expectedOld != null && string.Concat(ts.Select(t => t.Value)) != expectedOld) throw new PreconditionFailedException("Lenketeksten er endret siden analysen.");
        ts[0].Value = text;
        if (ts[0].Name == Ns.W + "t") ts[0].SetAttributeValue(XNamespace.Xml + "space", "preserve");
        al.TextOf.Add(ts[0]);
        al.Attributes.Add((ts[0], XNamespace.Xml + "space"));
        foreach (var t in ts.Skip(1)) { t.Value = ""; al.TextOf.Add(t); }
        return al;
    }

    // ---------- Tekstfarge i avsnitt (bare «Tillat synlige forbedringer») ----------
    public static Allowance SetDocxParagraphColor(XElement p, string newHex, string? oldHex)
    {
        if (p.Name != Ns.W + "p") throw new PreconditionFailedException("Ikke et avsnitt.");
        var al = new Allowance();
        int n = 0;
        foreach (var color in p.Descendants(Ns.W + "color").Where(c => c.Parent?.Name == Ns.W + "rPr" && c.Ancestors(Ns.W + "p").First() == p))
        {
            if (oldHex != null && !string.Equals((string?)color.Attribute(Ns.W + "val"), oldHex.TrimStart('#'), StringComparison.OrdinalIgnoreCase)) continue;
            color.SetAttributeValue(Ns.W + "val", newHex.TrimStart('#'));
            al.Attributes.Add((color, Ns.W + "val"));
            foreach (var a in new[] { "themeColor", "themeShade", "themeTint" })
                if (color.Attribute(Ns.W + a) != null) { color.Attribute(Ns.W + a)!.Remove(); al.Attributes.Add((color, Ns.W + a)); }
            n++;
        }
        if (n == 0) throw new PreconditionFailedException("Fant ingen eksplisitt tekstfarge å endre (farge fra stil endres ikke automatisk).");
        return al;
    }

    // ---------- Tabelloverskrift (DOCX) ----------
    public static Allowance MarkDocxTableHeader(XElement tbl)
    {
        if (tbl.Name != Ns.W + "tbl") throw new PreconditionFailedException("Ikke en Word-tabell.");
        var tr = tbl.Element(Ns.W + "tr") ?? throw new PreconditionFailedException("Tabellen har ingen rader.");
        var al = new Allowance();
        var trPr = tr.Element(Ns.W + "trPr");
        var hdr = new XElement(Ns.W + "tblHeader");
        if (trPr == null)
        {
            trPr = new XElement(Ns.W + "trPr", hdr);
            var tblPrEx = tr.Element(Ns.W + "tblPrEx");
            if (tblPrEx != null) tblPrEx.AddAfterSelf(trPr); else tr.AddFirst(trPr);
            al.AddedElements.Add(trPr);
        }
        else
        {
            if (trPr.Element(Ns.W + "tblHeader") != null) throw new PreconditionFailedException("Overskriftsrad er allerede markert.");
            var after = trPr.Elements().FirstOrDefault(e => e.Name.LocalName is "jc" or "hidden" or "ins" or "del" or "trPrChange");
            if (after != null) after.AddBeforeSelf(hdr); else trPr.Add(hdr);
            al.AddedElements.Add(hdr);
        }
        return al;
    }
}

public sealed record PatchEnv(long SlideWidthEmu = 0, long SlideHeightEmu = 0, string? Language = null);

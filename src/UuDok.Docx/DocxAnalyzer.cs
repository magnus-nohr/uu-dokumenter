using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using UuDok.Core;
using UuDok.Core.Opc;

namespace UuDok.Docx;

/// <summary>
/// Lesende analyse av DOCX. Open XML SDK brukes til å navigere pakkedeler og relasjoner;
/// elementinnhold leses som XML med de samme lokatorene som patchemotoren bruker.
/// </summary>
public sealed partial class DocxAnalyzer
{
    private sealed record StyleInfo(string Id, string? Name, string? BasedOn, int? OutlineLvl, int? Size, bool? Bold, string? Color, string Type);

    private Dictionary<string, StyleInfo> _styles = [];
    private int _defaultSize = 22;               // halvpunkter
    private string? _defaultColor;

    public DocumentModel Analyze(byte[] file, OpcPackage pkg)
    {
        var model = new DocumentModel { Kind = DocumentKind.Docx };
        using var ms = new MemoryStream(file, writable: false);
        using var doc = WordprocessingDocument.Open(ms, false, Settings.ReadOnly);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("Dokumentet mangler hoveddel.");

        model.Title = CoreTitle(pkg);
        LoadStyles(pkg, main, model);
        if (pkg.Get("/word/settings.xml") is { } st && SafeXml.Load(st.Data).Root?.Element(Ns.W + "documentProtection") is { } dp
            && (string?)dp.Attribute(Ns.W + "enforcement") is "1" or "true") model.HasProtection = true;
        model.HasComments = main.WordprocessingCommentsPart != null;

        var parts = new List<(OpenXmlPart Part, string Label)> { (main, "Hoveddokument") };
        parts.AddRange(main.HeaderParts.Select((h, i) => ((OpenXmlPart)h, $"Topptekst {i + 1}")));
        parts.AddRange(main.FooterParts.Select((f, i) => ((OpenXmlPart)f, $"Bunntekst {i + 1}")));
        if (main.FootnotesPart != null) parts.Add((main.FootnotesPart, "Fotnoter"));
        if (main.EndnotesPart != null) parts.Add((main.EndnotesPart, "Sluttnoter"));

        foreach (var (part, label) in parts)
        {
            var uri = part.Uri.OriginalString;
            var x = SafeXml.Load(pkg.GetBytes(uri));
            AnalyzePart(model, part, uri, x, label);
            if (x.Descendants(Ns.W + "ins").Any() || x.Descendants(Ns.W + "del").Any()) model.HasTrackedChanges = true;
        }
        model.SlideOrPageCount = 0; // sidetall er bare kjent etter rendering
        return model;
    }

    private static string? CoreTitle(OpcPackage pkg)
    {
        var core = pkg.Get("/docProps/core.xml");
        if (core == null) return null;
        var t = SafeXml.Load(core.Data).Root?.Element(Ns.Dc + "title")?.Value;
        return string.IsNullOrWhiteSpace(t) ? null : t.Trim();
    }

    private void LoadStyles(OpcPackage pkg, MainDocumentPart main, DocumentModel model)
    {
        _styles = [];
        if (main.StyleDefinitionsPart == null) return;
        var s = SafeXml.Load(pkg.GetBytes(main.StyleDefinitionsPart.Uri.OriginalString)).Root!;
        var rpr = s.Element(Ns.W + "docDefaults")?.Element(Ns.W + "rPrDefault")?.Element(Ns.W + "rPr");
        model.DefaultLanguage = (string?)rpr?.Element(Ns.W + "lang")?.Attribute(Ns.W + "val");
        if (int.TryParse((string?)rpr?.Element(Ns.W + "sz")?.Attribute(Ns.W + "val"), out var sz)) _defaultSize = sz;
        foreach (var st in s.Elements(Ns.W + "style"))
        {
            var id = (string?)st.Attribute(Ns.W + "styleId");
            if (id == null) continue;
            var srpr = st.Element(Ns.W + "rPr");
            _styles[id] = new StyleInfo(id,
                (string?)st.Element(Ns.W + "name")?.Attribute(Ns.W + "val"),
                (string?)st.Element(Ns.W + "basedOn")?.Attribute(Ns.W + "val"),
                int.TryParse((string?)st.Element(Ns.W + "pPr")?.Element(Ns.W + "outlineLvl")?.Attribute(Ns.W + "val"), out var ol) ? ol : null,
                int.TryParse((string?)srpr?.Element(Ns.W + "sz")?.Attribute(Ns.W + "val"), out var ssz) ? ssz : null,
                srpr?.Element(Ns.W + "b") is { } b ? (string?)b.Attribute(Ns.W + "val") is not ("0" or "false") : null,
                (string?)srpr?.Element(Ns.W + "color")?.Attribute(Ns.W + "val"),
                (string?)st.Attribute(Ns.W + "type") ?? "paragraph");
            if ((string?)st.Attribute(Ns.W + "default") is "1" or "true" && (string?)st.Attribute(Ns.W + "type") == "paragraph")
            {
                if (_styles[id].Size is { } ds) _defaultSize = ds;
                _defaultColor = _styles[id].Color;
            }
        }
    }

    private T? StyleProp<T>(string? styleId, Func<StyleInfo, T?> f) where T : struct
    {
        for (int guard = 0; styleId != null && guard < 20 && _styles.TryGetValue(styleId, out var s); guard++)
        {
            if (f(s) is { } v) return v;
            styleId = s.BasedOn;
        }
        return null;
    }

    private string? StyleColor(string? styleId)
    {
        for (int guard = 0; styleId != null && guard < 20 && _styles.TryGetValue(styleId, out var s); guard++)
        {
            if (s.Color != null) return s.Color;
            styleId = s.BasedOn;
        }
        return null;
    }

    private int? HeadingLevel(string? styleId)
    {
        if (styleId == null || !_styles.TryGetValue(styleId, out var s)) return null;
        var name = s.Name?.ToLowerInvariant() ?? "";
        var m = HeadingName().Match(name);
        if (m.Success) return int.Parse(m.Groups[1].Value);
        if (name == "title") return null; // Tittel-stil er ikke en overskrift i navigasjonen
        var ol = StyleProp<int>(styleId, x => x.OutlineLvl);
        return ol is >= 0 and <= 8 ? ol + 1 : null;
    }

    [GeneratedRegex(@"^heading (\d)$")] private static partial Regex HeadingName();
    [GeneratedRegex(@"^\s*([-–•*▪●○■□►✓]|\d{1,2}[.)]|[a-zA-Z][.)])\s+")] private static partial Regex FakeListPrefix();

    public static string OwnText(XElement p)
    {
        var sb = new StringBuilder();
        foreach (var e in p.Descendants())
        {
            if (e.Name != Ns.W + "t" && e.Name != Ns.W + "tab" && e.Name != Ns.W + "br") continue;
            if (e.Ancestors(Ns.W + "p").FirstOrDefault() != p) continue;
            if (Locators.InFallback(e)) continue;
            sb.Append(e.Name == Ns.W + "t" ? e.Value : e.Name == Ns.W + "tab" ? "\t" : "\n");
        }
        return sb.ToString();
    }

    private void AnalyzePart(DocumentModel model, OpenXmlPart part, string uri, XDocument x, string label)
    {
        var paras = Locators.Live(x, Ns.W + "p").ToList();
        var texts = paras.Select(OwnText).ToList();
        var bodyParas = paras.Select((p, i) => (p, i)).Where(t => !t.p.Ancestors(Ns.W + "txbxContent").Any() && !t.p.Ancestors(Ns.W + "tc").Any()).ToList();
        var paraIndex = new Dictionary<XElement, int>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < paras.Count; k++) paraIndex[paras[k]] = k;
        var pageBg = Rgb.TryParse((string?)x.Root?.Element(Ns.W + "background")?.Attribute(Ns.W + "color")) ?? Rgb.White;

        // ---- Avsnitt ----
        int? prevLevel = 0;
        for (int i = 0; i < paras.Count; i++)
        {
            var p = paras[i];
            var text = texts[i];
            var pPr = p.Element(Ns.W + "pPr");
            var style = (string?)pPr?.Element(Ns.W + "pStyle")?.Attribute(Ns.W + "val");
            var direct = int.TryParse((string?)pPr?.Element(Ns.W + "outlineLvl")?.Attribute(Ns.W + "val"), out var dol) && dol <= 8 ? dol + 1 : (int?)null;
            var level = direct ?? HeadingLevel(style);
            var inTable = p.Ancestors(Ns.W + "tc").Any();
            var inBox = p.Ancestors(Ns.W + "txbxContent").Any();
            var runs = p.Elements(Ns.W + "r").Concat(p.Elements(Ns.W + "hyperlink").Elements(Ns.W + "r"))
                .Concat(p.Elements(Ns.W + "ins").Elements(Ns.W + "r")).Where(r => r.Element(Ns.W + "t") != null).ToList();
            int maxSize = 0; bool allBold = runs.Count > 0;
            foreach (var r in runs)
            {
                var rpr = r.Element(Ns.W + "rPr");
                var sz = int.TryParse((string?)rpr?.Element(Ns.W + "sz")?.Attribute(Ns.W + "val"), out var s1) ? s1
                       : StyleProp<int>(style, st => st.Size) ?? _defaultSize;
                maxSize = Math.Max(maxSize, sz);
                var b = rpr?.Element(Ns.W + "b") is { } be ? (string?)be.Attribute(Ns.W + "val") is not ("0" or "false") : StyleProp<bool>(style, st => st.Bold) ?? false;
                allBold &= b;
            }
            var isList = pPr?.Element(Ns.W + "numPr") != null;
            var trimmed = text.Trim();
            var extra = new Dictionary<string, string>
            {
                ["size"] = maxSize.ToString(), ["bold"] = allBold ? "1" : "0", ["part"] = label,
                ["inTable"] = inTable ? "1" : "0", ["inTextBox"] = inBox ? "1" : "0", ["isList"] = isList ? "1" : "0",
            };
            if (!isList && FakeListPrefix().IsMatch(text)) extra["fakeList"] = "1";

            // Heuristisk «ser ut som overskrift»
            if (level == null && !inTable && !inBox && trimmed.Length is > 0 and <= 90 && !trimmed.EndsWith('.') && !trimmed.EndsWith(':') && !isList
                && (maxSize >= _defaultSize * 1.25 || (allBold && trimmed.Length <= 60)))
            {
                var next = texts.Skip(i + 1).FirstOrDefault(t => t.Trim().Length > 0)?.Trim() ?? "";
                if (next.Length > 0)
                {
                    int guess = maxSize >= _defaultSize * 1.9 ? 1 : maxSize >= _defaultSize * 1.25 ? 2 : 3;
                    extra["looksLikeHeading"] = guess.ToString();
                }
            }

            model.Objects.Add(new DocObject
            {
                Ref = new ObjectRef(uri, Locators.ForIndex("p", i)),
                Type = level != null ? ObjectType.Heading : isList ? ObjectType.ListItem : ObjectType.Paragraph,
                Text = text, HeadingLevel = level, StyleId = style, TreeIndex = i, Extra = extra,
                Source = new SourceRef(uri, Locators.XPathOf(p)), Fingerprint = SafeXml.Fingerprint(p),
            });
            if (level != null) prevLevel = level;

            // ---- Kontrast per avsnitt (eksplisitte farger) ----
            var shdP = Rgb.TryParse((string?)pPr?.Element(Ns.W + "shd")?.Attribute(Ns.W + "fill"));
            var shdCell = Rgb.TryParse((string?)p.Ancestors(Ns.W + "tc").FirstOrDefault()?.Element(Ns.W + "tcPr")?.Element(Ns.W + "shd")?.Attribute(Ns.W + "fill"));
            double worst = 99; string? worstFg = null, worstBg = null; bool worstLarge = false;
            foreach (var r in runs)
            {
                var rpr = r.Element(Ns.W + "rPr");
                var rStyle = (string?)rpr?.Element(Ns.W + "rStyle")?.Attribute(Ns.W + "val");
                var colorVal = (string?)rpr?.Element(Ns.W + "color")?.Attribute(Ns.W + "val") ?? StyleColor(rStyle) ?? StyleColor(style) ?? _defaultColor;
                var fg = Rgb.TryParse(colorVal);
                if (fg == null) continue; // «auto» eller tema uten verdi: Word velger kontrastfarge
                var bg = Rgb.FromHighlight((string?)rpr?.Element(Ns.W + "highlight")?.Attribute(Ns.W + "val"))
                         ?? Rgb.TryParse((string?)rpr?.Element(Ns.W + "shd")?.Attribute(Ns.W + "fill")) ?? shdP ?? shdCell ?? pageBg;
                var cr = Rgb.Contrast(fg.Value, bg);
                var sz = int.TryParse((string?)rpr?.Element(Ns.W + "sz")?.Attribute(Ns.W + "val"), out var s2) ? s2 : StyleProp<int>(style, st => st.Size) ?? _defaultSize;
                var bold = rpr?.Element(Ns.W + "b") != null;
                var large = sz >= 36 || (bold && sz >= 28);
                if (cr < worst) { worst = cr; worstFg = fg.ToString(); worstBg = bg.ToString(); worstLarge = large; }
            }
            if (worstFg != null)
            {
                extra["contrast"] = worst.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                extra["fg"] = worstFg; extra["bg"] = worstBg!; extra["large"] = worstLarge ? "1" : "0";
            }
        }

        // ---- Bilder og tegninger (DrawingML) ----
        var docPrs = Locators.Live(x, Ns.Wp + "docPr").ToList();
        var occ = new Dictionary<string, int>();
        foreach (var dp in docPrs)
        {
            var id = (string?)dp.Attribute("id") ?? "0";
            occ[id] = occ.GetValueOrDefault(id, -1) + 1;
            var holder = dp.Parent!;                    // wp:inline / wp:anchor
            var gd = holder.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData");
            var uriGd = (string?)gd?.Attribute("uri") ?? "";
            var type = uriGd switch
            {
                _ when uriGd.EndsWith("/picture") => ObjectType.Picture,
                _ when uriGd.EndsWith("/chart") => ObjectType.Chart,
                _ when uriGd.EndsWith("/diagram") => ObjectType.SmartArt,
                _ when uriGd.EndsWith("wordprocessingShape") => holder.Descendants(Ns.Wps + "txbx").Any() ? ObjectType.TextBox : ObjectType.Shape,
                _ when uriGd.EndsWith("wordprocessingGroup") => ObjectType.Group,
                _ => ObjectType.Shape
            };
            var blip = holder.Descendants(Ns.A + "blip").FirstOrDefault();
            var rid = (string?)blip?.Attribute(Ns.R + "embed");
            string? imgUri = null, imgCt = null;
            if (rid != null) try { var ip = part.GetPartById(rid); imgUri = ip.Uri.OriginalString; imgCt = ip.ContentType; } catch { }
            var src = holder.Descendants(Ns.A + "srcRect").FirstOrDefault();
            var ext = holder.Element(Ns.Wp + "extent");
            var para = dp.Ancestors(Ns.W + "p").FirstOrDefault();
            var pi = para != null && paraIndex.TryGetValue(para, out var pidx) ? pidx : -1;
            var (before, after, caption) = Context(texts, pi);
            var extra = new Dictionary<string, string> { ["part"] = label, ["floating"] = holder.Name.LocalName == "anchor" ? "1" : "0" };
            if (type == ObjectType.Chart && rid == null)
            {
                var cid = (string?)gd?.Descendants().FirstOrDefault(e => e.Name.LocalName == "chart")?.Attribute(Ns.R + "id");
                if (cid != null) try { extra["chartPart"] = part.GetPartById(cid).Uri.OriginalString; } catch { }
            }
            model.Objects.Add(new DocObject
            {
                Ref = new ObjectRef(uri, Locators.ForDocPr(int.TryParse(id, out var nid) ? nid : 0, occ[id])),
                Type = type, Name = (string?)dp.Attribute("name"), NativeId = int.TryParse(id, out var n2) ? n2 : null,
                AltText = (string?)dp.Attribute("descr"), AltTitle = (string?)dp.Attribute("title"),
                Decorative = DecorativeOf(dp), ImagePartUri = imgUri, ImageContentType = imgCt,
                Crop = src == null ? null : new Crop(Int(src, "l"), Int(src, "t"), Int(src, "r"), Int(src, "b")),
                Bounds = ext == null ? null : new Bounds(0, 0, (long?)ext.Attribute("cx") ?? 0, (long?)ext.Attribute("cy") ?? 0),
                ContextBefore = before, ContextAfter = after, Caption = caption, TreeIndex = pi, Extra = extra,
                Source = new SourceRef(uri, Locators.XPathOf(dp)), Fingerprint = SafeXml.Fingerprint(dp),
            });
        }

        // ---- Eldre VML-bilder ----
        var vml = x.Descendants(Ns.V + "shape").Where(e => e.Element(Ns.V + "imagedata") != null && !Locators.InFallback(e)).ToList();
        for (int i = 0; i < vml.Count; i++)
        {
            var sh = vml[i];
            var rid = (string?)sh.Element(Ns.V + "imagedata")!.Attribute(Ns.R + "id");
            string? imgUri = null, imgCt = null;
            if (rid != null) try { var ip = part.GetPartById(rid); imgUri = ip.Uri.OriginalString; imgCt = ip.ContentType; } catch { }
            var isOle = sh.Ancestors(Ns.W + "object").Any();
            var para = sh.Ancestors(Ns.W + "p").FirstOrDefault();
            var pi = para != null && paraIndex.TryGetValue(para, out var pidx) ? pidx : -1;
            var (before, after, caption) = Context(texts, pi);
            model.Objects.Add(new DocObject
            {
                Ref = new ObjectRef(uri, Locators.ForIndex("vml", i)), Type = isOle ? ObjectType.OleObject : ObjectType.Picture,
                Name = (string?)sh.Attribute("id"), AltText = (string?)sh.Attribute("alt"), ImagePartUri = imgUri, ImageContentType = imgCt,
                ContextBefore = before, ContextAfter = after, Caption = caption, TreeIndex = pi,
                Extra = new() { ["part"] = label, ["legacy"] = "vml" },
                Source = new SourceRef(uri, Locators.XPathOf(sh)), Fingerprint = SafeXml.Fingerprint(sh),
            });
        }

        // ---- Tabeller ----
        var tbls = x.Descendants().Where(e => e.Name == Ns.W + "tbl" && !Locators.InFallback(e)).ToList();
        for (int i = 0; i < tbls.Count; i++)
        {
            var t = tbls[i];
            var rows = t.Elements(Ns.W + "tr").ToList();
            var first = rows.FirstOrDefault();
            bool header = first?.Element(Ns.W + "trPr")?.Element(Ns.W + "tblHeader") is { } th && (string?)th.Attribute(Ns.W + "val") is not ("0" or "false");
            bool merged = t.Descendants(Ns.W + "gridSpan").Any(g => (int?)g.Attribute(Ns.W + "val") > 1) || t.Descendants(Ns.W + "vMerge").Any();
            int cols = t.Element(Ns.W + "tblGrid")?.Elements(Ns.W + "gridCol").Count() ?? 0;
            model.Objects.Add(new DocObject
            {
                Ref = new ObjectRef(uri, Locators.ForIndex("tbl", i)), Type = ObjectType.Table,
                Text = string.Join(" | ", first?.Elements(Ns.W + "tc").Select(c => string.Concat(c.Descendants(Ns.W + "t").Select(z => z.Value))) ?? []),
                Extra = new()
                {
                    ["rows"] = rows.Count.ToString(), ["cols"] = cols.ToString(), ["headerRow"] = header ? "1" : "0",
                    ["merged"] = merged ? "1" : "0", ["nested"] = t.Ancestors(Ns.W + "tbl").Any() ? "1" : "0", ["part"] = label,
                    ["caption"] = (string?)t.Element(Ns.W + "tblPr")?.Element(Ns.W + "tblCaption")?.Attribute(Ns.W + "val") ?? "",
                },
                Source = new SourceRef(uri, Locators.XPathOf(t)), Fingerprint = SafeXml.Fingerprint(t),
            });
        }

        // ---- Lenker ----
        var rels = part.HyperlinkRelationships.ToDictionary(h => h.Id, h => h.Uri.OriginalString);
        var links = x.Descendants().Where(e => (e.Name == Ns.W + "hyperlink" || e.Name == Ns.A + "hlinkClick") && !Locators.InFallback(e)).ToList();
        for (int i = 0; i < links.Count; i++)
        {
            var h = links[i];
            if (h.Name != Ns.W + "hyperlink") continue;
            var rid = (string?)h.Attribute(Ns.R + "id");
            var anchor = (string?)h.Attribute(Ns.W + "anchor");
            var target = rid != null && rels.TryGetValue(rid, out var tt) ? tt : anchor != null ? "#" + anchor : null;
            var para = h.Ancestors(Ns.W + "p").FirstOrDefault();
            model.Objects.Add(new DocObject
            {
                Ref = new ObjectRef(uri, Locators.ForIndex("hl", i)), Type = ObjectType.Hyperlink,
                Text = string.Concat(h.Descendants(Ns.W + "t").Select(t => t.Value)), HyperlinkTarget = target, HyperlinkExternal = rid != null,
                ContextBefore = para != null ? OwnText(para) : null, Extra = new() { ["part"] = label },
                Source = new SourceRef(uri, Locators.XPathOf(h)), Fingerprint = SafeXml.Fingerprint(h),
            });
        }

        // ---- Formler og språk ----
        int maths = Locators.Live(x, Ns.M + "oMath").Count();
        if (maths > 0) model.Notes.Add($"{label}: {maths} formler (OMML) – bevares som ekte formler.");
        foreach (var l in x.Descendants(Ns.W + "lang").Select(e => (string?)e.Attribute(Ns.W + "val")).Where(v => v != null).Distinct())
            if (l != model.DefaultLanguage) model.Notes.Add($"{label}: tekst merket med språk {l}.");
    }

    private static (string? before, string? after, string? caption) Context(List<string> texts, int i)
    {
        if (i < 0) return (null, null, null);
        string? Pick(IEnumerable<string> seq) => string.Join(" ", seq.Where(t => t.Trim().Length > 0).Take(2).Select(t => t.Trim())) is { Length: > 0 } s ? Trunc(s, 600) : null;
        var before = Pick(Enumerable.Range(0, i).Reverse().Select(k => texts[k]));
        var after = Pick(Enumerable.Range(i + 1, Math.Max(0, texts.Count - i - 1)).Select(k => texts[k]));
        string? caption = null;
        foreach (var k in new[] { i - 1, i + 1, i })
            if (k >= 0 && k < texts.Count && CaptionRx().IsMatch(texts[k].Trim())) { caption = texts[k].Trim(); break; }
        return (before, after, caption);
    }

    [GeneratedRegex(@"^(Figur|Fig\.|Figure|Bilde|Illustrasjon|Diagram|Tabell|Table)\s*\d*[:.\s]", RegexOptions.IgnoreCase)]
    private static partial Regex CaptionRx();

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
    private static int Int(XElement e, string a) => int.TryParse((string?)e.Attribute(a), out var v) ? v : 0;

    public static bool DecorativeOf(XElement nv) =>
        nv.Element(Ns.A + "extLst")?.Elements(Ns.A + "ext").Any(x => (string?)x.Attribute("uri") == Ns.DecorativeExtUri &&
            (string?)x.Element(Ns.Adec + "decorative")?.Attribute("val") is "1" or "true") == true;
}

internal static class Settings
{
    public static DocumentFormat.OpenXml.Packaging.OpenSettings ReadOnly => new()
    {
        AutoSave = false,
        MarkupCompatibilityProcessSettings = new DocumentFormat.OpenXml.Packaging.MarkupCompatibilityProcessSettings(
            DocumentFormat.OpenXml.Packaging.MarkupCompatibilityProcessMode.NoProcess, DocumentFormat.OpenXml.FileFormatVersions.Microsoft365),
    };
}

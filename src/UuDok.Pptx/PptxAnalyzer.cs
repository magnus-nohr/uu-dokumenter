using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using UuDok.Core;
using UuDok.Core.Opc;

namespace UuDok.Pptx;

/// <summary>
/// Lesende analyse av PPTX. Figurtreet gås gjennom i lagrekkefølge (= leserekkefølge, se bevis 3/4).
/// Plassholdere uten egen plassering arver fra oppsett og master.
/// </summary>
public sealed class PptxAnalyzer
{
    private static readonly OpenSettings ReadOnly = new()
    {
        AutoSave = false,
        MarkupCompatibilityProcessSettings = new MarkupCompatibilityProcessSettings(MarkupCompatibilityProcessMode.NoProcess, FileFormatVersions.Microsoft365),
    };

    public DocumentModel Analyze(byte[] file, OpcPackage pkg)
    {
        var model = new DocumentModel { Kind = DocumentKind.Pptx };
        using var ms = new MemoryStream(file, writable: false);
        using var doc = PresentationDocument.Open(ms, false, ReadOnly);
        var pp = doc.PresentationPart ?? throw new InvalidDataException("Presentasjonen mangler hoveddel.");
        var pres = SafeXml.Load(pkg.GetBytes(pp.Uri.OriginalString)).Root!;
        var sz = pres.Element(Ns.P + "sldSz");
        model.SlideWidthEmu = (long?)sz?.Attribute("cx") ?? 12192000;
        model.SlideHeightEmu = (long?)sz?.Attribute("cy") ?? 6858000;
        if (pkg.Get("/docProps/core.xml") is { } core)
        {
            var t = SafeXml.Load(core.Data).Root?.Element(Ns.Dc + "title")?.Value;
            model.Title = string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        }
        model.HasProtection = pres.Element(Ns.P + "modifyVerifier") != null;
        model.DefaultLanguage = (string?)pres.Element(Ns.P + "defaultTextStyle")?.Descendants(Ns.A + "defRPr").FirstOrDefault()?.Attribute("lang");

        var ids = pres.Element(Ns.P + "sldIdLst")?.Elements(Ns.P + "sldId").Select(s => (string)s.Attribute(Ns.R + "id")!).ToList() ?? [];
        int n = 0;
        foreach (var rid in ids)
        {
            n++;
            if (pp.GetPartById(rid) is not SlidePart sp) continue;
            AnalyzeSlide(model, pkg, sp, n);
        }
        model.SlideOrPageCount = n;
        if (pp.Parts.Any(p => p.OpenXmlPart is PresentationPropertiesPart) == false) { }
        model.HasComments = pp.SlideParts.Any(s => s.SlideCommentsPart != null || s.Parts.Any(p => p.OpenXmlPart.ContentType.Contains("comments")));
        return model;
    }

    private sealed record Xf(long OffX, long OffY, long ScaleNum, long ScaleDenX, long ScaleNumY, long ScaleDenY, long ChOffX, long ChOffY)
    {
        public static readonly Xf Identity = new(0, 0, 1, 1, 1, 1, 0, 0);
        public Bounds Map(Bounds b) => new(
            OffX + (b.X - ChOffX) * ScaleNum / ScaleDenX, OffY + (b.Y - ChOffY) * ScaleNumY / ScaleDenY,
            b.Cx * ScaleNum / ScaleDenX, b.Cy * ScaleNumY / ScaleDenY, b.RotationDeg60k);
    }

    private void AnalyzeSlide(DocumentModel model, OpcPackage pkg, SlidePart sp, int number)
    {
        var uri = sp.Uri.OriginalString;
        var x = SafeXml.Load(pkg.GetBytes(uri));
        var root = x.Root!;
        var layoutPart = sp.SlideLayoutPart;
        XDocument? layout = layoutPart != null ? SafeXml.Load(pkg.GetBytes(layoutPart.Uri.OriginalString)) : null;
        XDocument? master = layoutPart?.SlideMasterPart != null ? SafeXml.Load(pkg.GetBytes(layoutPart.SlideMasterPart.Uri.OriginalString)) : null;

        var info = new SlideInfo
        {
            Number = number, PartUri = uri, Hidden = (string?)root.Attribute("show") is "0" or "false",
            LayoutName = (string?)layout?.Root?.Element(Ns.P + "cSld")?.Attribute("name"),
        };
        info.HasAnimations = root.Element(Ns.P + "timing")?.Descendants(Ns.P + "par").Any() == true;
        info.HasTransition = root.Element(Ns.P + "transition") != null || root.Descendants().Any(e => e.Name.LocalName == "transition");
        if (sp.NotesSlidePart is { } np)
        {
            var nx = SafeXml.Load(pkg.GetBytes(np.Uri.OriginalString));
            var body = nx.Descendants(Ns.P + "sp").FirstOrDefault(s => (string?)s.Descendants(Ns.P + "ph").FirstOrDefault()?.Attribute("type") == "body");
            info.NotesText = body == null ? null : TextOf(body);
        }
        var animated = root.Element(Ns.P + "timing")?.Descendants(Ns.P + "spTgt").Select(t => (string?)t.Attribute("spid")).Where(s => s != null).ToHashSet() ?? [];
        var hlRels = sp.HyperlinkRelationships.ToDictionary(h => h.Id, h => h.Uri.OriginalString);

        var tree = root.Element(Ns.P + "cSld")?.Element(Ns.P + "spTree");
        if (tree == null) { model.Slides.Add(info); return; }
        int index = 0;
        var allText = new StringBuilder();
        var objs = new List<DocObject>();

        void Walk(XElement container, Xf xf, int depth, string? parent)
        {
            foreach (var raw in container.Elements())
            {
                var el = raw;
                if (el.Name == Ns.Mc + "AlternateContent")
                {
                    var choice = el.Element(Ns.Mc + "Choice");
                    if (choice == null) continue;
                    foreach (var c in choice.Elements()) Handle(c, xf, depth, parent);
                    continue;
                }
                Handle(el, xf, depth, parent);
            }
        }

        void Handle(XElement el, Xf xf, int depth, string? parent)
        {
            var ln = el.Name.LocalName;
            if (el.Name.Namespace != Ns.P || ln is not ("sp" or "pic" or "grpSp" or "graphicFrame" or "cxnSp" or "contentPart")) return;
            var nvPr = el.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("nv"));
            var cNvPr = nvPr?.Element(Ns.P + "cNvPr");
            if (cNvPr == null) return;
            var id = int.TryParse((string?)cNvPr.Attribute("id"), out var iv) ? iv : 0;
            var loc = Locators.ForNv(id);
            var ph = nvPr!.Element(Ns.P + "nvPr")?.Element(Ns.P + "ph");
            var phType = ph == null ? null : (string?)ph.Attribute("type") ?? "obj";
            var phIdx = (string?)ph?.Attribute("idx");

            var xfrmEl = ln switch
            {
                "graphicFrame" => el.Element(Ns.P + "xfrm"),
                "grpSp" => el.Element(Ns.P + "grpSpPr")?.Element(Ns.A + "xfrm"),
                _ => el.Element(Ns.P + "spPr")?.Element(Ns.A + "xfrm"),
            };
            Bounds? local = ReadXfrm(xfrmEl);
            if (local == null && ph != null) local = Inherited(layout, master, phType!, phIdx);
            var bounds = local == null ? null : xf.Map(local);

            var type = ln switch
            {
                "pic" => nvPr.Element(Ns.P + "nvPr")?.Elements().Any(e => e.Name.LocalName is "videoFile" or "audioFile" || e.Name.LocalName == "extLst" && e.Descendants().Any(d => d.Name.LocalName == "media")) == true ? ObjectType.Media : ObjectType.Picture,
                "grpSp" => ObjectType.Group,
                "graphicFrame" => GraphicKind(el),
                "cxnSp" => ObjectType.Shape,
                _ => el.Element(Ns.P + "nvSpPr")?.Element(Ns.P + "cNvSpPr")?.Attribute("txBox") is { } tb && (string)tb == "1" ? ObjectType.TextBox : ObjectType.Shape,
            };
            var text = el.Element(Ns.P + "txBody") is { } txb ? TextOf(txb) : null;
            if (!string.IsNullOrWhiteSpace(text)) allText.AppendLine(text);

            var extra = new Dictionary<string, string> { ["kind"] = ln };
            if (cNvPr.Attribute("hidden") is { } hid && (string)hid is "1" or "true") extra["hidden"] = "1";
            if (animated.Contains(id.ToString())) extra["animated"] = "1";
            if (ln == "cxnSp") extra["connector"] = "1";
            // Bilde (eller figur fylt med bilde)
            var blip = (ln == "pic" ? el.Element(Ns.P + "blipFill") : el.Element(Ns.P + "spPr")?.Element(Ns.A + "blipFill"))?.Element(Ns.A + "blip");
            string? imgUri = null, imgCt = null;
            if ((string?)blip?.Attribute(Ns.R + "embed") is { } rid)
                try { var ip = sp.GetPartById(rid); imgUri = ip.Uri.OriginalString; imgCt = ip.ContentType; } catch { }
            if (blip != null && ln == "sp") extra["imageFill"] = "1";
            var src = (ln == "pic" ? el.Element(Ns.P + "blipFill") : null)?.Element(Ns.A + "srcRect");
            if (type == ObjectType.Media) extra["media"] = "1";

            if (type == ObjectType.Chart)
            {
                var cid = (string?)el.Descendants().FirstOrDefault(e => e.Name == Ns.C + "chart")?.Attribute(Ns.R + "id");
                if (cid != null) try
                {
                    var cp = sp.GetPartById(cid);
                    extra["chartPart"] = cp.Uri.OriginalString;
                    if (ChartReader.Read(pkg.GetBytes(cp.Uri.OriginalString)) is { } cd) extra["chartData"] = cd.Summary();
                } catch { }
            }
            if (type == ObjectType.SmartArt)
            {
                var dm = (string?)el.Descendants().FirstOrDefault(e => e.Name.LocalName == "relIds")?.Attribute(Ns.R + "dm");
                if (dm != null) try
                {
                    var dp = sp.GetPartById(dm);
                    var dx = SafeXml.Load(pkg.GetBytes(dp.Uri.OriginalString));
                    var nodes = dx.Descendants().Where(e => e.Name.LocalName == "pt" && (string?)e.Attribute("type") is null or "node")
                        .Select(pt => string.Concat(pt.Descendants(Ns.A + "t").Select(t => t.Value))).Where(s => s.Length > 0).ToList();
                    extra["smartArtText"] = string.Join(" → ", nodes);
                } catch { }
            }
            if (type == ObjectType.Table && el.Descendants(Ns.A + "tbl").FirstOrDefault() is { } tbl)
            {
                var tblPr = tbl.Element(Ns.A + "tblPr");
                extra["headerRow"] = (string?)tblPr?.Attribute("firstRow") is "1" or "true" ? "1" : "0";
                extra["rows"] = tbl.Elements(Ns.A + "tr").Count().ToString();
                extra["cols"] = tbl.Element(Ns.A + "tblGrid")?.Elements(Ns.A + "gridCol").Count().ToString() ?? "0";
                extra["merged"] = tbl.Descendants(Ns.A + "tc").Any(c => c.Attribute("gridSpan") != null || c.Attribute("rowSpan") != null || c.Attribute("hMerge") != null || c.Attribute("vMerge") != null) ? "1" : "0";
                text = string.Join(" | ", tbl.Elements(Ns.A + "tr").FirstOrDefault()?.Elements(Ns.A + "tc").Select(c => string.Concat(c.Descendants(Ns.A + "t").Select(t => t.Value))) ?? []);
            }

            var obj = new DocObject
            {
                Ref = new ObjectRef(uri, loc), Type = type, Name = (string?)cNvPr.Attribute("name"), NativeId = id,
                AltText = (string?)cNvPr.Attribute("descr"), AltTitle = (string?)cNvPr.Attribute("title"),
                Decorative = DecorativeOf(cNvPr), Text = text, SlideNumber = number, ParentLocator = parent, Depth = depth,
                TreeIndex = index++, Bounds = bounds, ImagePartUri = imgUri, ImageContentType = imgCt,
                Crop = src == null ? null : new Crop(Int(src, "l"), Int(src, "t"), Int(src, "r"), Int(src, "b")),
                PlaceholderType = phType, IsPlaceholder = ph != null, Extra = extra,
                Source = new SourceRef(uri, Locators.XPathOf(cNvPr)), Fingerprint = SafeXml.Fingerprint(cNvPr),
            };
            objs.Add(obj);
            if (depth == 0) info.ReadingOrder.Add(loc);

            if (phType is "title" or "ctrTitle" && !string.IsNullOrWhiteSpace(text))
            {
                info.TitleText ??= text!.Trim();
                info.HasTitlePlaceholder = true;
                if (bounds != null && bounds.IsOutside(model.SlideWidthEmu, model.SlideHeightEmu)) info.TitleIsHiddenOffSlide = true;
            }
            else if (phType is "title" or "ctrTitle") info.HasTitlePlaceholder = true;

            // Lenker i teksten
            foreach (var h in el.Descendants(Ns.A + "hlinkClick"))
            {
                var hrid = (string?)h.Attribute(Ns.R + "id");
                if (string.IsNullOrEmpty(hrid)) continue;
                var run = h.Ancestors(Ns.A + "r").FirstOrDefault();
                objs.Add(new DocObject
                {
                    Ref = new ObjectRef(uri, Locators.ForIndex("hl", HlIndex(x, h))), Type = ObjectType.Hyperlink,
                    Text = run != null ? string.Concat(run.Elements(Ns.A + "t").Select(t => t.Value)) : (string?)cNvPr.Attribute("name"),
                    HyperlinkTarget = hlRels.GetValueOrDefault(hrid), HyperlinkExternal = hlRels.ContainsKey(hrid), SlideNumber = number,
                    ContextBefore = text, ParentLocator = loc, Extra = new() { ["onShape"] = run == null ? "1" : "0" },
                    Source = new SourceRef(uri, Locators.XPathOf(h)),
                });
            }

            if (ln == "grpSp")
            {
                var gx = el.Element(Ns.P + "grpSpPr")?.Element(Ns.A + "xfrm");
                var off = gx?.Element(Ns.A + "off"); var ext = gx?.Element(Ns.A + "ext");
                var chOff = gx?.Element(Ns.A + "chOff"); var chExt = gx?.Element(Ns.A + "chExt");
                var child = gx == null || chExt == null ? xf : new Xf(
                    xf.Map(new Bounds(L(off, "x"), L(off, "y"), 0, 0)).X, xf.Map(new Bounds(L(off, "x"), L(off, "y"), 0, 0)).Y,
                    Math.Max(1, L(ext, "cx")) * xf.ScaleNum, Math.Max(1, L(chExt, "cx")) * xf.ScaleDenX,
                    Math.Max(1, L(ext, "cy")) * xf.ScaleNumY, Math.Max(1, L(chExt, "cy")) * xf.ScaleDenY,
                    L(chOff, "x"), L(chOff, "y"));
                Walk(el, child, depth + 1, loc);
            }
        }

        foreach (var r in tree.Descendants(Ns.A + "r"))
        {
            var lang = (string?)r.Element(Ns.A + "rPr")?.Attribute("lang") ?? model.DefaultLanguage ?? "(ikke satt)";
            var len = string.Concat(r.Elements(Ns.A + "t").Select(t => t.Value)).Length;
            model.RunLanguages[lang] = model.RunLanguages.GetValueOrDefault(lang) + len;
        }
        Walk(tree, Xf.Identity, 0, null);
        info.AllVisibleText = allText.ToString();
        model.Slides.Add(info);
        model.Objects.AddRange(objs);
    }

    private static int HlIndex(XDocument x, XElement h) =>
        x.Descendants().Where(e => (e.Name == Ns.W + "hyperlink" || e.Name == Ns.A + "hlinkClick") && !Locators.InFallback(e)).ToList().IndexOf(h);

    private static ObjectType GraphicKind(XElement gf)
    {
        var uri = (string?)gf.Descendants(Ns.A + "graphicData").FirstOrDefault()?.Attribute("uri") ?? "";
        if (uri.EndsWith("/table")) return ObjectType.Table;
        if (uri.EndsWith("/chart") || uri.Contains("chartex")) return ObjectType.Chart;
        if (uri.EndsWith("/diagram")) return ObjectType.SmartArt;
        if (uri.EndsWith("/ole")) return ObjectType.OleObject;
        return ObjectType.Shape;
    }

    private static Bounds? ReadXfrm(XElement? xfrm)
    {
        if (xfrm == null) return null;
        var off = xfrm.Element(Ns.A + "off"); var ext = xfrm.Element(Ns.A + "ext");
        if (off == null || ext == null) return null;
        return new Bounds(L(off, "x"), L(off, "y"), L(ext, "cx"), L(ext, "cy"), (int?)xfrm.Attribute("rot") ?? 0);
    }

    private static Bounds? Inherited(XDocument? layout, XDocument? master, string type, string? idx)
    {
        foreach (var d in new[] { layout, master })
        {
            if (d == null) continue;
            foreach (var sp in d.Descendants(Ns.P + "sp"))
            {
                var ph = sp.Descendants(Ns.P + "ph").FirstOrDefault();
                if (ph == null) continue;
                var t = (string?)ph.Attribute("type") ?? "obj";
                var i = (string?)ph.Attribute("idx");
                bool match = (idx != null && i == idx) || (t == type) || (type == "ctrTitle" && t == "title") || (type == "subTitle" && t == "body");
                if (!match) continue;
                var b = ReadXfrm(sp.Element(Ns.P + "spPr")?.Element(Ns.A + "xfrm"));
                if (b != null) return b;
            }
        }
        return null;
    }

    public static string TextOf(XElement txBody) =>
        string.Join("\n", txBody.Elements(Ns.A + "p").Select(p => string.Concat(p.Elements().Select(r =>
            r.Name == Ns.A + "r" || r.Name == Ns.A + "fld" ? string.Concat(r.Elements(Ns.A + "t").Select(t => t.Value)) : r.Name == Ns.A + "br" ? "\n" : ""))))
        .Trim();

    private static long L(XElement? e, string a) => (long?)e?.Attribute(a) ?? 0;
    private static int Int(XElement e, string a) => int.TryParse((string?)e.Attribute(a), out var v) ? v : 0;

    public static bool DecorativeOf(XElement nv) =>
        nv.Element(Ns.A + "extLst")?.Elements(Ns.A + "ext").Any(x => (string?)x.Attribute("uri") == Ns.DecorativeExtUri &&
            (string?)x.Element(Ns.Adec + "decorative")?.Attribute("val") is "1" or "true") == true;
}

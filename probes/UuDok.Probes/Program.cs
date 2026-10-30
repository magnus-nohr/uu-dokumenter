using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using UuDok.Core;
using UuDok.Core.Opc;
using UuDok.Patching;
using UuDok.Rendering;

// Tekniske bevis (fase A). Kjøres mot ekte Word/PowerPoint på denne maskinen.
// Bruk: UuDok.Probes <repo-rot>
var root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var corpus = Path.Combine(root, "testkorpus");
var outDir = Path.Combine(root, "probes", "output");
if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
Directory.CreateDirectory(outDir);
var helper = Path.Combine(root, "src", "UuDok.WinHelper", "bin", "Debug", "net10.0-windows10.0.19041.0", "UuDok.WinHelper.exe");
var renderer = new OfficeRenderer(helper, TimeSpan.FromMinutes(4));
var R = new Probe.Recorder();
R.Meta["maskin"] = Environment.MachineName.Length > 0 ? "lokal Windows-maskin" : "";
R.Meta["os"] = Environment.OSVersion.VersionString;
R.Meta["dato"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

var pptx = Path.Combine(corpus, "office-presentasjon.pptx");
var docx = Path.Combine(corpus, "office-dokument.docx");

await Probe.Run(R, "P7-kalibrering", "Rendering av samme fil to ganger (renderingsstøy)", async r =>
{
    var a = await renderer.RenderAsync(pptx, DocumentKind.Pptx, Path.Combine(outDir, "p7", "a"), default);
    var b = await renderer.RenderAsync(pptx, DocumentKind.Pptx, Path.Combine(outDir, "p7", "b"), default);
    R.Meta["powerpoint"] = a.EngineVersion;
    r.Observe($"PowerPoint-rendering: {a.Pages.Count} lysbilder, {a.Duration.TotalSeconds:0.0} s og {b.Duration.TotalSeconds:0.0} s.");
    var diffs = a.Pages.Zip(b.Pages).Select(p => ImageCompare.Compare(p.First, p.Second, 0)).ToList();
    r.Observe($"PowerPoint: maks avvikende piksler per lysbilde ved toleranse 0: {diffs.Max(d => d.DifferentPixels)}; maks kanalavvik {diffs.Max(d => d.MaxChannelDelta)}.");
    var wa = await renderer.RenderAsync(docx, DocumentKind.Docx, Path.Combine(outDir, "p7", "wa"), default);
    var wb = await renderer.RenderAsync(docx, DocumentKind.Docx, Path.Combine(outDir, "p7", "wb"), default);
    R.Meta["word"] = wa.EngineVersion;
    r.Observe($"Word-rendering (PDF → PNG via Windows.Data.Pdf): {wa.Pages.Count} sider, {wa.Duration.TotalSeconds:0.0} s og {wb.Duration.TotalSeconds:0.0} s.");
    var wd = wa.Pages.Zip(wb.Pages).Select(p => ImageCompare.Compare(p.First, p.Second, 0)).ToList();
    r.Observe($"Word: maks avvikende piksler per side ved toleranse 0: {wd.Max(d => d.DifferentPixels)}; maks kanalavvik {wd.Max(d => d.MaxChannelDelta)}.");
    r.Pass(diffs.All(d => d.SameSize) && wd.All(d => d.SameSize), "Renderingen er deterministisk nok til å kalibrere toleranser.");
});

await Probe.Run(R, "P1-alt-pptx", "Alternativ tekst i PPTX uten andre endringer", async r =>
{
    var pkg = Probe.Load(pptx);
    var s2 = Probe.FindPic(pkg, "/ppt/slides/slide2.xml", "Bilde celle");
    var s7 = Probe.FindPic(pkg, "/ppt/slides/slide7.xml", "Skjermbilde");
    var changes = new List<PlannedChange>
    {
        Probe.Change(ChangeKind.SetAltText, "/ppt/slides/slide2.xml", s2, "Planteselle med cellevegg ytterst og cellekjerne i midten."),
        Probe.Change(ChangeKind.SetAltText, "/ppt/slides/slide7.xml", s7, "Skjermbilde av et program der knappen «Lagre» er uthevet med rød ramme.", old: "tekst, datamaskin, tegnefilm, innendørs"),
    };
    var res = await Probe.PatchAndVerify(r, pkg, changes, DocumentKind.Pptx, pptx, Path.Combine(outDir, "p1"), renderer, new PatchEnv());
    var shapes = res.After!["slides"]!.AsArray().SelectMany(s => s!["shapes"]!.AsArray()).ToList();
    var alt1 = shapes.First(s => (string?)s!["name"] == "Bilde celle")!["altText"]?.ToString();
    var alt2 = shapes.First(s => (string?)s!["name"] == "Skjermbilde")!["altText"]?.ToString();
    r.Observe($"PowerPoint leser AlternativeText: «{alt1}» og «{alt2}».");
    r.Pass(res.Ok && alt1 == changes[0].Value && alt2 == changes[1].Value, "Bare descr-attributtet ble endret; PowerPoint leser den nye teksten; ingen visuelle avvik.");
});

await Probe.Run(R, "P1-alt-docx", "Alternativ tekst i DOCX uten andre endringer", async r =>
{
    var pkg = Probe.Load(docx);
    var changes = new List<PlannedChange>
    {
        Probe.Change(ChangeKind.SetAltText, "/word/document.xml", Probe.FindDocPr(pkg, "Bilde 1"), "Planteselle med cellevegg ytterst og cellekjerne i midten."),
    };
    var res = await Probe.PatchAndVerify(r, pkg, changes, DocumentKind.Docx, docx, Path.Combine(outDir, "p1d"), renderer, new PatchEnv());
    var alts = res.After!["inlineAltTexts"]!.AsArray().Select(x => x!.ToString()).ToList();
    r.Observe($"Word leser InlineShape.AlternativeText: [{string.Join(" | ", alts)}]");
    r.Pass(res.Ok && alts.Contains(changes[0].Value), "Bare descr på wp:docPr endret; Word leser den nye teksten; ingen visuelle avvik.");
});

await Probe.Run(R, "P2-ukjente-utvidelser", "Ukjente XML-utvidelser og urørte pakkedeler beholdes", async r =>
{
    foreach (var (src, kind, part, loc) in new[] {
        (pptx, DocumentKind.Pptx, "/ppt/slides/slide2.xml", (string?)null),
        (docx, DocumentKind.Docx, "/word/document.xml", "docPr-navn:Bilde 1") })
    {
        var inj = Path.Combine(outDir, "p2", "injisert-" + Path.GetFileName(src));
        Directory.CreateDirectory(Path.GetDirectoryName(inj)!);
        UuDok.Tools.Crafted.InjectUnknown(src, inj);
        var pkg = Probe.Load(inj);
        var target = loc == null ? Probe.FindPic(pkg, part, "Bilde celle") : Probe.FindDocPr(pkg, loc["docPr-navn:".Length..]);
        var ch = new List<PlannedChange> { Probe.Change(ChangeKind.SetAltText, part, target, "Planteselle.") };
        var res = await Probe.PatchAndVerify(r, pkg, ch, kind, inj, Path.Combine(outDir, "p2", kind.ToString()), renderer, new PatchEnv());
        var outPkg = OpcPackage.Load(new MemoryStream(res.Output));
        var x = Encoding.UTF8.GetString(outPkg.GetBytes(part));
        bool attr = x.Contains("uux:merke=\"bevares\""), elem = x.Contains("ukjent-element-bevares"),
             ext = kind == DocumentKind.Docx || x.Contains("ukjent-ext-bevares"),
             partOk = outPkg.Contains("/uudok/ukjent-del.xml") && outPkg.GetBytes("/uudok/ukjent-del.xml").SequenceEqual(pkg.GetBytes("/uudok/ukjent-del.xml")),
             relOk = Encoding.UTF8.GetString(outPkg.GetBytes("/_rels/.rels")).Contains("urn:uudok:test-relasjon");
        r.Observe($"{kind}: ukjent attributt {Ja(attr)}, ukjent element {Ja(elem)}, ukjent extLst {Ja(ext)}, ukjent del byte-lik {Ja(partOk)}, ukjent relasjon {Ja(relOk)}.");
        r.Pass(res.Ok && attr && elem && ext && partOk && relOk, $"{kind}: alt bevart.");
    }
});

await Probe.Run(R, "P3-P4-leserekkefolge", "Leserekkefølge og lagrekkefølge i PowerPoint", async r =>
{
    var pkg = Probe.Load(pptx);
    var hits = pkg.Entries.Where(e => e.Name.StartsWith("ppt/") && e.Name.EndsWith(".xml"))
        .Where(e => Encoding.UTF8.GetString(e.Data).Contains("readingOrder", StringComparison.OrdinalIgnoreCase)).Select(e => e.Name).ToList();
    r.Observe(hits.Count == 0
        ? "Ingen egen leserekkefølge-representasjon (søk etter «readingOrder») i filen PowerPoint lagret. Rekkefølgen i p:spTree er eneste rekkefølge."
        : "Fant mulig leserekkefølge-markup i: " + string.Join(", ", hits));

    // Eksperiment: bytt rekkefølge på (a) to overlappende figurer og (b) to ikke-overlappende figurer.
    async Task<(JsonNode s, List<string> pngs)> Variant(string name, Func<XDocument, bool> mutate)
    {
        var doc = SafeXml.Load(pkg.GetBytes("/ppt/slides/slide4.xml"));
        if (!mutate(doc)) throw new InvalidOperationException("Fant ikke figurene.");
        using var ms = new MemoryStream();
        pkg.WriteTo(ms, new Dictionary<string, byte[]> { ["/ppt/slides/slide4.xml"] = SafeXml.Save(doc, pkg.GetBytes("/ppt/slides/slide4.xml")) });
        var f = Path.Combine(outDir, "p3", name + ".pptx"); Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllBytes(f, ms.ToArray());
        var o = await renderer.RenderAsync(f, DocumentKind.Pptx, Path.Combine(outDir, "p3", name), default);
        return (o.Structure, o.Pages.ToList());
    }
    static bool Swap(XDocument d, string n1, string n2)
    {
        var tree = d.Descendants(Ns.P + "spTree").First();
        var a = tree.Elements().FirstOrDefault(e => e.Descendants().Any(c => c.Name.LocalName == "cNvPr" && (string?)c.Attribute("name") == n1));
        var b = tree.Elements().FirstOrDefault(e => e.Descendants().Any(c => c.Name.LocalName == "cNvPr" && (string?)c.Attribute("name") == n2));
        if (a == null || b == null) return false;
        var tmp = new XElement("tmp");
        a.AddBeforeSelf(tmp); a.Remove();
        b.AddBeforeSelf(a); b.Remove();
        tmp.ReplaceWith(b);
        return true;
    }
    var orig = await Variant("original", _ => true);
    var overl = await Variant("bytt-overlappende", d => Swap(d, "Boks A", "Boks B"));
    var separate = await Variant("bytt-ikke-overlappende", d => Swap(d, "Gruppe dyr", "Dekorlinje"));
    string Order(JsonNode s) => string.Join(" → ", s["slides"]![3]!["shapes"]!.AsArray().OrderBy(x => (int)x!["zOrder"]!).Select(x => (string)x!["name"]!));
    r.Observe("Original rekkefølge (ZOrderPosition 1→n): " + Order(orig.s));
    r.Observe("Etter bytte av overlappende «Boks A»/«Boks B»: " + Order(overl.s));
    var d1 = ImageCompare.Compare(orig.pngs[3], overl.pngs[3], 0, Path.Combine(outDir, "p3", "diff-overlappende.png"));
    r.Observe($"Pikselavvik etter bytte av overlappende figurer: {d1.DifferentPixels} piksler ({d1.DifferentRatio:P3}).");
    var d2 = ImageCompare.Compare(orig.pngs[3], separate.pngs[3], 0, Path.Combine(outDir, "p3", "diff-ikke-overlappende.png"));
    r.Observe("Etter bytte av naboene «Gruppe dyr»/«Dekorlinje» (overlapper ikke): " + Order(separate.s));
    r.Observe($"Pikselavvik etter bytte av ikke-overlappende naboer: {d2.DifferentPixels} piksler.");
    var animOrig = orig.s["slides"]![3]!["shapes"]!.AsArray().Where(x => (bool)x!["hasAnimation"]!).Select(x => (string)x!["name"]!);
    var animNew = overl.s["slides"]![3]!["shapes"]!.AsArray().Where(x => (bool)x!["hasAnimation"]!).Select(x => (string)x!["name"]!);
    r.Observe($"Animert figur før/etter: {string.Join(",", animOrig)} / {string.Join(",", animNew)} (p:timing peker på cNvPr-id, ikke posisjon).");
    r.Pass(d1.DifferentPixels > 0 && d2.DifferentPixels == 0,
        "Leserekkefølge = rekkefølge i figurtreet = lagrekkefølge. Omordning av overlappende figurer endrer utseendet; ikke-overlappende gjør det ikke.");
});

await Probe.Run(R, "P5-skjult-tittel", "Lysbildetittel uten synlig endring (KI-bilde med tekst)", async r =>
{
    var pkg = Probe.Load(pptx);
    var pres = SafeXml.Load(pkg.GetBytes("/ppt/presentation.xml"));
    var sz = pres.Root!.Element(Ns.P + "sldSz")!;
    var env = new PatchEnv((long)sz.Attribute("cx")!, (long)sz.Attribute("cy")!, "nb-NO");
    var changes = new List<PlannedChange>
    {
        Probe.Change(ChangeKind.AddHiddenSlideTitle, "/ppt/slides/slide3.xml", "part", "Fotosyntese"),
        Probe.Change(ChangeKind.AddHiddenSlideTitle, "/ppt/slides/slide8.xml", "part", "Vannets kretsløp"),
    };
    var res = await Probe.PatchAndVerify(r, pkg, changes, DocumentKind.Pptx, pptx, Path.Combine(outDir, "p5"), renderer, env);
    foreach (var i in new[] { 2, 7 })
    {
        var s = res.After!["slides"]![i]!;
        var title = s["shapes"]!.AsArray().FirstOrDefault(x => (int?)x!["placeholderType"] == 1);
        r.Observe($"Lysbilde {i + 1}: HasTitle={s["hasTitle"]}, tittel «{s["titleText"]}», Top={title?["top"]} pt, ZOrder={title?["zOrder"]}.");
    }
    r.Pass(res.Ok && (bool)res.After!["slides"]![2]!["hasTitle"]! && (bool)res.After!["slides"]![7]!["hasTitle"]!,
        "PowerPoint gjenkjenner tittelplassholderen (HasTitle) uten synlig endring. Skjermleseropplevelse må bekreftes manuelt (NVDA-protokoll).");
});

await Probe.Run(R, "P6-overskrifter-word", "Overskriftsnivå i Word uten endret layout", async r =>
{
    var pkg = Probe.Load(docx);
    var doc = SafeXml.Load(pkg.GetBytes("/word/document.xml"));
    var paras = Locators.Live(doc, Ns.W + "p").ToList();
    int Idx(string text) => paras.FindIndex(p => string.Concat(p.Descendants(Ns.W + "t").Where(t => !t.Ancestors(Ns.W + "txbxContent").Any()).Select(t => t.Value)) == text);
    var i1 = Idx("Prosjektplan for naturfag"); var i2 = Idx("Økt 1");
    var changes = new List<PlannedChange>
    {
        Probe.Change(ChangeKind.SetOutlineLevel, "/word/document.xml", $"p:{i1}", "1"),
        Probe.Change(ChangeKind.SetOutlineLevel, "/word/document.xml", $"p:{i2}", "2"),
    };
    var res = await Probe.PatchAndVerify(r, pkg, changes, DocumentKind.Docx, docx, Path.Combine(outDir, "p6"), renderer, new PatchEnv());
    var pi = res.After!["paragraphInfo"]!.AsArray();
    var before = res.Before!["paragraphInfo"]!.AsArray();
    // Words avsnittsliste (hoveddelen) teller ikke avsnitt i tekstbokser; sammenlign derfor før/etter per Word-indeks.
    var changed = pi.Zip(before).Where(z => (int)z.First!["outlineLevel"]! != (int)z.Second!["outlineLevel"]!)
        .Select(z => $"avsnitt {z.First!["index"]} ({z.First!["style"]}): {z.Second!["outlineLevel"]} → {z.First!["outlineLevel"]}").ToList();
    r.Observe("Word OutlineLevel endret (10 = brødtekst): " + string.Join("; ", changed));
    r.Observe($"Sider før/etter: {res.Before!["pages"]} / {res.After!["pages"]}. Sidebrudd-signatur (sidetall per avsnitt) lik: {Ja(Probe.PageSignature(res.Before) == Probe.PageSignature(res.After))}.");
    var levels = pi.Zip(before).Where(z => (int)z.First!["outlineLevel"]! != (int)z.Second!["outlineLevel"]!).Select(z => (int)z.First!["outlineLevel"]!).ToList();
    r.Pass(res.Ok && levels.SequenceEqual([1, 2]) && Probe.PageSignature(res.Before) == Probe.PageSignature(res.After),
        "w:outlineLvl gir navigasjonsnivå 1 og 2 uten layoutendring. Om skjermleser kunngjør det som overskrift må testes manuelt.");
});

await Probe.Run(R, "P6b-docx-uten-metadata", "Tittel (ny core.xml), språk og alt-tekst i SDK-fil", async r =>
{
    var f = Path.Combine(corpus, "sdk-mangler-mye.docx");
    var pkg = Probe.Load(f);
    var changes = new List<PlannedChange>
    {
        Probe.Change(ChangeKind.SetDocumentTitle, "/docProps/core.xml", "part", "Årsplan i samfunnsfag"),
        Probe.Change(ChangeKind.SetDocumentLanguage, "/word/styles.xml", "part", "nb-NO"),
        Probe.Change(ChangeKind.SetAltText, "/word/document.xml", "docPr:1:0", "Planteselle med cellevegg og cellekjerne."),
        Probe.Change(ChangeKind.SetAltText, "/word/document.xml", "docPr:1:1", "Søylediagram: antall elever per trinn.", old: "graf"),
        Probe.Change(ChangeKind.SetAltText, "/word/document.xml", "vml:0", "Planteselle (eldre bildeformat)."),
    };
    var res = await Probe.PatchAndVerify(r, pkg, changes, DocumentKind.Docx, f, Path.Combine(outDir, "p6b"), renderer, new PatchEnv());
    r.Pass(res.Ok, "Ny kjerneegenskapsdel med relasjon og innholdstype; Word åpner filen og viser den likt.");
});

await Probe.Run(R, "P8-ocr", "Lokal OCR av KI-bilde med tekst (Windows.Media.Ocr)", async r =>
{
    foreach (var img in new[] { "ki-tittelbilde.png", "ki-tittelbilde-skjev.png", "lite-uskarpt.png" })
    {
        var json = Path.Combine(outDir, "ocr-" + img + ".json");
        var p = Process.Start(new ProcessStartInfo(helper, ["ocr", "--in", Path.Combine(corpus, "img", img), "--json", json]) { CreateNoWindow = true })!;
        await p.WaitForExitAsync();
        var n = JsonNode.Parse(File.ReadAllText(json))!;
        if (n["error"] != null) { r.Observe($"{img}: FEIL {n["message"]}"); continue; }
        r.Observe($"{img}: språk {n["language"]}; linjer: {string.Join(" | ", n["lines"]!.AsArray().Select(l => $"«{l!["text"]}» (h={(double)l["h"]!:0})"))}. Tilgjengelige OCR-språk: {string.Join(", ", n["availableLanguages"]!.AsArray())}");
    }
    r.Pass(true, "Se observasjoner for kvalitet.");
});

R.Write(Path.Combine(root, "docs", "tekniske-bevis"));
Console.WriteLine(R.Summary());
return 0;

static string Ja(bool b) => b ? "ja" : "NEI";

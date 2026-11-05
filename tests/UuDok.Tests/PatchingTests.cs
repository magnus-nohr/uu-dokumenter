using System.Text;
using System.Xml.Linq;
using UuDok.Core;
using UuDok.Core.Opc;
using UuDok.Patching;

namespace UuDok.Tests;

/// <summary>Pakkedeler, relasjoner, tillatte XML-endringer, validering og tilbakerulling.</summary>
public class PatchingTests
{
    private static PlannedChange C(ChangeKind k, string part, string loc, string value, string? old = null, string? fp = null) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..6], Kind = k, Target = new ObjectRef(part, loc), Value = value, OldValue = old,
        ExpectedFingerprint = fp, Origin = ChangeOrigin.Deterministic,
    };

    private static string PicLoc(OpcPackage pkg, string part, string name)
    {
        var d = SafeXml.Load(pkg.GetBytes(part));
        return Locators.ForNv(int.Parse((string)Locators.LiveNonVisualProps(d).First(e => (string?)e.Attribute("name") == name).Attribute("id")!));
    }

    [Fact]
    public void Urorte_deler_har_identiske_bytes_og_bare_descr_endres()
    {
        var pkg = Corpus.Package("office-presentasjon.pptx");
        var c = C(ChangeKind.SetAltText, "/ppt/slides/slide2.xml", PicLoc(pkg, "/ppt/slides/slide2.xml", "Bilde celle"), "Planteselle.");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv());
        Assert.Equal(ChangeStatus.Applied, c.Status);
        Assert.True(res.UntouchedVerified);
        Assert.Equal(pkg.Entries.Count - 1, res.UntouchedParts);
        var part = Assert.Single(res.ChangedParts);
        Assert.Equal("/ppt/slides/slide2.xml", part.PartUri);
        var diff = Assert.Single(part.Diffs);
        Assert.Contains("descr", diff);

        var outPkg = OpcPackage.Load(new MemoryStream(res.Output));
        Assert.Equal(pkg.Entries.Select(e => e.Name), outPkg.Entries.Select(e => e.Name));   // samme rekkefølge, ingen nye deler
        var diffs = SemanticDiff.Compare(SafeXml.Load(pkg.GetBytes("/ppt/slides/slide2.xml")), SafeXml.Load(outPkg.GetBytes("/ppt/slides/slide2.xml")));
        Assert.All(diffs, d => Assert.Equal("descr", d.Attribute!.LocalName));
    }

    [Fact]
    public void Ingen_nye_valideringsfeil_etter_endringer()
    {
        foreach (var (file, kind) in new[] { ("office-presentasjon.pptx", DocumentKind.Pptx), ("office-dokument.docx", DocumentKind.Docx) })
        {
            var bytes = Corpus.Bytes(file);
            var pkg = OpcPackage.Load(new MemoryStream(bytes));
            var model = kind == DocumentKind.Pptx ? new UuDok.Pptx.PptxAnalyzer().Analyze(bytes, pkg) : new UuDok.Docx.DocxAnalyzer().Analyze(bytes, pkg);
            var changes = model.Objects.Where(o => o.Type == ObjectType.Picture && string.IsNullOrEmpty(o.AltText))
                .Select(o => C(ChangeKind.SetAltText, o.Ref.PartUri, o.Ref.Locator, "Testbeskrivelse.", fp: o.Fingerprint)).ToList();
            changes.Add(C(ChangeKind.SetDocumentTitle, "/docProps/core.xml", "part", "Testtittel"));
            var res = new Patcher().Apply(pkg, changes, Engine.Pipeline.Env(model));
            Assert.All(changes, c => Assert.Equal(ChangeStatus.Applied, c.Status));
            var before = OoxmlValidation.Validate(bytes, kind);
            var after = OoxmlValidation.Validate(res.Output, kind);
            Assert.Empty(OoxmlValidation.NewIssues(before, after));
        }
    }

    [Fact]
    public void Endring_forkastes_naar_forutsetningen_ikke_lenger_gjelder()
    {
        var pkg = Corpus.Package("office-presentasjon.pptx");
        var c = C(ChangeKind.SetAltText, "/ppt/slides/slide2.xml", PicLoc(pkg, "/ppt/slides/slide2.xml", "Bilde celle"), "X", fp: "FEIL-FINGERAVTRYKK");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv());
        Assert.Equal(ChangeStatus.Discarded, c.Status);
        Assert.Empty(res.ChangedParts);
        Assert.Equal(pkg.Entries.Count, res.UntouchedParts);
    }

    [Fact]
    public void Eksisterende_menneskeskrevet_tekst_overskrives_ikke_uten_samsvar()
    {
        var pkg = Corpus.Package("office-presentasjon.pptx");
        // Forventer tom tekst, men bildet har «Planteselle med …» – endringen skal forkastes.
        var c = C(ChangeKind.SetAltText, "/ppt/slides/slide7.xml", PicLoc(pkg, "/ppt/slides/slide7.xml", "Celle med god tekst"), "Ny tekst", old: "");
        new Patcher().Apply(pkg, [c], new PatchEnv());
        Assert.Equal(ChangeStatus.Discarded, c.Status);
    }

    [Fact]
    public void Ukjente_utvidelser_og_deler_bevares()
    {
        var dir = Corpus.TempDir();
        var inj = Path.Combine(dir, "inj.pptx");
        UuDok.Tools.Crafted.InjectUnknown(Corpus.File("office-presentasjon.pptx"), inj);
        var pkg = OpcPackage.Load(File.OpenRead(inj));
        var c = C(ChangeKind.SetAltText, "/ppt/slides/slide2.xml", PicLoc(pkg, "/ppt/slides/slide2.xml", "Bilde celle"), "Planteselle.");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv());
        var o = OpcPackage.Load(new MemoryStream(res.Output));
        var xml = Encoding.UTF8.GetString(o.GetBytes("/ppt/slides/slide2.xml"));
        Assert.Contains("uux:merke=\"bevares\"", xml);
        Assert.Contains("ukjent-element-bevares", xml);
        Assert.Contains("ukjent-ext-bevares", xml);
        Assert.Equal(pkg.GetBytes("/uudok/ukjent-del.xml"), o.GetBytes("/uudok/ukjent-del.xml"));
        Assert.Contains("urn:uudok:test-relasjon", Encoding.UTF8.GetString(o.GetBytes("/_rels/.rels")));
    }

    [Fact]
    public void Manglende_kjerneegenskaper_opprettes_med_relasjon_og_innholdstype()
    {
        var pkg = Corpus.Package("sdk-uten-kjerneegenskaper.docx");
        Assert.False(pkg.Contains("/docProps/core.xml"));
        var c = C(ChangeKind.SetDocumentTitle, "/docProps/core.xml", "part", "Notat");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv());
        var o = OpcPackage.Load(new MemoryStream(res.Output));
        Assert.True(o.Contains("/docProps/core.xml"));
        Assert.Equal("Notat", SafeXml.Load(o.GetBytes("/docProps/core.xml")).Root!.Element(Ns.Dc + "title")!.Value);
        Assert.Contains("core-properties", Encoding.UTF8.GetString(o.GetBytes("/[Content_Types].xml")));
        Assert.True(res.UntouchedVerified);
        Assert.Empty(OoxmlValidation.NewIssues(OoxmlValidation.Validate(Corpus.Bytes("sdk-uten-kjerneegenskaper.docx"), DocumentKind.Docx), OoxmlValidation.Validate(res.Output, DocumentKind.Docx)));
    }

    [Fact]
    public void Skjult_tittel_legges_forst_i_figurtreet_og_utenfor_lysbildet()
    {
        var pkg = Corpus.Package("office-presentasjon.pptx");
        var c = C(ChangeKind.AddHiddenSlideTitle, "/ppt/slides/slide3.xml", "part", "Fotosyntese");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv(12192000, 6858000, "nb-NO"));
        Assert.Equal(ChangeStatus.Applied, c.Status);
        var d = SafeXml.Load(OpcPackage.Load(new MemoryStream(res.Output)).GetBytes("/ppt/slides/slide3.xml"));
        var first = d.Descendants(Ns.P + "spTree").First().Element(Ns.P + "grpSpPr")!.ElementsAfterSelf().First();
        Assert.Equal("title", (string?)first.Descendants(Ns.P + "ph").First().Attribute("type"));
        Assert.True(long.Parse((string)first.Descendants(Ns.A + "off").First().Attribute("y")!) + long.Parse((string)first.Descendants(Ns.A + "ext").First().Attribute("cy")!) < 0);
        // Lysbilde som allerede har tittel får ikke ny
        var c2 = C(ChangeKind.AddHiddenSlideTitle, "/ppt/slides/slide2.xml", "part", "X");
        new Patcher().Apply(pkg, [c2], new PatchEnv(12192000, 6858000));
        Assert.Equal(ChangeStatus.Discarded, c2.Status);
    }

    [Fact]
    public void Tittel_og_brodtekst_legges_bak_bildet_eller_utenfor_lysbildet()
    {
        var pkg = Corpus.Package("office-presentasjon.pptx");
        var env = new PatchEnv(12192000, 6858000, "nb-NO");
        var pic = PicLoc(pkg, "/ppt/slides/slide3.xml", "KI-bilde");
        var behind = C(ChangeKind.AddHiddenSlideText, "/ppt/slides/slide3.xml", "part", "Fotosyntese\nSlik lager plantene næring av sollys");
        behind.Params["behind"] = pic;
        var outside = C(ChangeKind.AddHiddenSlideText, "/ppt/slides/slide8.xml", "part", "Vannets kretsløp\nFordamping");
        var res = new Patcher().Apply(pkg, [behind, outside], env);
        Assert.All(new[] { behind, outside }, c => Assert.Equal(ChangeStatus.Applied, c.Status));
        Assert.True(res.UntouchedVerified);
        Assert.Empty(OoxmlValidation.NewIssues(OoxmlValidation.Validate(Corpus.Bytes("office-presentasjon.pptx"), DocumentKind.Pptx), OoxmlValidation.Validate(res.Output, DocumentKind.Pptx)));

        var o = OpcPackage.Load(new MemoryStream(res.Output));
        long Y(XElement sp) => long.Parse((string)sp.Descendants(Ns.A + "off").First().Attribute("y")!);
        var s3 = SafeXml.Load(o.GetBytes("/ppt/slides/slide3.xml")).Descendants(Ns.P + "spTree").First().Elements(Ns.P + "sp").ToList();
        Assert.All(s3.Take(2), sp => Assert.InRange(Y(sp), 0, 6858000));           // innenfor bildet (fullskjerm)
        Assert.Equal("clip", (string?)s3[1].Descendants(Ns.A + "bodyPr").First().Attribute("vertOverflow"));
        var s8 = SafeXml.Load(o.GetBytes("/ppt/slides/slide8.xml")).Descendants(Ns.P + "spTree").First().Elements(Ns.P + "sp").ToList();
        Assert.True(Y(s8[0]) < 0);                                                  // tittel over lysbildet
        Assert.True(Y(s8[1]) > 6858000);                                            // brødtekst under lysbildet
    }

    [Fact]
    public void Tom_tittelplassholder_erstattes_med_tittel_fra_bildet()
    {
        // Brukerens tilfelle: oppsett med tom «Klikk for å legge til tittel» og et KI-bilde oppå.
        var orig = Corpus.Package("office-presentasjon.pptx");
        var x = SafeXml.Load(orig.GetBytes("/ppt/slides/slide3.xml"));
        var tree = x.Descendants(Ns.P + "spTree").First();
        tree.Element(Ns.P + "grpSpPr")!.AddAfterSelf(XElement.Parse(
            "<p:sp xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'>" +
            "<p:nvSpPr><p:cNvPr id='99' name='Tittel 1'/><p:cNvSpPr><a:spLocks noGrp='1'/></p:cNvSpPr><p:nvPr><p:ph type='title'/></p:nvPr></p:nvSpPr>" +
            "<p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang='nb-NO'/></a:p></p:txBody></p:sp>"));
        using var ms = new MemoryStream();
        orig.WriteTo(ms, new Dictionary<string, byte[]> { ["/ppt/slides/slide3.xml"] = SafeXml.Save(x, orig.GetBytes("/ppt/slides/slide3.xml")) });
        var pkg = OpcPackage.Load(new MemoryStream(ms.ToArray()));

        var c = C(ChangeKind.AddHiddenSlideText, "/ppt/slides/slide3.xml", "part", "Fra chatbot til KI\nkronologisk utvikling");
        c.Params["behind"] = PicLoc(pkg, "/ppt/slides/slide3.xml", "KI-bilde");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv(12192000, 6858000, "nb-NO"));
        Assert.Equal(ChangeStatus.Applied, c.Status);
        var after = SafeXml.Load(OpcPackage.Load(new MemoryStream(res.Output)).GetBytes("/ppt/slides/slide3.xml"));
        var titles = after.Descendants(Ns.P + "ph").Where(ph => (string?)ph.Attribute("type") == "title").ToList();
        var title = Assert.Single(titles);                                         // den tomme er erstattet, ikke duplisert
        Assert.Contains("Fra chatbot til KI", title.Ancestors(Ns.P + "sp").First().Value);

        // En tittelplassholder MED tekst skal aldri erstattes.
        var c2 = C(ChangeKind.AddHiddenSlideText, "/ppt/slides/slide2.xml", "part", "X");
        new Patcher().Apply(Corpus.Package("office-presentasjon.pptx"), [c2], new PatchEnv(12192000, 6858000));
        Assert.Equal(ChangeStatus.Discarded, c2.Status);
    }

    [Fact]
    public void Brodtekst_fyller_tom_innholdsplassholder_som_kulepunkter_bak_bildet()
    {
        // Brukerens tilfelle: oppsettet «Tittel og innhold» har en tom «Klikk for å legge til tekst». Teksten fra bildet skal
        // inn i DEN (oppsettets kulepunktliste), ikke i en ny tekstboks ved siden av. En tom plassholder med tekst røres ikke.
        const string P = "xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'";
        var orig = Corpus.Package("office-presentasjon.pptx");
        var x = SafeXml.Load(orig.GetBytes("/ppt/slides/slide3.xml"));
        x.Descendants(Ns.P + "spTree").First().Element(Ns.P + "grpSpPr")!.AddAfterSelf(
            XElement.Parse($"<p:sp {P}><p:nvSpPr><p:cNvPr id='98' name='Tittel 1'/><p:cNvSpPr><a:spLocks noGrp='1'/></p:cNvSpPr><p:nvPr><p:ph type='title'/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang='nb-NO'/></a:p></p:txBody></p:sp>"),
            XElement.Parse($"<p:sp {P}><p:nvSpPr><p:cNvPr id='99' name='Plassholder for innhold 2'/><p:cNvSpPr><a:spLocks noGrp='1'/></p:cNvSpPr><p:nvPr><p:ph idx='1'/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang='nb-NO'/></a:p></p:txBody></p:sp>"));
        using var ms = new MemoryStream();
        orig.WriteTo(ms, new Dictionary<string, byte[]> { ["/ppt/slides/slide3.xml"] = SafeXml.Save(x, orig.GetBytes("/ppt/slides/slide3.xml")) });
        var input = ms.ToArray();
        var pkg = OpcPackage.Load(new MemoryStream(input));

        var c = C(ChangeKind.AddHiddenSlideText, "/ppt/slides/slide3.xml", "part", "Resonnering, agenter\nResonnering og tenking\nKI bruker mer tid på å analysere komplekse oppgaver.");
        c.Params["behind"] = PicLoc(pkg, "/ppt/slides/slide3.xml", "KI-bilde");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv(12192000, 6858000, "nb-NO"));
        Assert.Equal(ChangeStatus.Applied, c.Status);
        Assert.Empty(OoxmlValidation.NewIssues(OoxmlValidation.Validate(input, DocumentKind.Pptx), OoxmlValidation.Validate(res.Output, DocumentKind.Pptx)));

        var tree = SafeXml.Load(OpcPackage.Load(new MemoryStream(res.Output)).GetBytes("/ppt/slides/slide3.xml")).Descendants(Ns.P + "spTree").First();
        var phs = tree.Descendants(Ns.P + "ph").ToList();
        var bodyPh = Assert.Single(phs, ph => (string?)ph.Attribute("idx") == "1");       // den tomme er fylt, ikke duplisert
        Assert.DoesNotContain(phs, ph => (string?)ph.Attribute("type") == "body");      // ingen ekstra tekstboks ved siden av
        var bodySp = bodyPh.Ancestors(Ns.P + "sp").First();
        Assert.Equal(["Resonnering og tenking", "KI bruker mer tid på å analysere komplekse oppgaver."],
            bodySp.Descendants(Ns.A + "p").Select(p => string.Concat(p.Descendants(Ns.A + "t").Select(t => t.Value))));
        Assert.Empty(bodySp.Descendants(Ns.A + "buNone"));                               // kulepunkter arves fra oppsettet
        // Leserekkefølge: tittel, brødtekst, deretter bildet. Plassert innenfor bildet (bak det).
        var shapes = tree.Elements().Where(e => e.Name.LocalName is "sp" or "pic").ToList();
        Assert.Equal("title", (string?)shapes[0].Descendants(Ns.P + "ph").FirstOrDefault()?.Attribute("type"));
        Assert.Same(bodySp, shapes[1]);
        Assert.InRange(long.Parse((string)bodySp.Descendants(Ns.A + "off").First().Attribute("y")!), 0, 6858000);
    }

    [Fact]
    public void Flytting_i_leserekkefolge_godtas_som_flytting_og_ikke_som_andre_endringer()
    {
        var pkg = Corpus.Package("office-presentasjon.pptx");
        var loc = PicLoc(pkg, "/ppt/slides/slide4.xml", "Dekorlinje");
        var c = C(ChangeKind.MoveToReadingStart, "/ppt/slides/slide4.xml", loc, "first");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv());
        Assert.Equal(ChangeStatus.Applied, c.Status);
        var d = SafeXml.Load(OpcPackage.Load(new MemoryStream(res.Output)).GetBytes("/ppt/slides/slide4.xml"));
        var first = d.Descendants(Ns.P + "spTree").First().Element(Ns.P + "grpSpPr")!.ElementsAfterSelf().First();
        Assert.Equal("Dekorlinje", (string?)first.Descendants().First(e => e.Name.LocalName == "cNvPr").Attribute("name"));
    }

    [Fact]
    public void Semantisk_diff_avviser_endringer_utenfor_tillatelsen()
    {
        var a = XDocument.Parse("<r><p:sp xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main'><p:nvSpPr><p:cNvPr id='2' name='a'/></p:nvSpPr></p:sp></r>");
        var b = new XDocument(a);
        var nv = b.Descendants().First(e => e.Name.LocalName == "cNvPr");
        var al = PatchOps.SetAltText(nv, "ok", null);
        nv.SetAttributeValue("name", "endret");   // utilsiktet endring
        var diffs = SemanticDiff.Compare(a, b);
        Assert.Contains(diffs, d => !al.Covers(d));
        Assert.Contains(diffs, d => al.Covers(d));
    }

    [Fact]
    public void Dekorativ_markering_bruker_Office_sin_utvidelse()
    {
        var pkg = Corpus.Package("office-presentasjon.pptx");
        var c = C(ChangeKind.SetDecorative, "/ppt/slides/slide4.xml", PicLoc(pkg, "/ppt/slides/slide4.xml", "Dekorlinje"), "1");
        var res = new Patcher().Apply(pkg, [c], new PatchEnv());
        var d = SafeXml.Load(OpcPackage.Load(new MemoryStream(res.Output)).GetBytes("/ppt/slides/slide4.xml"));
        var nv = Locators.LiveNonVisualProps(d).First(e => (string?)e.Attribute("name") == "Dekorlinje");
        Assert.True(PatchOps.IsDecorative(nv));
        // Allerede dekorativ → ingen ny endring
        var c2 = C(ChangeKind.SetDecorative, "/ppt/slides/slide4.xml", PicLoc(pkg, "/ppt/slides/slide4.xml", "Dekorlinje"), "1");
        new Patcher().Apply(OpcPackage.Load(new MemoryStream(res.Output)), [c2], new PatchEnv());
        Assert.Equal(ChangeStatus.Discarded, c2.Status);
    }

    [Fact]
    public void Dokumentbeskyttelse_omgas_aldri()
    {
        var model = new DocumentModel { Kind = DocumentKind.Docx, HasProtection = true };
        var plan = Engine.Planner.Build(new Engine.PlanInput(model, new UuDok.Core.Rules.AnalysisResult(), new Dictionary<string, UuDok.Core.Rules.OcrInfo>(),
            new Dictionary<string, UuDok.Ai.AiResult>(), new Dictionary<string, string>(), new Engine.JobOptions(), false));
        Assert.Empty(plan.Changes);
    }
}

using UuDok.Core;
using UuDok.Core.Rules;
using UuDok.Docx;
using UuDok.Pptx;

namespace UuDok.Tests;

/// <summary>Dokumentmodell og meningsfulle regelresultater.</summary>
public class AnalysisAndRulesTests
{
    private static DocumentModel Pptx() { var b = Corpus.Bytes("office-presentasjon.pptx"); return new PptxAnalyzer().Analyze(b, Corpus.Package("office-presentasjon.pptx")); }
    private static DocumentModel Docx(string f) { var b = Corpus.Bytes(f); return new DocxAnalyzer().Analyze(b, Corpus.Package(f)); }

    [Fact]
    public void Pptx_modell_har_lysbilder_titler_bilder_grupper_og_leserekkefolge()
    {
        var m = Pptx();
        Assert.Equal(8, m.Slides.Count);
        Assert.Null(m.Slides[2].TitleText);                            // KI-bildet uten tittel
        Assert.Equal("Cellens deler", m.Slides[1].TitleText);
        Assert.Contains(m.Objects, o => o.Type == ObjectType.Group && o.Name == "Gruppe dyr");
        Assert.Contains(m.Objects, o => o.Type == ObjectType.SmartArt && o.Extra.ContainsKey("smartArtText"));
        Assert.Contains(m.Objects, o => o.Type == ObjectType.Table && o.Extra["headerRow"] == "1");
        Assert.Contains(m.Objects, o => o.Type == ObjectType.Hyperlink && o.Text == "Klikk her");
        Assert.True(m.Slides[3].HasAnimations);
        Assert.All(m.Slides, s => Assert.NotEmpty(s.ReadingOrder));
        // Stabile identiteter: del + cNvPr-id
        Assert.All(m.Objects.Where(o => o.Type == ObjectType.Picture), o => Assert.StartsWith("nv:", o.Ref.Locator));
    }

    [Fact]
    public void Docx_modell_finner_falske_overskrifter_lister_kontrast_vml_og_dupliserte_id()
    {
        var m = Docx("sdk-mangler-mye.docx");
        Assert.Null(m.Title);
        Assert.Null(m.DefaultLanguage);
        Assert.Contains(m.Objects, o => o.Extra.ContainsKey("looksLikeHeading") && o.Text == "Årsplan i samfunnsfag");
        Assert.Contains(m.Objects, o => o.Extra.ContainsKey("contrast"));
        Assert.Contains(m.Objects, o => o.Extra.GetValueOrDefault("legacy") == "vml");
        var pics = m.Objects.Where(o => o.Ref.Locator.StartsWith("docPr:1:")).ToList();
        Assert.Equal(2, pics.Count);                                   // samme docPr-id to ganger skilles med forekomst
        Assert.Contains(m.Objects, o => o.Type == ObjectType.Table && o.Extra["merged"] == "1");
        Assert.Contains(m.Objects, o => o.Caption?.StartsWith("Figur 1") == true);
    }

    [Fact]
    public void Regelregisteret_er_konsistent_og_har_kilder_med_dato()
    {
        var r = RuleRegistry.Load();
        Assert.NotEmpty(r.Rules);
        Assert.All(r.Sources, s => { Assert.StartsWith("https://", s.Url); Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", s.Checked); });
        Assert.Contains(r.Profiles, p => p.Id == "en411" && !p.Normative);  // nyeste standard er ikke merket som lovkrav
        Assert.All(r.Rules.Where(x => x.Check != "auto"), x => Assert.False(x.CanPass));
    }

    [Fact]
    public void Manglende_teststotte_gir_aldri_bestatt()
    {
        var m = Pptx();
        var res = new RuleEngine(RuleRegistry.Default).Run(m);   // uten OCR
        Assert.Equal(RuleOutcome.NotTested, res.Outcomes["ALT-TEXTIMAGE"]);
        Assert.Equal(RuleOutcome.NotTested, res.Outcomes["CONTRAST-TEXT"]);  // ikke implementert for PPTX
        Assert.Equal(RuleOutcome.NotTested, res.Outcomes["LANG-PARTS"]);     // manuell
        foreach (var rule in RuleRegistry.Default.Rules.Where(x => !x.CanPass))
            Assert.NotEqual(RuleOutcome.Passed, res.Outcomes[rule.Id]);
    }

    [Fact]
    public void Generisk_objektliste_som_alt_tekst_flagges()
    {
        var res = new RuleEngine(RuleRegistry.Default).Run(Pptx());
        Assert.Contains(res.Findings, f => f.RuleId == "ALT-POOR" && f.Description.Contains("tekst, datamaskin, tegnefilm, innendørs"));
        Assert.Contains(res.Findings, f => f.RuleId == "ALT-POOR" && f.Description.Contains("«Diagram»"));
        Assert.DoesNotContain(res.Findings, f => f.RuleId == "ALT-POOR" && f.Description.Contains("Planteselle med cellevegg"));   // god tekst beholdes
    }

    [Fact]
    public void Kontrastproblem_krever_synlig_endring_og_kan_ikke_repareres_skjult()
    {
        var res = new RuleEngine(RuleRegistry.Default).Run(Docx("sdk-mangler-mye.docx"));
        var f = Assert.Single(res.Findings, f => f.RuleId == "CONTRAST-TEXT");
        Assert.True(f.RequiresVisibleChange);
        Assert.False(f.AutoRepairSupported);
    }

    [Fact]
    public void Hvert_funn_har_paakrevde_felt()
    {
        var res = new RuleEngine(RuleRegistry.Default).Run(Docx("sdk-mangler-mye.docx"));
        Assert.All(res.Findings, f =>
        {
            Assert.False(string.IsNullOrEmpty(f.Id)); Assert.False(string.IsNullOrEmpty(f.RuleId)); Assert.False(string.IsNullOrEmpty(f.Profile));
            Assert.NotNull(f.Object); Assert.False(string.IsNullOrEmpty(f.Location)); Assert.False(string.IsNullOrEmpty(f.Description));
        });
    }

    [Theory]
    [InlineData("Dette er en tekst om elevene og det de skal lære på skolen i år, og hvordan vi kan vurdere dem.", "nb-NO")]
    [InlineData("Dette er ein tekst om elevane og det dei skal lære på skulen i år, og korleis vi kan vurdere dei.", "nn-NO")]
    [InlineData("This is a text about the students and what they will learn at school this year and how we assess them.", "en-GB")]
    public void Sprakgjenkjenning_skiller_bokmal_nynorsk_og_engelsk(string text, string lang)
    {
        Assert.Equal(lang, LanguageGuess.Detect(text).Language);
    }

    [Fact]
    public void Kontrastberegning_folger_WCAG()
    {
        Assert.Equal(21.0, Rgb.Contrast(Rgb.Black, Rgb.White), 1);
        Assert.Equal(1.6, Rgb.Contrast(Rgb.TryParse("CCCCCC")!.Value, Rgb.White), 1);
        Assert.InRange(Rgb.Contrast(Rgb.TryParse("767676")!.Value, Rgb.White), 4.5, 4.6);
    }
}

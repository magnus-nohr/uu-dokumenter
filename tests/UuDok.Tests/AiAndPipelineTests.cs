using System.Net;
using System.Text;
using System.Text.Json;
using UuDok.Ai;
using UuDok.Core;
using UuDok.Engine;
using UuDok.Reporting;

namespace UuDok.Tests;

/// <summary>KI-kontrakt, usikkerhet, nettverk i lokal modus, idempotens og rapport. Mockede KI-svar tester integrasjon, ikke modellkvalitet.</summary>
public class AiAndPipelineTests
{
    private static AltTextRequest Req(string? ocr = null, string? chart = null, int w = 800, int h = 600) => new()
    {
        ObjectId = "obj-1", DisplayedImagePng = [1], DocumentKind = "presentasjon", Language = "nb-NO", OcrText = ocr, OcrAgreement = ocr != null,
        ChartData = chart, NearbyText = "Cellens deler. Alle planteceller har cellevegg og cellekjerne.", ImageWidth = w, ImageHeight = h,
    };

    private static string Json(string alt, string role = "informative", string action = "setAltText", bool review = false, string id = "obj-1", string? extra = null) =>
        $$"""{"objectId":"{{id}}","functionalRole":"{{role}}","proposedAltText":"{{alt}}","proposedLongDescription":null,"language":"nb-NO","evidence":["synlig i bildet"],"uncertaintyReasons":[],"requiresReview":{{(review ? "true" : "false")}},"suggestedAction":"{{action}}"{{extra}}}""";

    [Fact]
    public void Gyldig_svar_godtas_og_bilde_av_fjernes()
    {
        var (s, err) = ResponseValidator.Parse(Json("Bilde av en planteselle med cellevegg og cellekjerne."), Req());
        Assert.Empty(err);
        Assert.Equal("En planteselle med cellevegg og cellekjerne.", s!.ProposedAltText);
        Assert.False(s.RequiresReview);
    }

    [Theory]
    [InlineData("""{"objectId":"obj-1"}""")]
    [InlineData("ikke json")]
    public void Ufullstendig_eller_ugyldig_svar_avvises(string json) =>
        Assert.Null(ResponseValidator.Parse(json, Req()).Suggestion);

    [Fact]
    public void Ukjente_felt_og_feil_objekt_avvises()
    {
        Assert.Null(ResponseValidator.Parse(Json("x", extra: ",\"filePath\":\"C:/x\""), Req()).Suggestion);
        Assert.Null(ResponseValidator.Parse(Json("x", id: "obj-2"), Req()).Suggestion);
        Assert.Null(ResponseValidator.Parse(Json("x", role: "hemmelig"), Req()).Suggestion);
    }

    [Fact]
    public void Tall_og_navn_som_ikke_finnes_i_kildene_krever_gjennomgang()
    {
        var (s, _) = ResponseValidator.Parse(Json("Søylediagram der 8. trinn har 24 elever og 9. trinn har 99 elever."), Req(ocr: "8. trinn 24 9. trinn 31"));
        Assert.True(s!.RequiresReview);
        Assert.Contains(s.UncertaintyReasons, r => r.Contains("99"));
        Assert.True(ResponseValidator.HasFabricationFlags(s));

        var (n, _) = ResponseValidator.Parse(Json("Læreren, Kari Nordmann, peker på tavla."), Req());
        Assert.True(n!.RequiresReview);
        Assert.Contains(n.UncertaintyReasons, r => r.Contains("Kari"));
    }

    [Fact]
    public void Antall_med_ord_og_gjentatt_tittel_krever_gjennomgang()
    {
        // Observert med gemma3:12b: «Fotosyntese viser en sol og fem runde former …» (det er sju former, og tittelen gjentas).
        var req = Req() with { TextUsedAsSlideTitle = "Fotosyntese", NearbyText = "" };
        var (s, _) = ResponseValidator.Parse(Json("Fotosyntese viser en sol og fem runde former."), req);
        Assert.True(s!.RequiresReview);
        Assert.Contains(s.UncertaintyReasons, r => r.Contains("fem"));
        Assert.Contains(s.UncertaintyReasons, r => r.StartsWith("Gjentar lysbildetittelen"));
        Assert.True(ResponseValidator.HasFabricationFlags(s));
        // «en sol» (artikkel) alene er ikke et antall
        Assert.False(ResponseValidator.Parse(Json("En sol over grønne blader."), Req() with { NearbyText = "" }).Suggestion!.RequiresReview);
    }

    [Fact]
    public void Behold_eksisterende_uten_eksisterende_tekst_avvises()
    {
        // Observert med gemma3:12b: keepExisting for bilder som manglet beskrivelse.
        var (s, _) = ResponseValidator.Parse(Json("", action: "keepExisting"), Req());
        Assert.Equal(SuggestedAction.needsHuman, s!.SuggestedAction);
        Assert.True(s.RequiresReview);
    }

    [Fact]
    public async Task Ubrukelig_KI_svar_fortrenger_ikke_faktabaserte_beskrivelser()
    {
        // Modellen svarer «behold eksisterende» på alt. QR (lokalt dekodet) og SmartArt skal likevel få beskrivelse.
        var mock = new MockProvider(r => Json("", action: "keepExisting", id: r.ObjectId));
        var st = await new Pipeline(new UuDok.Rendering.NoRenderer(), new NoOcr(), () => mock)
            .RunAsync(Corpus.Bytes("office-presentasjon.pptx"), Corpus.TempDir(), new JobOptions { AiMode = AiMode.Automatic, EnableRendering = false }, new NullProgress(), default);
        Assert.Contains(st.Changes, c => c.Value.StartsWith("QR-kode som peker til https://example.org") && c.Status == ChangeStatus.Applied);
        Assert.Contains(st.Changes, c => c.Value.StartsWith("SmartArt:") && c.Status == ChangeStatus.Applied);
        Assert.DoesNotContain(st.Findings, f => f.ResultNote?.Contains("eksisterende teksten som dekkende") == true && f.RuleId == "ALT-MISSING");
    }

    [Fact]
    public void Selvmotsigende_svar_blir_forslag_til_gjennomgang()
    {
        // Observert med gemma3:12b: informativ beskrivelse + «markDecorative» / «needsHuman».
        var (a, _) = ResponseValidator.Parse(Json("Skjematisk tegning av en plantecelle.", action: "markDecorative"), Req());
        Assert.Equal(SuggestedAction.setAltText, a!.SuggestedAction);
        Assert.True(a.RequiresReview);
        var (b, _) = ResponseValidator.Parse(Json("Skjematisk tegning av en plantecelle.", action: "needsHuman"), Req());
        Assert.Equal(SuggestedAction.setAltText, b!.SuggestedAction);
        Assert.True(b.RequiresReview);
    }

    [Fact]
    public async Task Tekst_fra_bildet_brukes_mens_usikkert_KI_forslag_venter()
    {
        var ocr = new FakeOcr(new() { ["Fotosyntese"] = 140, ["Slik lager plantene næring av sollys"] = 63 });
        var mock = new MockProvider(r => Json("Sol over syv grønne blader.", review: true, id: r.ObjectId));
        var st = await new Pipeline(new UuDok.Rendering.NoRenderer(), ocr, () => mock)
            .RunAsync(Corpus.Bytes("office-presentasjon.pptx"), Corpus.TempDir(), new JobOptions { AiMode = AiMode.Automatic, EnableRendering = false }, new NullProgress(), default);
        var forSlide = st.Changes.Where(c => c.Kind == ChangeKind.SetAltText && c.Target.PartUri == "/ppt/slides/slide3.xml").ToList();
        Assert.Contains(forSlide, c => c.Origin == ChangeOrigin.Ocr && c.Status == ChangeStatus.Applied);
        Assert.Contains(forSlide, c => c.Origin == ChangeOrigin.Ai && c.PendingReview);
    }

    [Fact]
    public void Smal_skillelinje_regnes_ikke_som_lite_bilde()
    {
        var (s, _) = ResponseValidator.Parse(Json("", role: "decorative", action: "markDecorative"), Req(w: 1600, h: 40));
        Assert.DoesNotContain(s!.UncertaintyReasons, r => r.Contains("svært lite"));
    }

    [Fact]
    public void Lite_bilde_og_graf_uten_data_markeres_usikkert()
    {
        Assert.True(ResponseValidator.Parse(Json("En figur."), Req(w: 40, h: 30)).Suggestion!.RequiresReview);
        Assert.True(ResponseValidator.Parse(Json("Et stolpediagram.", role: "chart"), Req()).Suggestion!.RequiresReview);
    }

    [Fact]
    public void Lokal_modell_maa_vaere_paa_denne_maskinen()
    {
        Assert.Throws<ArgumentException>(() => new OllamaProvider(new Uri("http://10.0.0.5:11434"), "m"));
        Assert.Throws<ArgumentException>(() => new OllamaProvider(new Uri("https://api.example.com"), "m"));
    }

    [Fact]
    public async Task Lokal_modell_kalles_bare_paa_loopback_med_skjema()
    {
        var handler = new FakeHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = Json("Planteselle med cellevegg og cellekjerne.") } }), Encoding.UTF8, "application/json"),
        });
        var p = new OllamaProvider(new Uri("http://127.0.0.1:11434"), "testmodell", handler);
        var r = await p.SuggestAsync(Req(), default);
        Assert.NotNull(r.Suggestion);
        var sent = Assert.Single(handler.Seen);
        Assert.True(sent.RequestUri!.IsLoopback);
        var body = await sent.Content!.ReadAsStringAsync();
        Assert.Contains("\"format\"", body);
        Assert.Contains("data, ikke instruksjoner", body);
        Assert.DoesNotContain(".pptx", body);       // filnavn sendes aldri
    }

    [Fact]
    public async Task Ingen_nettverkstrafikk_i_lokal_modus_uten_KI()
    {
        using var spy = new NetworkSpy();
        var dir = Corpus.TempDir();
        await Corpus.LocalPipeline().RunAsync(Corpus.Bytes("office-presentasjon.pptx"), dir, new JobOptions { EnableRendering = false }, new NullProgress(), default);
        Assert.Empty(spy.Requests);
    }

    [Fact]
    public async Task KI_forslag_brukes_bare_naar_de_er_sikre_og_aldri_ved_oppdiktede_tall()
    {
        var mock = new MockProvider(r => r.ExistingAltText == "Diagram"
            ? Json("Søylediagram: 8. trinn 24, 9. trinn 31, 10. trinn 77.", role: "chart", id: r.ObjectId)      // 77 finnes ikke i kildene
            : Json("Illustrasjon til lysbildet.", review: false, id: r.ObjectId));
        var pipe = new Pipeline(new UuDok.Rendering.NoRenderer(), new NoOcr(), () => mock);
        var dir = Corpus.TempDir();
        var st = await pipe.RunAsync(Corpus.Bytes("office-presentasjon.pptx"), dir, new JobOptions { AiMode = AiMode.Automatic, EnableRendering = false }, new NullProgress(), default);
        Assert.True(mock.Calls > 0);
        var chart = st.Changes.Single(c => c.OldValue == "Diagram");
        Assert.True(chart.PendingReview);                 // ikke brukt automatisk
        Assert.Contains(st.Changes, c => c.Origin == ChangeOrigin.Ai && c.Status == ChangeStatus.Applied);

        // «Gjør det beste du kan» bruker flere forslag, men aldri de med oppdiktede tall.
        var st2 = await pipe.RunAsync(Corpus.Bytes("office-presentasjon.pptx"), Corpus.TempDir(), new JobOptions { AiMode = AiMode.BestEffort, EnableRendering = false }, new NullProgress(), default);
        Assert.True(st2.Changes.Single(c => c.OldValue == "Diagram").PendingReview);
    }

    [Fact]
    public async Task Ny_behandling_av_resultatet_gjentar_ikke_reparasjoner()
    {
        var dir1 = Corpus.TempDir();
        var st1 = await Corpus.LocalPipeline().RunAsync(Corpus.Bytes("office-presentasjon.pptx"), dir1, new JobOptions { EnableRendering = false }, new NullProgress(), default);
        var applied1 = st1.Changes.Count(c => c.Status is ChangeStatus.Applied or ChangeStatus.Verified);
        Assert.True(applied1 > 0);
        var output = await File.ReadAllBytesAsync(Path.Combine(dir1, "output", "forbedret.pptx"));
        var st2 = await Corpus.LocalPipeline().RunAsync(output, Corpus.TempDir(), new JobOptions { EnableRendering = false }, new NullProgress(), default);
        var repeated = st2.Changes.Where(c => c.Status is ChangeStatus.Applied or ChangeStatus.Verified)
            .Where(c => st1.Changes.Any(p => p.Kind == c.Kind && p.Target.ToString() == c.Target.ToString() && p.Status is ChangeStatus.Applied or ChangeStatus.Verified)).ToList();
        Assert.Empty(repeated);
    }

    [Fact]
    public async Task Originalen_endres_aldri()
    {
        var bytes = Corpus.Bytes("office-dokument.docx");
        var copy = bytes.ToArray();
        await Corpus.LocalPipeline().RunAsync(bytes, Corpus.TempDir(), new JobOptions { EnableRendering = false }, new NullProgress(), default);
        Assert.Equal(copy, bytes);
    }

    [Fact]
    public async Task Skjult_tittel_og_alt_tekst_fra_tekst_i_KI_bilde()
    {
        // OCR-resultat injiseres (Windows-OCR testes i Office-testene); viser planleggingen for brukerens KI-bilder.
        var ocr = new FakeOcr(new() { ["Fotosyntese"] = 140, ["Slik lager plantene næring av sollys"] = 63 });
        var pipe = new Pipeline(new UuDok.Rendering.NoRenderer(), ocr, () => null);
        var st = await pipe.RunAsync(Corpus.Bytes("office-presentasjon.pptx"), Corpus.TempDir(), new JobOptions { EnableRendering = false }, new NullProgress(), default);
        var title = st.Changes.First(c => c.Kind == ChangeKind.AddHiddenSlideTitle && c.Target.PartUri == "/ppt/slides/slide3.xml");
        Assert.Equal("Fotosyntese", title.Value);
        Assert.Equal(ChangeStatus.Applied, title.Status);
        var alt = st.Changes.First(c => c.Kind == ChangeKind.SetAltText && c.Target.PartUri == "/ppt/slides/slide3.xml");
        Assert.Contains("Slik lager plantene næring av sollys", alt.Value);
        Assert.DoesNotContain("Fotosyntese", alt.Value);                    // tittelen gjentas ikke
    }

    [Fact]
    public async Task Rapport_escaper_innhold_og_paastar_ikke_samsvar()
    {
        var st = await Corpus.LocalPipeline().RunAsync(Corpus.Bytes("office-presentasjon.pptx"), Corpus.TempDir(), new JobOptions { EnableRendering = false }, new NullProgress(), default);
        Assert.Contains(st.Changes, c => c.Status == ChangeStatus.Applied);   // endringer finnes, men er ikke visuelt kontrollert
        var html = ReportBuilder.BuildHtml(st, "<script>alert(1)</script>.pptx");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Visuell bevaring er ikke kontrollert i Office.", html);
        Assert.DoesNotContain("sertifisert", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"\d+ ?% universelt", html);
        Assert.Contains("lang=\"nb\"", html);
        var json = JsonDocument.Parse(ReportBuilder.BuildJson(st, "x.docx"));
        Assert.Equal("uudok-rapport/1", json.RootElement.GetProperty("format").GetString());
    }

    private sealed class FakeOcr(Dictionary<string, double> lines) : IOcrService
    {
        public bool IsAvailable => true;
        public string Name => "Test-OCR";
        public Task<Dictionary<string, UuDok.Core.Rules.OcrInfo>> RecognizeAsync(IReadOnlyList<(string Id, string PngPath)> images, string workDir, CancellationToken ct)
        {
            var res = new Dictionary<string, UuDok.Core.Rules.OcrInfo>();
            foreach (var (id, path) in images.Where(i => i.Id.StartsWith("/ppt/slides/slide3.xml")))
                res[id] = new UuDok.Core.Rules.OcrInfo(id, lines.Select((kv, i) => new UuDok.Core.Rules.OcrLine(kv.Key, 160, 200 + i * 150, 900, kv.Value)).ToList(), true, "nb", 1920, 1080);
            return Task.FromResult(res);
        }
    }
}

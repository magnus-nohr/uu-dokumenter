using System.Diagnostics;
using UuDok.Core;
using UuDok.Engine;
using UuDok.Rendering;

namespace UuDok.Tests;

/// <summary>
/// Integrasjon med ekte Word/PowerPoint på denne maskinen: visuell regresjon, åpning uten feil og lokal OCR.
/// Hoppes over (med begrunnelse) når Office ikke er installert eller UUDOK_SKIP_OFFICE=1.
/// Kjør bare disse: UuDok.Tests.exe -trait "Category=Office"
/// </summary>
[Trait("Category", "Office")]
[Collection("office")]
public class OfficeTests
{
    private static void RequireOffice() { if (!Corpus.OfficeAvailable) Assert.Skip("Word/PowerPoint eller renderingshjelperen er ikke tilgjengelig."); }

    private static Pipeline OfficePipeline(IOcrService? ocr = null) =>
        new(new OfficeRenderer(Corpus.Helper, TimeSpan.FromMinutes(10)), ocr ?? new WindowsOcr(Corpus.Helper, TimeSpan.FromMinutes(10)), () => null);

    [Theory]
    [InlineData("office-presentasjon.pptx")]
    [InlineData("office-dokument.docx")]
    [InlineData("sdk-mangler-mye.docx")]
    public async Task Resultatet_aapnes_i_Office_og_ser_likt_ut(string file)
    {
        RequireOffice();
        var dir = Corpus.TempDir();
        var st = await OfficePipeline().RunAsync(Corpus.Bytes(file), dir, new JobOptions(), new NullProgress(), TestContext.Current.CancellationToken);
        Assert.True(st.Verification.OpenedInOffice, "Office åpnet ikke resultatet.");
        Assert.Contains(st.Verification.Visual, new[] { VisualCheck.Passed, VisualCheck.PassedAfterDiscard });
        Assert.True(st.Verification.UntouchedPartsIdentical);
        Assert.Empty(st.Verification.NewValidationErrors);
        Assert.Contains(st.Changes, c => c.Status == ChangeStatus.Verified);
        // Ingen verifisert endring kan stå på et lysbilde/en side som avvek.
        Assert.DoesNotContain(st.Changes, c => c.Status == ChangeStatus.Verified && c.StatusNote?.Contains("avvik") == true);
    }

    [Fact]
    public async Task Tekst_i_KI_bilde_blir_skjult_tittel_med_ekte_OCR()
    {
        RequireOffice();
        var st = await OfficePipeline().RunAsync(Corpus.Bytes("office-presentasjon.pptx"), Corpus.TempDir(), new JobOptions(), new NullProgress(), TestContext.Current.CancellationToken);
        var t3 = st.Changes.Single(c => c.Kind == ChangeKind.AddHiddenSlideText && c.Target.PartUri == "/ppt/slides/slide3.xml");
        Assert.Equal("Fotosyntese\nSlik lager plantene næring av sollys", t3.Value);
        Assert.Equal(ChangeStatus.Verified, t3.Status);                         // rendret likt i PowerPoint
        Assert.True(t3.Params.ContainsKey("behind"));                          // lagt bak bildet (bildet er ugjennomsiktig)
        var t8 = st.Changes.Single(c => c.Kind == ChangeKind.AddHiddenSlideText && c.Target.PartUri == "/ppt/slides/slide8.xml");
        Assert.StartsWith("Vannets kretsløp\n", t8.Value);
        // Teksten fra bildet gjentas ikke i alt-teksten.
        Assert.DoesNotContain(st.Changes, c => c.Kind == ChangeKind.SetAltText && c.Target.PartUri == "/ppt/slides/slide3.xml" && c.Value.Contains("Slik lager"));
    }

    [Fact]
    public async Task Rendering_av_samme_fil_er_stabil()
    {
        RequireOffice();
        var r = new OfficeRenderer(Corpus.Helper, TimeSpan.FromMinutes(5));
        var dir = Corpus.TempDir();
        var a = await r.RenderAsync(Corpus.File("office-presentasjon.pptx"), DocumentKind.Pptx, Path.Combine(dir, "a"), TestContext.Current.CancellationToken);
        var b = await r.RenderAsync(Corpus.File("office-presentasjon.pptx"), DocumentKind.Pptx, Path.Combine(dir, "b"), TestContext.Current.CancellationToken);
        Assert.All(a.Pages.Zip(b.Pages), p => Assert.True(VisualTolerance.Default.Accepts(ImageCompare.Compare(p.First, p.Second))));
    }

    [Fact]
    public async Task Stor_presentasjon_med_400_lysbilder()
    {
        RequireOffice();
        var big = Corpus.File("stor-400.pptx");
        if (!File.Exists(big))
        {
            var p = Process.Start(new ProcessStartInfo(Corpus.Helper, ["create-big", "--in", Corpus.File("office-presentasjon.pptx"), "--out", big, "--n", "400"]) { CreateNoWindow = true })!;
            await p.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        Assert.True(File.Exists(big), "Kunne ikke lage stor testpresentasjon.");
        var sw = Stopwatch.StartNew();
        var st = await OfficePipeline().RunAsync(await File.ReadAllBytesAsync(big, TestContext.Current.CancellationToken), Corpus.TempDir(),
            new JobOptions(), new NullProgress(), TestContext.Current.CancellationToken);
        Assert.Equal(400, st.Model.Slides.Count);
        Assert.True(st.Findings.Count > 300, $"Forventet flere hundre funn, fikk {st.Findings.Count}.");
        Assert.Contains(st.Verification.Visual, new[] { VisualCheck.Passed, VisualCheck.PassedAfterDiscard });
        // Brukeren skal ikke måtte godkjenne hvert bilde: de fleste endringer er brukt automatisk.
        var auto = st.Changes.Count(c => c.Status == ChangeStatus.Verified);
        var pending = st.Changes.Count(c => c.PendingReview);
        Assert.True(auto > pending, $"Automatisk: {auto}, til gjennomgang: {pending}");
        TestContext.Current.SendDiagnosticMessage($"400 lysbilder: {sw.Elapsed.TotalSeconds:0} s, {st.Findings.Count} funn, {auto} verifiserte endringer, {pending} til gjennomgang.");
    }
}

[CollectionDefinition("office", DisableParallelization = true)]
public sealed class OfficeCollection;

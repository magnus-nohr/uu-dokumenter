using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UuDok.Ai;
using UuDok.Core;
using UuDok.Core.Opc;
using UuDok.Core.Rules;
using UuDok.Docx;
using UuDok.Intake;
using UuDok.Patching;
using UuDok.Pptx;
using UuDok.Rendering;

namespace UuDok.Engine;

public sealed record JobOptions
{
    public ProcessingMode Mode { get; init; } = ProcessingMode.PreserveAppearance;
    public AiMode AiMode { get; init; } = AiMode.None;
    public bool EnableRendering { get; init; } = true;
    public bool AllowSignatureInvalidation { get; init; }
    public int MaxAiImages { get; init; } = 600;
    public int MaxOcrImages { get; init; } = 800;
}

public interface IProgress
{
    void Report(string stage, int percent, string? detail = null);
}

public sealed class NullProgress : IProgress { public void Report(string stage, int percent, string? detail = null) { } }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualCheck { NotRun, Passed, PassedAfterDiscard, Unavailable, Failed }

public sealed class Verification
{
    public int ValidationErrorsBefore { get; set; }
    public int ValidationErrorsAfter { get; set; }
    public List<string> NewValidationErrors { get; } = [];
    public bool UntouchedPartsIdentical { get; set; }
    public int UntouchedParts { get; set; }
    public List<string> ChangedParts { get; } = [];
    public VisualCheck Visual { get; set; } = VisualCheck.NotRun;
    public string? Engine { get; set; }
    public int Pages { get; set; }
    public List<string> VisualNotes { get; } = [];
    public bool OpenedInOffice { get; set; }
}

/// <summary>Alt som trengs for rapport, gjennomgang og ny patching. Lagres som JSON i jobbmappen (ingen dokumentbytes).</summary>
public sealed class JobState
{
    public required DocumentKind Kind { get; init; }
    public required JobOptions Options { get; init; }
    public required DocumentModel Model { get; init; }
    public required List<Finding> Findings { get; init; }
    public required Dictionary<string, RuleOutcome> Outcomes { get; init; }
    public required List<PlannedChange> Changes { get; init; }
    public Verification Verification { get; set; } = new();
    public Verification? VisibleVerification { get; set; }
    public List<string> IntakeNotes { get; init; } = [];
    public string? AiProvider { get; set; }
    public bool AiLocal { get; set; }
    public int AiCalls { get; set; }
    public int AiFailures { get; set; }
    public string? OcrEngine { get; set; }
    public int OcrImages { get; set; }
    public bool HasVisibleVariant { get; set; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.Now;
    public string RegistryVersion { get; init; } = RuleRegistry.Default.Version;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };
}

public sealed class JobRejectedException(string code, string message) : Exception(message) { public string Code { get; } = code; }

public sealed class Pipeline(IRenderer renderer, IOcrService ocr, Func<IAltTextProvider?> aiFactory)
{
    public const string OutputName = "forbedret";
    public const string VisibleName = "forbedret-synlige-endringer";

    public async Task<JobState> RunAsync(byte[] input, string workDir, JobOptions opt, IProgress progress, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        progress.Report("Kontrollerer filen", 2);
        var intake = FileInspector.Inspect(input);
        if (!intake.Accepted) throw new JobRejectedException(intake.RejectCode!, intake.RejectMessage!);
        var pkg = intake.Package!;

        progress.Report("Analyserer innholdet", 8);
        var model = intake.Kind == DocumentKind.Docx ? new DocxAnalyzer().Analyze(input, pkg) : new PptxAnalyzer().Analyze(input, pkg);
        model.IsSigned = intake.IsSigned;
        model.IsProtectedFromIntake = intake.IsProtected;
        ct.ThrowIfCancellationRequested();

        // ---- Bilder slik de vises (for OCR, KI og gjennomgang) ----
        var imgDir = Path.Combine(workDir, "img");
        Directory.CreateDirectory(imgDir);
        var prepared = new Dictionary<string, (PreparedImage Img, string Path)>();
        var pics = model.Objects.Where(o => o.ImagePartUri != null && !o.Decorative && o.Type is ObjectType.Picture or ObjectType.Shape or ObjectType.OleObject).Take(opt.MaxOcrImages).ToList();
        int k = 0;
        foreach (var o in pics)
        {
            ct.ThrowIfCancellationRequested();
            if (++k % 20 == 0) progress.Report("Forbereder bilder", 10 + 8 * k / Math.Max(1, pics.Count), $"{k} av {pics.Count}");
            var bytes = pkg.Get(o.ImagePartUri!)?.Data;
            if (bytes == null) continue;
            PreparedImage? pi;
            try { pi = ImagePrep.Prepare(bytes, o.Crop); } catch { pi = null; }
            if (pi == null) continue;
            var path = Path.Combine(imgDir, Planner.ImageKey(o) + ".png");
            await File.WriteAllBytesAsync(path, pi.DisplayedPng, ct);
            prepared[o.Ref.ToString()] = (pi, path);
        }

        // ---- Lokal OCR og QR ----
        var ocrResults = new Dictionary<string, OcrInfo>();
        if (ocr.IsAvailable && prepared.Count > 0)
        {
            progress.Report("Leser tekst i bilder (lokalt)", 20, $"{prepared.Count} bilder");
            ocrResults = await ocr.RecognizeAsync(prepared.Select(kv => (kv.Key, kv.Value.Path)).ToList(), workDir, ct);
        }
        var qr = new Dictionary<string, string>();
        foreach (var (key, (img, _)) in prepared)
        {
            if (Math.Abs(img.Width - img.Height) > Math.Max(img.Width, img.Height) * 0.2) continue;   // QR er tilnærmet kvadratisk
            try { if (ImagePrep.DecodeQr(img.DisplayedPng) is { } t) qr[key] = t; } catch { }
        }

        progress.Report("Kontrollerer regler", 28);
        var analysis = new RuleEngine(RuleRegistry.Default).Run(model, ocrResults, ocr.IsAvailable);

        // ---- KI-forslag ----
        var aiResults = new Dictionary<string, AiResult>();
        var provider = opt.AiMode == AiMode.None ? null : aiFactory();
        var state = new JobState
        {
            Kind = intake.Kind, Options = opt, Model = model, Findings = analysis.Findings, Outcomes = analysis.Outcomes, Changes = [],
            IntakeNotes = intake.Notes, OcrEngine = ocr.IsAvailable ? ocr.Name : null, OcrImages = ocrResults.Count,
            AiProvider = provider == null ? null : $"{provider.Id} ({provider.Model})", AiLocal = provider?.IsLocal ?? true,
        };
        if (provider != null && !(model.HasProtection || model.IsProtectedFromIntake))
        {
            // Sikre dekorasjoner (tynne linjer, koblinger) og QR-koder med dekodet innhold sendes ikke til KI.
            var skip = analysis.Findings.Where(f => f.RuleId == "DECOR-CANDIDATE" && f.Outcome == RuleOutcome.Failed).Select(f => f.Object.ToString())
                .Concat(qr.Keys).ToHashSet();
            var targets = analysis.Findings.Where(f => f.RuleId is "ALT-MISSING" or "ALT-POOR" or "ALT-TEXTIMAGE")
                .Select(f => f.Object.ToString()).Distinct().Where(k => prepared.ContainsKey(k) && !skip.Contains(k)).Take(opt.MaxAiImages).ToList();
            int i = 0;
            foreach (var key in targets)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report("Lager beskrivelser", 30 + 35 * i / Math.Max(1, targets.Count), $"{++i} av {targets.Count}");
                var o = model.Objects.First(x => x.Ref.ToString() == key);
                var (img, _) = prepared[key];
                var slide = o.SlideNumber is { } sn ? model.Slides.First(s => s.Number == sn) : null;
                var slideText = slide != null && slide.TitleText == null ? Planner.SlideTextFromImage(model, slide, ocrResults) : null;
                ocrResults.TryGetValue(key, out var oi);
                var req = new AltTextRequest
                {
                    ObjectId = $"obj-{i}", DisplayedImagePng = img.DisplayedPng, OriginalImagePng = img.OriginalPng,
                    DocumentKind = model.Kind == DocumentKind.Pptx ? "presentasjon" : "dokument",
                    HeadingOrSlideTitle = slide?.TitleText ?? NearestHeading(model, o), Caption = o.Caption,
                    NearbyText = slide != null ? Trunc(slide.AllVisibleText, 1500) : Trunc(string.Join(" … ", new[] { o.ContextBefore, o.ContextAfter }.Where(x => x != null)), 1500),
                    ExistingAltText = o.AltText, OcrText = oi?.Text, OcrAgreement = oi?.Agreement ?? false,
                    TextUsedAsSlideTitle = slideText?.Picture == o ? slideText.Title : null,
                    TextAlreadyOnSlide = slideText?.Picture == o ? slideText.AllText : null,
                    QrPayload = qr.GetValueOrDefault(key), Language = DocLanguage(model), ImageWidth = img.Width, ImageHeight = img.Height,
                };
                var r = await provider.SuggestAsync(req, ct);
                state.AiCalls++;
                if (r.Suggestion == null) state.AiFailures++;
                // Objekt-ID i KI-svaret er anonym; koble tilbake til ekte referanse lokalt.
                aiResults[key] = r;
            }
        }

        progress.Report("Planlegger endringer", 66);
        var renderOk = opt.EnableRendering && renderer.IsAvailable();
        var opaque = prepared.Where(kv => kv.Value.Img.Opaque).Select(kv => kv.Key).ToHashSet();
        var plan = Planner.Build(new PlanInput(model, analysis, ocrResults, aiResults, qr, opt, renderOk, opaque));
        state.Changes.AddRange(plan.Changes);
        foreach (var f in analysis.Findings)
            if (f.ChangeIds.Count > 0) f.State = FindingState.Proposed;

        await ApplyAndVerifyAsync(state, input, workDir, progress, ct);
        await SaveStateAsync(state, workDir);
        progress.Report("Ferdig", 100);
        return state;
    }

    /// <summary>
    /// Bruker planlagte endringer på originalen, validerer, rendrer og forkaster endringer som gir avvik.
    /// Kalles også etter gjennomgang. Originalen leses alltid på nytt fra <c>original.bin</c>.
    /// </summary>
    public async Task ApplyAndVerifyAsync(JobState state, byte[] original, string workDir, IProgress progress, CancellationToken ct)
    {
        var pkg = OpcPackage.Load(new MemoryStream(original));
        var env = Env(state.Model);
        var outDir = Path.Combine(workDir, "output");
        Directory.CreateDirectory(outDir);
        var ext = state.Kind == DocumentKind.Docx ? ".docx" : ".pptx";
        var before = OoxmlValidation.Validate(original, state.Kind);

        // Endringer som ble forkastet i en tidligere runde, prøves ikke igjen.
        var preserve = state.Changes.Where(c => !c.VisibleVariantOnly).ToList();
        foreach (var c in preserve.Where(c => c.Status is ChangeStatus.Applied or ChangeStatus.Verified or ChangeStatus.RolledBack)) c.Status = ChangeStatus.Planned;

        progress.Report("Bruker endringer", 70);
        var (output, ver) = await PatchVerifyLoop(state, pkg, original, preserve, env, before, Path.Combine(workDir, "render"), progress, ct, visualStrict: true);
        await File.WriteAllBytesAsync(Path.Combine(outDir, OutputName + ext), output, ct);
        state.Verification = ver;

        // Variant med synlige forbedringer: bare når brukeren har godtatt synlige endringer.
        var visible = state.Changes.Where(c => c.VisibleVariantOnly && !c.PendingReview && c.Status != ChangeStatus.Skipped).ToList();
        state.HasVisibleVariant = false;
        if (state.Options.Mode == ProcessingMode.AllowVisibleChanges && visible.Count > 0)
        {
            foreach (var c in visible) c.Status = ChangeStatus.Planned;
            var preserveApplied = preserve.Where(c => c.Status is ChangeStatus.Applied or ChangeStatus.Verified)
                .Select(c => new PlannedChange { Id = c.Id, Kind = c.Kind, Target = c.Target, Value = c.Value, OldValue = c.OldValue, ExpectedFingerprint = c.ExpectedFingerprint, Origin = c.Origin }).ToList();
            var all = preserveApplied.Concat(visible).ToList();
            var (vOut, vVer) = await PatchVerifyLoop(state, pkg, original, all, env, before, Path.Combine(workDir, "render-synlig"), progress, ct, visualStrict: false);
            await File.WriteAllBytesAsync(Path.Combine(outDir, VisibleName + ext), vOut, ct);
            state.VisibleVerification = vVer;
            state.HasVisibleVariant = true;
        }

        // Oppdater funnstatus: oppdaget → foreslått → endret → verifisert.
        foreach (var f in state.Findings)
        {
            var cs = state.Changes.Where(c => f.ChangeIds.Contains(c.Id)).ToList();
            if (cs.Count == 0) { f.State = FindingState.Detected; continue; }
            var done = cs.Where(c => c.Status is ChangeStatus.Applied or ChangeStatus.Verified).ToList();
            if (done.Count > 0 && done.All(c => c.UncertaintyReasons.Count > 0))
            {
                // Brukt, men usikker eller ufullstendig: ikke regnet som løst.
                f.State = FindingState.Changed;
                f.ResultNote = "Endret, men bør kontrolleres: " + string.Join(" ", done.SelectMany(c => c.UncertaintyReasons).Distinct());
                continue;
            }
            if (cs.Any(c => c.Status == ChangeStatus.Verified)) f.State = FindingState.Verified;
            else if (cs.Any(c => c.Status == ChangeStatus.Applied)) f.State = FindingState.Changed;
            else if (cs.Any(c => c.PendingReview)) f.State = FindingState.Proposed;
            else if (cs.All(c => c.Status is ChangeStatus.Discarded or ChangeStatus.RolledBack)) f.State = FindingState.Discarded;
            else f.State = FindingState.Deferred;
            // Synlige problemer løses aldri av skjulte endringer.
            if (f.RequiresVisibleChange && !cs.Any(c => c.VisibleVariantOnly && c.Status is ChangeStatus.Applied or ChangeStatus.Verified)) f.State = FindingState.Detected;
        }
        await SaveStateAsync(state, workDir);
    }

    private async Task<(byte[] Output, Verification Ver)> PatchVerifyLoop(JobState state, OpcPackage pkg, byte[] original, List<PlannedChange> changes,
        PatchEnv env, List<ValidationIssue> validationBefore, string renderDir, IProgress progress, CancellationToken ct, bool visualStrict)
    {
        var ver = new Verification { ValidationErrorsBefore = validationBefore.Count };
        byte[] output = original;
        bool discardedForVisual = false;
        RenderOutput? origRender = null;
        for (int round = 0; round < 4; round++)
        {
            var active = changes.Where(c => c.Status == ChangeStatus.Planned && !c.PendingReview).ToList();
            var res = new Patcher().Apply(pkg, active, env);
            output = res.Output;
            ver.UntouchedPartsIdentical = res.UntouchedVerified;
            ver.UntouchedParts = res.UntouchedParts;
            ver.ChangedParts.Clear();
            ver.ChangedParts.AddRange(res.ChangedParts.Select(p => p.PartUri));
            if (!res.UntouchedVerified)
            {
                foreach (var c in active) { c.Status = ChangeStatus.RolledBack; c.StatusNote = "Urørte deler ble endret; alt rullet tilbake."; }
                output = original;
                break;
            }
            // Open XML-validering: ingen nye feil. Forkast endringer i deler som fikk nye feil.
            var after = OoxmlValidation.Validate(output, state.Kind);
            var newIssues = OoxmlValidation.NewIssues(validationBefore, after);
            ver.ValidationErrorsAfter = after.Count;
            ver.NewValidationErrors.Clear();
            if (newIssues.Count > 0)
            {
                var parts = newIssues.Select(i => i.Part).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var c in active.Where(c => parts.Contains(c.Target.PartUri) || c.Kind == ChangeKind.SetDocumentTitle && parts.Contains("/docProps/core.xml")))
                { c.Status = ChangeStatus.Discarded; c.StatusNote = "Ville gitt nye valideringsfeil: " + newIssues.First().Description; }
                if (!active.Any(c => c.Status == ChangeStatus.Applied)) { ver.NewValidationErrors.AddRange(newIssues.Select(i => i.Description).Take(5)); output = original; }
                continue;
            }
            if (!active.Any(c => c.Status == ChangeStatus.Applied)) { ver.Visual = VisualCheck.NotRun; break; }

            if (!state.Options.EnableRendering || !renderer.IsAvailable())
            {
                ver.Visual = VisualCheck.Unavailable;
                ver.VisualNotes.Add("Visuell bevaring er ikke kontrollert i Office.");
                break;
            }
            progress.Report("Kontrollerer utseendet i Office", 80 + round * 4);
            try
            {
                origRender ??= await renderer.RenderAsync(await WriteTemp(renderDir, "original", original, state.Kind, ct), state.Kind, Path.Combine(renderDir, "original"), ct);
                var afterRender = await renderer.RenderAsync(await WriteTemp(renderDir, $"runde{round}", output, state.Kind, ct), state.Kind, Path.Combine(renderDir, $"runde{round}"), ct);
                ver.Engine = origRender.EngineVersion;
                ver.Pages = afterRender.Pages.Count;
                ver.OpenedInOffice = true;
                var bad = Compare(state, origRender, afterRender, visualStrict, ver.VisualNotes);
                if (bad.Count == 0)
                {
                    foreach (var c in active.Where(c => c.Status == ChangeStatus.Applied)) c.Status = ChangeStatus.Verified;
                    ver.Visual = discardedForVisual ? VisualCheck.PassedAfterDiscard : VisualCheck.Passed;
                    break;
                }
                discardedForVisual = true;
                // Forkast endringer i berørte lysbilder (PPTX) eller de mest risikable (DOCX).
                var toDiscard = state.Kind == DocumentKind.Pptx
                    ? active.Where(c => c.Status == ChangeStatus.Applied && bad.Contains(c.Target.PartUri)).ToList()
                    : (active.Any(c => c.Status == ChangeStatus.Applied && c.Risk > VisualRisk.None)
                        ? active.Where(c => c.Status == ChangeStatus.Applied && c.Risk > VisualRisk.None).ToList()
                        : active.Where(c => c.Status == ChangeStatus.Applied).ToList());
                if (toDiscard.Count == 0) toDiscard = active.Where(c => c.Status == ChangeStatus.Applied).ToList();
                foreach (var c in toDiscard) { c.Status = ChangeStatus.Discarded; c.StatusNote = "Forkastet: ga visuelt eller strukturelt avvik i Office."; }
                foreach (var c in active.Where(c => c.Status == ChangeStatus.Applied)) c.Status = ChangeStatus.Planned;
            }
            catch (RenderFailedException ex)
            {
                ver.Visual = VisualCheck.Failed;
                ver.VisualNotes.Add("Office kunne ikke kontrollere filen: " + ex.Message);
                if (ex.Message.Contains("åpne", StringComparison.OrdinalIgnoreCase) && origRender != null)
                {
                    // Originalen åpnet, men resultatet ikke: forkast alt.
                    foreach (var c in active.Where(c => c.Status == ChangeStatus.Applied)) { c.Status = ChangeStatus.RolledBack; c.StatusNote = "Resultatet kunne ikke åpnes i Office."; }
                    output = original;
                }
                break;
            }
        }
        if (ver.Visual == VisualCheck.NotRun && changes.All(c => c.Status != ChangeStatus.Applied && c.Status != ChangeStatus.Verified))
            ver.VisualNotes.Add("Ingen endringer ble gjort i filen.");
        return (output, ver);
    }

    /// <summary>Visuell og strukturell sammenligning. Returnerer deler (lysbilder) eller «document» som avviker.</summary>
    private static HashSet<string> Compare(JobState state, RenderOutput a, RenderOutput b, bool strict, List<string> notes)
    {
        var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tol = VisualTolerance.Default;
        if (a.Pages.Count != b.Pages.Count)
        {
            notes.Add($"Antall {(state.Kind == DocumentKind.Pptx ? "lysbilder" : "sider")} endret seg ({a.Pages.Count} → {b.Pages.Count}).");
            if (state.Kind == DocumentKind.Docx) { bad.Add("document"); return bad; }
        }
        for (int i = 0; i < Math.Min(a.Pages.Count, b.Pages.Count); i++)
        {
            var d = ImageCompare.Compare(a.Pages[i], b.Pages[i], tol.ChannelTolerance);
            if (strict && !tol.Accepts(d))
            {
                notes.Add($"{(state.Kind == DocumentKind.Pptx ? "Lysbilde" : "Side")} {i + 1}: {d.DifferentPixels} piksler avviker ({d.DifferentRatio:P2}).");
                bad.Add(state.Kind == DocumentKind.Pptx ? state.Model.Slides.ElementAtOrDefault(i)?.PartUri ?? "?" : "document");
            }
        }
        // Strukturelle kontroller
        if (state.Kind == DocumentKind.Pptx)
        {
            var sa = a.Structure["slides"]!.AsArray(); var sb = b.Structure["slides"]!.AsArray();
            for (int i = 0; i < Math.Min(sa.Count, sb.Count); i++)
            {
                var part = state.Model.Slides.ElementAtOrDefault(i)?.PartUri ?? "?";
                if ((int)sa[i]!["animationCount"]! != (int)sb[i]!["animationCount"]!) { bad.Add(part); notes.Add($"Lysbilde {i + 1}: antall animasjoner endret."); }
                if ((int)sa[i]!["mediaCount"]! != (int)sb[i]!["mediaCount"]!) { bad.Add(part); notes.Add($"Lysbilde {i + 1}: medieobjekter endret."); }
                if ((int)sa[i]!["transitionEffect"]! != (int)sb[i]!["transitionEffect"]!) { bad.Add(part); notes.Add($"Lysbilde {i + 1}: overgang endret."); }
                var after = sb[i]!["shapes"]!.AsArray().ToDictionary(s => (int)s!["id"]!);
                var origShapes = sa[i]!["shapes"]!.AsArray().ToList();
                foreach (var s in origShapes)
                {
                    if (!after.TryGetValue((int)s!["id"]!, out var t))
                    {
                        // En tom plassholder («Klikk for å legge til …») viser ingenting og kan være erstattet med vilje.
                        var emptyPlaceholder = (bool?)s["isPlaceholder"] == true && ((int?)s["textLength"] ?? 0) == 0;
                        if (!emptyPlaceholder) { bad.Add(part); notes.Add($"Lysbilde {i + 1}: en figur mangler."); }
                        continue;
                    }
                    foreach (var prop in new[] { "left", "top", "width", "height", "rotation" })
                        if (Math.Abs((double)s[prop]! - (double)t![prop]!) > 0.01) { bad.Add(part); notes.Add($"Lysbilde {i + 1}: figur {s["name"]} har endret {prop}."); break; }
                    if ((string?)s["textHash"] != (string?)t!["textHash"] && strict) { bad.Add(part); notes.Add($"Lysbilde {i + 1}: tekst endret i {s["name"]}."); }
                }
                // Relativ lagrekkefølge for overlappende figurer må være uendret.
                for (int x = 0; x < origShapes.Count; x++)
                    for (int y = x + 1; y < origShapes.Count; y++)
                    {
                        var p = origShapes[x]!; var q = origShapes[y]!;
                        if (!Overlap(p, q)) continue;
                        if (!after.TryGetValue((int)p["id"]!, out var p2) || !after.TryGetValue((int)q["id"]!, out var q2)) continue;
                        if (Math.Sign((int)p["zOrder"]! - (int)q["zOrder"]!) != Math.Sign((int)p2!["zOrder"]! - (int)q2!["zOrder"]!))
                        { bad.Add(part); notes.Add($"Lysbilde {i + 1}: lagrekkefølgen for overlappende figurer er endret."); }
                    }
            }
        }
        else
        {
            foreach (var key in new[] { "hyperlinks", "fields", "bookmarks", "comments", "revisions", "contentControls", "tables", "inlineShapes", "shapes", "paragraphs" })
                if ((int)a.Structure[key]! != (int)b.Structure[key]!) { bad.Add("document"); notes.Add($"Strukturavvik: antall {key} endret."); }
            if (strict && (string?)a.Structure["textHash"] != (string?)b.Structure["textHash"]) { bad.Add("document"); notes.Add("Teksten i dokumentet er endret."); }
            var pa = string.Join(",", a.Structure["paragraphInfo"]!.AsArray().Select(p => (int)p!["endPage"]!));
            var pb = string.Join(",", b.Structure["paragraphInfo"]!.AsArray().Select(p => (int)p!["endPage"]!));
            if (strict && pa != pb) { bad.Add("document"); notes.Add("Sidebrytningen er endret."); }
        }
        return bad;
    }

    private static bool Overlap(JsonNode p, JsonNode q)
    {
        double l1 = (double)p["left"]!, t1 = (double)p["top"]!, r1 = l1 + (double)p["width"]!, b1 = t1 + (double)p["height"]!;
        double l2 = (double)q["left"]!, t2 = (double)q["top"]!, r2 = l2 + (double)q["width"]!, b2 = t2 + (double)q["height"]!;
        return l1 < r2 && l2 < r1 && t1 < b2 && t2 < b1;
    }

    private static async Task<string> WriteTemp(string dir, string name, byte[] data, DocumentKind kind, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, name + (kind == DocumentKind.Docx ? ".docx" : ".pptx"));
        await File.WriteAllBytesAsync(f, data, ct);
        return f;
    }

    public static PatchEnv Env(DocumentModel m) => new(m.SlideWidthEmu, m.SlideHeightEmu, DocLanguage(m));

    public static string DocLanguage(DocumentModel m)
    {
        if (m.DefaultLanguage is { Length: > 0 } l && !l.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return l;
        var text = m.Kind == DocumentKind.Pptx ? string.Join(" ", m.Slides.Select(s => s.AllVisibleText)) : string.Join(" ", m.Objects.Select(o => o.Text));
        return LanguageGuess.Detect(text).Language ?? m.DefaultLanguage ?? "nb-NO";
    }

    private static string? NearestHeading(DocumentModel m, DocObject o) =>
        m.Objects.Where(x => x.Type == ObjectType.Heading && x.Ref.PartUri == o.Ref.PartUri && x.TreeIndex <= o.TreeIndex)
            .OrderByDescending(x => x.TreeIndex).FirstOrDefault()?.Text;

    private static string? Trunc(string? s, int n) => s == null ? null : s.Length <= n ? s : s[..n] + "…";

    public static async Task SaveStateAsync(JobState s, string workDir) =>
        await File.WriteAllTextAsync(Path.Combine(workDir, "state.json"), JsonSerializer.Serialize(s, JobState.Json));

    public static JobState? LoadState(string workDir)
    {
        var f = Path.Combine(workDir, "state.json");
        return File.Exists(f) ? JsonSerializer.Deserialize<JobState>(File.ReadAllText(f), JobState.Json) : null;
    }
}

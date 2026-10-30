using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using UuDok.Ai;
using UuDok.Core;
using UuDok.Engine;
using UuDok.Rendering;
using UuDok.Reporting;
using UuDok.Web.Security;

namespace UuDok.Web.Jobs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiChoice { none, local, cloud }

public sealed record JobRequest(ProcessingMode Mode, AiMode AiMode, AiChoice Ai, bool AllowSignatureInvalidation);

public sealed record ReviewDecision(string ChangeId, string Action, string? Value);

public sealed class Paths(string dataDir)
{
    public string DataDir { get; } = dataDir;
    public string Jobs => Path.Combine(DataDir, "jobs");
    public string Job(string id) => Path.Combine(Jobs, Guid.Parse(id).ToString("N"));   // Guid.Parse avviser stiangrep
    public string Original(string id) => Path.Combine(Job(id), "original.bin");
    public string Work(string id) => Path.Combine(Job(id), "work");
}

/// <summary>
/// Lokal jobbkø (én jobb om gangen) i en bakgrunnstjeneste. Jobber som var i gang ved stopp settes i kø igjen ved oppstart.
/// Avbrytelse og automatisk opprydding etter oppbevaringstiden.
/// </summary>
public sealed class JobService(JobStore store, Paths paths, SettingsService settings, ISecretStore secrets,
    Func<IRenderer> rendererFactory, Func<IOcrService> ocrFactory, ILogger<JobService> log) : BackgroundService
{
    private readonly Channel<(string Id, bool Review)> _queue = Channel.CreateUnbounded<(string, bool)>();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
    public static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() }, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Enqueue(byte[] data, string displayName, JobRequest req)
    {
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(paths.Work(id));
        File.WriteAllBytes(paths.Original(id), data);
        File.SetAttributes(paths.Original(id), FileAttributes.ReadOnly);   // originalen endres aldri
        var kind = displayName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase) ? "pptx" : "docx";
        store.Insert(new JobRow(id, DateTimeOffset.Now, DateTimeOffset.Now, JobStatus.Queued, "I kø", 0, null, SafeName(displayName), kind,
            JsonSerializer.Serialize(req, Json), null, null));
        _queue.Writer.TryWrite((id, false));
        return id;
    }

    public bool Cancel(string id)
    {
        if (_running.TryGetValue(id, out var cts)) { cts.Cancel(); return true; }
        var row = store.Get(id);
        if (row?.Status == JobStatus.Queued) { store.Update(id, JobStatus.Cancelled, "Avbrutt", 0); return true; }
        return false;
    }

    public void Delete(string id)
    {
        Cancel(id);
        store.Delete(id);
        DeleteFolder(paths.Job(id));
    }

    public string? SubmitReview(string id, IReadOnlyList<ReviewDecision> decisions)
    {
        var row = store.Get(id);
        if (row == null || row.Status != JobStatus.Done) return "Jobben er ikke klar for gjennomgang.";
        var state = Pipeline.LoadState(paths.Work(id));
        if (state == null) return "Fant ikke jobbdata.";
        foreach (var d in decisions)
        {
            var c = state.Changes.FirstOrDefault(x => x.Id == d.ChangeId);
            if (c == null) return $"Ukjent forslag: {d.ChangeId}";
            switch (d.Action)
            {
                case "accept":
                    if (c.Value.Length == 0) return "Forslaget mangler tekst; bruk «Rediger».";
                    c.PendingReview = false; c.Status = ChangeStatus.Planned; c.StatusNote = "Godtatt av bruker.";
                    c.UncertaintyReasons.Clear();   // brukeren har vurdert forslaget
                    SupersedeOthers(state, c);
                    break;
                case "edit":
                    var v = c.Kind == ChangeKind.AddHiddenSlideText   // første linje tittel, resten brødtekst
                        ? string.Join("\n", (d.Value ?? "").Replace("\r", "").Split('\n').Select(ResponseValidator.Clean).Where(l => l.Length > 0))
                        : ResponseValidator.Clean(d.Value ?? "");
                    if (v.Length == 0 || v.Length > 2000) return "Teksten må være mellom 1 og 2000 tegn.";
                    if (c.Kind == ChangeKind.SetTextColor && !System.Text.RegularExpressions.Regex.IsMatch(v, "^#?[0-9A-Fa-f]{6}$")) return "Fargen må være en heksadesimal kode, for eksempel #333333.";
                    if (c.Kind is not (ChangeKind.SetAltText or ChangeKind.AddHiddenSlideTitle or ChangeKind.AddHiddenSlideText or ChangeKind.SetDocumentTitle or ChangeKind.SetVisibleLinkText or ChangeKind.SetTextColor))
                        return "Dette forslaget kan ikke redigeres.";
                    var nc = Clone(c, v.TrimStart('#'));
                    state.Changes[state.Changes.IndexOf(c)] = nc;
                    SupersedeOthers(state, nc);
                    break;
                case "acceptBulk": // «Bruk alle forslag»: brukt uten enkeltvis kontroll, så usikkerheten beholdes i rapporten
                    if (c.Value.Length == 0) break;
                    c.PendingReview = false; c.Status = ChangeStatus.Planned; c.StatusNote = "Brukt med «Bruk alle forslag» (ikke kontrollert enkeltvis).";
                    SupersedeOthers(state, c);
                    break;
                case "keep":      // behold original
                case "skip":      // hopp over
                    c.PendingReview = false; c.Status = ChangeStatus.Skipped;
                    c.StatusNote = d.Action == "keep" ? "Brukeren valgte å beholde originalen." : "Hoppet over av bruker.";
                    break;
                case "undo":      // angre en automatisk endring
                    c.PendingReview = false; c.Status = ChangeStatus.Skipped; c.StatusNote = "Angret av bruker.";
                    break;
                default: return "Ukjent handling.";
            }
        }
        Pipeline.SaveStateAsync(state, paths.Work(id)).GetAwaiter().GetResult();
        store.Update(id, JobStatus.Reviewing, "Bruker valgene dine", 70);
        _queue.Writer.TryWrite((id, true));
        return null;
    }

    /// <summary>Bruker alle ventende forslag som har innhold. Returnerer antall, eller en feilmelding.</summary>
    public (int Count, string? Error) AcceptAll(string id)
    {
        var state = Pipeline.LoadState(paths.Work(id));
        if (state == null) return (0, "Fant ikke jobbdata.");
        var decisions = state.Changes.Where(c => c.PendingReview && c.Value.Length > 0)
            .Select(c => new ReviewDecision(c.Id, "acceptBulk", null)).ToList();
        if (decisions.Count == 0) return (0, "Det er ingen forslag å bruke.");
        return (decisions.Count, SubmitReview(id, decisions));
    }

    /// <summary>
    /// Når brukeren godtar eller redigerer en tekst, erstatter den andre tekster for samme objekt
    /// (for eksempel «Tekst i bildet: …» som ble brukt mens KI-forslaget ventet).
    /// </summary>
    private static void SupersedeOthers(JobState state, PlannedChange chosen)
    {
        foreach (var o in state.Changes.Where(o => !ReferenceEquals(o, chosen) && o.Kind == chosen.Kind && o.Target.ToString() == chosen.Target.ToString()))
        {
            o.PendingReview = false;
            o.Status = ChangeStatus.Skipped;
            o.StatusNote = $"Erstattet av {chosen.Id}.";
        }
    }

    private static PlannedChange Clone(PlannedChange c, string value) => new()
    {
        Id = c.Id, Kind = c.Kind, Target = c.Target, Value = value, OldValue = c.OldValue, ExpectedFingerprint = c.ExpectedFingerprint,
        Origin = ChangeOrigin.User, Risk = c.Risk, Visible = c.Visible, RequiresReview = false, Reason = c.Reason, FindingIds = c.FindingIds,
        Status = ChangeStatus.Planned, StatusNote = "Redigert av bruker.", PendingReview = false, UncertaintyReasons = [], Display = c.Display,
        Group = c.Group, VisibleVariantOnly = c.VisibleVariantOnly, Params = c.Params,
    };

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Gjenoppta etter omstart
        foreach (var r in store.WithStatus(JobStatus.Running, JobStatus.Queued, JobStatus.Reviewing))
        {
            var review = r.Status == JobStatus.Reviewing;
            store.Update(r.Id, review ? JobStatus.Reviewing : JobStatus.Queued, "Gjenopptatt etter omstart", 0);
            _queue.Writer.TryWrite((r.Id, review));
        }
        _ = Task.Run(() => CleanupLoop(stop), stop);
        await foreach (var (id, review) in _queue.Reader.ReadAllAsync(stop))
        {
            var row = store.Get(id);
            if (row == null || row.Status == JobStatus.Cancelled) continue;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
            _running[id] = cts;
            try { await Process(row, review, cts.Token); }
            finally { _running.TryRemove(id, out _); }
        }
    }

    private async Task Process(JobRow row, bool review, CancellationToken ct)
    {
        var id = row.Id;
        var req = JsonSerializer.Deserialize<JobRequest>(row.Options, Json)!;
        var s = settings.Get();
        var progress = new DbProgress(store, id, review ? JobStatus.Reviewing : JobStatus.Running);
        try
        {
            IAltTextProvider? ai = null;
            string? aiError = null;
            switch (req.Ai)
            {
                case AiChoice.local:
                    if (string.IsNullOrWhiteSpace(s.LocalAiModel)) aiError = "Ingen lokal modell er valgt i innstillingene.";
                    else ai = new OllamaProvider(new Uri(s.LocalAiUrl), s.LocalAiModel);
                    break;
                case AiChoice.cloud:
                    if (s.CloudConsentAt == null) aiError = "Skytjenesten er ikke godkjent i innstillingene.";
                    else if (secrets.Get("anthropic") is not { Length: > 0 } key) aiError = "Ingen API-nøkkel er lagret for skytjenesten.";
                    else ai = new AnthropicProvider(key, s.CloudModel);
                    break;
            }
            if (aiError != null) { store.Update(id, JobStatus.Failed, "Stoppet", 0, error: aiError, errorCode: "ai_config"); return; }

            var pipe = new Pipeline(s.Rendering ? rendererFactory() : new NoRenderer(), ocrFactory(), () => ai);
            var original = await File.ReadAllBytesAsync(paths.Original(id), ct);
            JobState state;
            if (review)
            {
                state = Pipeline.LoadState(paths.Work(id)) ?? throw new InvalidOperationException("Jobbdata mangler.");
                await pipe.ApplyAndVerifyAsync(state, original, paths.Work(id), progress, ct);
            }
            else
            {
                var opts = new JobOptions { Mode = req.Mode, AiMode = ai == null ? AiMode.None : req.AiMode, EnableRendering = s.Rendering, AllowSignatureInvalidation = req.AllowSignatureInvalidation };
                state = await pipe.RunAsync(original, paths.Work(id), opts, progress, ct);
            }
            progress.Report("Lager rapport", 97);
            await File.WriteAllTextAsync(Path.Combine(paths.Work(id), "rapport.html"), ReportBuilder.BuildHtml(state, row.DisplayName, s.DefaultProfile), ct);
            await File.WriteAllTextAsync(Path.Combine(paths.Work(id), "rapport.json"), ReportBuilder.BuildJson(state, row.DisplayName), ct);
            store.Update(id, JobStatus.Done, "Ferdig", 100);
        }
        catch (JobRejectedException ex) { store.Update(id, JobStatus.Rejected, "Avvist", 0, error: ex.Message, errorCode: ex.Code); }
        catch (OperationCanceledException) { store.Update(id, JobStatus.Cancelled, "Avbrutt", 0, error: "Behandlingen ble avbrutt."); }
        catch (Exception ex)
        {
            // Logg bare feiltype – aldri dokumentinnhold eller filnavn.
            log.LogError("Jobb {Id} feilet: {Type}", id, ex.GetType().Name);
            store.Update(id, JobStatus.Failed, "Feilet", 0, error: "Noe gikk galt under behandlingen. Originalfilen er uendret. Prøv igjen, eller prøv å åpne og lagre filen på nytt i Office.", errorCode: "internal");
        }
    }

    private async Task CleanupLoop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { Cleanup(); } catch (Exception ex) { log.LogWarning("Opprydding feilet: {Type}", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromHours(1), stop); } catch (OperationCanceledException) { return; }
        }
    }

    public int Cleanup()
    {
        var days = Math.Clamp(settings.Get().RetentionDays, 1, 365);
        int n = 0;
        foreach (var r in store.OlderThan(DateTimeOffset.Now.AddDays(-days)))
        {
            if (_running.ContainsKey(r.Id)) continue;
            store.Delete(r.Id); DeleteFolder(paths.Job(r.Id)); n++;
        }
        return n;
    }

    private static void DeleteFolder(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }

    public static string SafeName(string name)
    {
        var n = Path.GetFileName(name.Replace('\\', '/'));
        n = new string(n.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch) && !char.IsControl(ch)).ToArray());
        return n.Length is 0 or > 180 ? "dokument" + Path.GetExtension(n) : n;
    }

    private sealed class DbProgress(JobStore store, string id, string status) : IProgress
    {
        private string? _stage; private int _pct = -10;
        public void Report(string stage, int percent, string? detail = null)
        {
            if (stage == _stage && percent - _pct < 3) return;
            _stage = stage; _pct = percent;
            store.Update(id, status, stage, percent, detail);
        }
    }
}

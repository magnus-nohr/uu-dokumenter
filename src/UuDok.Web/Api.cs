using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
using UuDok.Core;
using UuDok.Engine;
using UuDok.Intake;
using UuDok.Reporting;
using UuDok.Rendering;
using UuDok.Web.Jobs;
using UuDok.Web.Security;

namespace UuDok.Web;

public static partial class Api
{
    public const long MaxUpload = 500L * 1024 * 1024;

    [GeneratedRegex("^[0-9a-f]{16}$")] private static partial Regex ImageKey();
    [GeneratedRegex("^[0-9a-fA-F]{32}$")] private static partial Regex JobId();

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/session", (HttpContext ctx, SettingsService settings, ISecretStore secrets, Func<IRenderer> rf, Func<IOcrService> of) =>
        {
            var token = ctx.Request.Cookies[LocalSecurityMiddleware.CsrfCookie] ?? LocalSecurityMiddleware.NewToken();
            ctx.Response.Cookies.Append(LocalSecurityMiddleware.CsrfCookie, token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = false, Path = "/" });
            var s = settings.Get();
            var r = rf();
            return Results.Ok(new
            {
                csrfToken = token,
                version = typeof(Api).Assembly.GetName().Version?.ToString(3),
                rendering = new { available = r.IsAvailable(), enabled = s.Rendering, name = r.Name },
                ocr = new { available = of().IsAvailable },
                ai = new
                {
                    localConfigured = !string.IsNullOrWhiteSpace(s.LocalAiModel), localModel = s.LocalAiModel,
                    cloudReady = s.CloudConsentAt != null && secrets.Get("anthropic") is { Length: > 0 }, cloudModel = s.CloudModel,
                },
                maxUploadMb = MaxUpload / (1024 * 1024),
                retentionDays = s.RetentionDays,
            });
        });

        api.MapPost("/jobs", async (HttpRequest req, JobService jobs) =>
        {
            if (!req.HasFormContentType) return Problem("Send filene som skjema (multipart).");
            var form = await req.ReadFormAsync();
            if (form.Files.Count == 0) return Problem("Velg minst én fil.");
            if (form.Files.Count > 20) return Problem("Du kan sende høyst 20 filer om gangen.");
            if (!Enum.TryParse<ProcessingMode>(form["mode"], true, out var mode)) mode = ProcessingMode.PreserveAppearance;
            if (!Enum.TryParse<AiMode>(form["aiMode"], true, out var aiMode)) aiMode = AiMode.Automatic;
            if (!Enum.TryParse<AiChoice>(form["ai"], true, out var ai)) ai = AiChoice.none;
            var allowSig = form["allowSignatureInvalidation"] == "true";
            var results = new List<object>();
            foreach (var f in form.Files)
            {
                var name = JobService.SafeName(f.FileName);
                if (f.Length > MaxUpload) { results.Add(new { name, error = "Filen er større enn 500 MB." }); continue; }
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms);
                var bytes = ms.ToArray();
                // Rask innholdskontroll før jobben legges i kø (filendelsen alene er ikke nok).
                var check = FileInspector.Inspect(bytes);
                if (!check.Accepted) { results.Add(new { name, error = check.RejectMessage, code = check.RejectCode }); continue; }
                var ext = check.Kind == DocumentKind.Docx ? ".docx" : ".pptx";
                if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name) + ext;
                var id = jobs.Enqueue(bytes, name, new JobRequest(mode, aiMode, ai, allowSig));
                results.Add(new { name, id, signed = check.IsSigned, isProtected = check.IsProtected });
            }
            return Results.Ok(new { jobs = results });
        }); // Størrelsesgrenser settes globalt for Kestrel og skjemaer i Program.cs.

        api.MapGet("/jobs", (JobStore store) => Results.Ok(store.List().Select(Dto)));

        api.MapGet("/jobs/{id}", (string id, JobStore store, Paths paths) =>
        {
            if (!JobId().IsMatch(id) || store.Get(id) is not { } row) return Results.NotFound(new { error = "Fant ikke jobben." });
            object? summary = null;
            if (row.Status == JobStatus.Done && Pipeline.LoadState(paths.Work(id)) is { } st)
            {
                var sum = ReportBuilder.Summarize(st);
                summary = new
                {
                    sentences = sum.Sentences, pendingReview = sum.PendingReview, remaining = sum.RemainingFindings,
                    visual = st.Verification.Visual.ToString(), hasVisibleVariant = st.HasVisibleVariant,
                    changed = st.Changes.Count(c => c.Status is ChangeStatus.Applied or ChangeStatus.Verified && !c.VisibleVariantOnly),
                    kind = st.Kind.ToString().ToLowerInvariant(),
                };
            }
            return Results.Ok(new { job = Dto(row), summary });
        });

        api.MapPost("/jobs/{id}/cancel", (string id, JobService jobs) =>
            JobId().IsMatch(id) && jobs.Cancel(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "Jobben kan ikke avbrytes." }));

        api.MapDelete("/jobs/{id}", (string id, JobService jobs) =>
        {
            if (!JobId().IsMatch(id)) return Results.NotFound();
            jobs.Delete(id);
            return Results.Ok(new { ok = true });
        });

        api.MapGet("/jobs/{id}/review", (string id, JobStore store, Paths paths) =>
        {
            if (!JobId().IsMatch(id) || store.Get(id) is null) return Results.NotFound(new { error = "Fant ikke jobben." });
            var st = Pipeline.LoadState(paths.Work(id));
            if (st == null) return Results.NotFound(new { error = "Jobben er ikke ferdig." });
            var items = st.Changes.Select(c => new
            {
                id = c.Id, kind = c.Kind.ToString(), kindText = ReportBuilder.KindText(c.Kind), group = c.Group ?? ReportBuilder.KindText(c.Kind),
                value = c.Value, oldValue = c.OldValue, status = ReportBuilder.ChangeStatusText(c), pending = c.PendingReview,
                applied = c.Status is ChangeStatus.Applied or ChangeStatus.Verified, discarded = c.Status is ChangeStatus.Discarded or ChangeStatus.RolledBack,
                editable = c.Kind is ChangeKind.SetAltText or ChangeKind.AddHiddenSlideTitle or ChangeKind.SetDocumentTitle or ChangeKind.SetVisibleLinkText or ChangeKind.SetTextColor,
                visibleOnly = c.VisibleVariantOnly, reason = c.Reason, statusNote = c.StatusNote, uncertainty = c.UncertaintyReasons,
                origin = c.Origin.ToString(),
                image = c.Display.TryGetValue("image", out var k) && File.Exists(Path.Combine(paths.Work(id), "img", k + ".png")) ? $"/api/jobs/{id}/image/{k}" : null,
                display = c.Display.Where(kv => kv.Key != "image").ToDictionary(kv => kv.Key, kv => kv.Value),
            });
            return Results.Ok(new { items });
        });

        api.MapPost("/jobs/{id}/review", async (string id, HttpRequest req, JobService jobs) =>
        {
            if (!JobId().IsMatch(id)) return Results.NotFound();
            ReviewBody? body;
            try { body = await JsonSerializer.DeserializeAsync<ReviewBody>(req.Body, JobService.Json); }
            catch (JsonException) { return Problem("Ugyldig forespørsel."); }
            if (body?.Decisions is not { Count: > 0 and <= 5000 }) return Problem("Ingen valg ble sendt.");
            var err = jobs.SubmitReview(id, body.Decisions);
            return err == null ? Results.Ok(new { ok = true }) : Problem(err);
        });

        api.MapGet("/jobs/{id}/image/{key}", (string id, string key, Paths paths) =>
        {
            if (!JobId().IsMatch(id) || !ImageKey().IsMatch(key)) return Results.NotFound();
            var f = Path.Combine(paths.Work(id), "img", key + ".png");
            return File.Exists(f) ? Results.File(f, "image/png") : Results.NotFound();
        });

        api.MapGet("/jobs/{id}/download", (string id, string? variant, JobStore store, Paths paths) =>
        {
            if (!JobId().IsMatch(id) || store.Get(id) is not { Status: JobStatus.Done } row) return Results.NotFound(new { error = "Filen er ikke klar." });
            var ext = row.Kind == "pptx" ? ".pptx" : ".docx";
            var visible = variant == "visible";
            var f = Path.Combine(paths.Work(id), "output", (visible ? Pipeline.VisibleName : Pipeline.OutputName) + ext);
            if (!File.Exists(f)) return Results.NotFound(new { error = "Varianten finnes ikke." });
            var name = Path.GetFileNameWithoutExtension(row.DisplayName) + (visible ? "-synlige-forbedringer" : "-forbedret") + ext;
            var ct = row.Kind == "pptx" ? "application/vnd.openxmlformats-officedocument.presentationml.presentation" : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            return Results.File(f, ct, name);
        });

        api.MapGet("/jobs/{id}/report.{fmt}", (string id, string fmt, bool? download, HttpContext ctx, JobStore store, Paths paths) =>
        {
            if (!JobId().IsMatch(id) || fmt is not ("html" or "json") || store.Get(id) is not { Status: JobStatus.Done } row) return Results.NotFound();
            var f = Path.Combine(paths.Work(id), "rapport." + fmt);
            if (!File.Exists(f)) return Results.NotFound();
            if (fmt == "html")
                ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'none'; sandbox";
            var name = Path.GetFileNameWithoutExtension(row.DisplayName) + "-rapport." + fmt;
            return Results.File(f, fmt == "html" ? "text/html; charset=utf-8" : "application/json; charset=utf-8", download == true ? name : null);
        });

        // ---- Innstillinger ----
        api.MapGet("/settings", (SettingsService s, ISecretStore secrets) => Results.Ok(SettingsDto(s.Get(), secrets)));

        api.MapPut("/settings", async (HttpRequest req, SettingsService svc, ISecretStore secrets) =>
        {
            SettingsBody? b;
            try { b = await JsonSerializer.DeserializeAsync<SettingsBody>(req.Body, JobService.Json); }
            catch (JsonException) { return Problem("Ugyldige innstillinger."); }
            if (b == null) return Problem("Ugyldige innstillinger.");
            var cur = svc.Get();
            if (b.LocalAiUrl != null && (!Uri.TryCreate(b.LocalAiUrl, UriKind.Absolute, out var u) || !u.IsLoopback || u.Scheme is not ("http" or "https")))
                return Problem("Lokal KI må ha en adresse på denne maskinen, for eksempel http://127.0.0.1:11434.");
            if (b.RetentionDays is < 1 or > 365) return Problem("Oppbevaringstid må være mellom 1 og 365 dager.");
            if (b.CloudModel != null && !Regex.IsMatch(b.CloudModel, "^[a-z0-9.-]{3,64}$")) return Problem("Ugyldig modellnavn.");
            if (b.LocalAiModel != null && !Regex.IsMatch(b.LocalAiModel, "^[A-Za-z0-9._:/-]{0,100}$")) return Problem("Ugyldig modellnavn.");
            var next = cur with
            {
                LocalAiUrl = b.LocalAiUrl ?? cur.LocalAiUrl,
                LocalAiModel = b.LocalAiModel == null ? cur.LocalAiModel : (b.LocalAiModel.Length == 0 ? null : b.LocalAiModel),
                CloudModel = b.CloudModel ?? cur.CloudModel,
                RetentionDays = b.RetentionDays ?? cur.RetentionDays,
                Rendering = b.Rendering ?? cur.Rendering,
                DefaultProfile = b.DefaultProfile is "no" or "en321" or "en411" or "bp" ? b.DefaultProfile : cur.DefaultProfile,
                CloudConsentAt = b.CloudConsent switch { true => cur.CloudConsentAt ?? DateTimeOffset.Now, false => null, null => cur.CloudConsentAt },
            };
            svc.Save(next);
            return Results.Ok(SettingsDto(next, secrets));
        });

        api.MapPut("/settings/cloud-key", async (HttpRequest req, ISecretStore secrets) =>
        {
            var b = await JsonSerializer.DeserializeAsync<KeyBody>(req.Body, JobService.Json);
            if (!secrets.IsAvailable) return Problem("Sikker nøkkellagring (Windows Legitimasjonsbehandling) er ikke tilgjengelig. Skytjenesten kan ikke brukes.");
            if (b?.Key is not { Length: > 20 and < 400 } key || key.Any(char.IsWhiteSpace)) return Problem("Nøkkelen ser ikke gyldig ut.");
            secrets.Set("anthropic", key);
            return Results.Ok(new { ok = true });
        });

        api.MapDelete("/settings/cloud-key", (ISecretStore secrets) => { secrets.Delete("anthropic"); return Results.Ok(new { ok = true }); });

        api.MapPost("/settings/test-local", async (SettingsService svc) =>
        {
            var s = svc.Get();
            try
            {
                using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(s.LocalAiUrl), Timeout = TimeSpan.FromSeconds(5) };
                var json = await http.GetFromJsonAsync<JsonElement>("/api/tags");
                var models = json.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()).Where(n => n != null).ToList();
                return Results.Ok(new { ok = true, models });
            }
            catch (Exception) { return Results.Ok(new { ok = false, error = "Fikk ikke kontakt med lokal KI-tjeneste på " + s.LocalAiUrl + "." }); }
        });
    }

    private static object SettingsDto(AppSettings s, ISecretStore secrets) => new
    {
        s.LocalAiUrl, s.LocalAiModel, s.CloudProvider, s.CloudModel, cloudConsent = s.CloudConsentAt != null, s.CloudConsentAt,
        cloudKeyStored = secrets.Get("anthropic") is { Length: > 0 }, secureStorage = secrets.IsAvailable,
        s.RetentionDays, s.Rendering, s.DefaultProfile, consentText = AppSettings.CloudConsentText,
    };

    private static object Dto(JobRow r) => new
    {
        id = r.Id, name = r.DisplayName, kind = r.Kind, status = r.Status, stage = r.Stage, percent = r.Percent, detail = r.Detail,
        error = r.Error, errorCode = r.ErrorCode, created = r.Created, updated = r.Updated,
    };

    private static IResult Problem(string msg) => Results.BadRequest(new { error = msg });

    public sealed record ReviewBody(List<ReviewDecision> Decisions);
    public sealed record SettingsBody(string? LocalAiUrl, string? LocalAiModel, string? CloudModel, int? RetentionDays, bool? Rendering, bool? CloudConsent, string? DefaultProfile);
    public sealed record KeyBody(string Key);
}

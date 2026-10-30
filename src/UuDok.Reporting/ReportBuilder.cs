using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UuDok.Core;
using UuDok.Core.Rules;
using UuDok.Engine;

namespace UuDok.Reporting;

public sealed record ReportSummary(List<string> Sentences, int AltTexts, int Decorative, int SlideTitles, int PendingReview,
    int ContrastProblems, int LinkProblems, int Discarded, string VisualStatement, int RemainingFindings);

/// <summary>Rapport i JSON og tilgjengelig HTML. Ingen skript; alt dokumentinnhold escapes.</summary>
public static class ReportBuilder
{
    private static readonly CultureInfo Nb = CultureInfo.GetCultureInfo("nb-NO");

    public static ReportSummary Summarize(JobState s)
    {
        bool Done(PlannedChange c) => c.Status is ChangeStatus.Applied or ChangeStatus.Verified && !c.VisibleVariantOnly;
        var alt = s.Changes.Count(c => Done(c) && c.Kind == ChangeKind.SetAltText);
        var dec = s.Changes.Count(c => Done(c) && c.Kind == ChangeKind.SetDecorative);
        var titles = s.Changes.Count(c => Done(c) && c.Kind == ChangeKind.AddHiddenSlideTitle);
        var pending = s.Changes.Count(c => c.PendingReview && !c.VisibleVariantOnly);
        var pendingVisible = s.Changes.Count(c => c.PendingReview && c.VisibleVariantOnly);
        var contrast = s.Findings.Count(f => f.RuleId == "CONTRAST-TEXT");
        var links = s.Findings.Count(f => f.RuleId is "LINK-PURPOSE" or "LINK-RAWURL");
        var discarded = s.Changes.Count(c => c.Status is ChangeStatus.Discarded or ChangeStatus.RolledBack);
        var remaining = s.Findings.Count(f => f.State is not (FindingState.Changed or FindingState.Verified) && f.Severity > Severity.Info);
        var engineName = s.Kind == DocumentKind.Pptx ? "PowerPoint" : "Word";
        var visual = s.Verification.Visual switch
        {
            VisualCheck.Passed => $"Utseendet er sammenlignet i {engineName} på denne maskinen. Ingen avvik ble funnet.",
            VisualCheck.PassedAfterDiscard => $"Utseendet er sammenlignet i {engineName} på denne maskinen. Endringer som ga avvik er forkastet.",
            VisualCheck.NotRun => "Ingen endringer trengte visuell kontroll.",
            _ => "Visuell bevaring er ikke kontrollert i Office.",
        };
        var list = new List<string>();
        if (s.Model.HasProtection || s.Model.IsProtectedFromIntake) list.Add("Dokumentet er beskyttet mot redigering. Det er bare analysert, ikke endret.");
        if (s.Model.IsSigned && !s.Options.AllowSignatureInvalidation) list.Add("Dokumentet er digitalt signert. Det er bare analysert, ikke endret.");
        if (alt > 0) list.Add($"{alt} {(alt == 1 ? "bilde har" : "bilder har")} fått beskrivelse.");
        if (dec > 0) list.Add(dec == 1 ? "1 objekt er markert som dekorativt." : $"{dec} objekter er markert som dekorative.");
        if (titles > 0) list.Add($"{titles} {(titles == 1 ? "lysbilde har" : "lysbilder har")} fått en skjult tittel uten synlig endring.");
        foreach (var c in s.Changes.Where(c => Done(c) && c.Kind == ChangeKind.SetDocumentTitle)) list.Add($"Dokumenttittelen er satt til «{c.Value}».");
        foreach (var c in s.Changes.Where(c => Done(c) && c.Kind == ChangeKind.SetDocumentLanguage)) list.Add($"Standardspråket er satt til {c.Value}.");
        var moved = s.Changes.Count(c => Done(c) && c.Kind == ChangeKind.MoveToReadingStart);
        if (moved > 0) list.Add($"Tittelen leses nå først på {moved} {(moved == 1 ? "lysbilde" : "lysbilder")}.");
        var heads = s.Changes.Count(c => Done(c) && c.Kind == ChangeKind.SetOutlineLevel);
        if (heads > 0) list.Add($"{heads} avsnitt er merket som overskrifter uten endret utseende.");
        var tbl = s.Changes.Count(c => Done(c) && c.Kind == ChangeKind.MarkTableHeaderRow);
        if (tbl > 0) list.Add($"{tbl} {(tbl == 1 ? "tabell har" : "tabeller har")} fått markert overskriftsrad.");
        var unsure = s.Changes.Count(c => Done(c) && c.UncertaintyReasons.Count > 0);
        if (unsure > 0) list.Add(unsure == 1 ? "1 utført endring er merket som usikker eller ufullstendig og bør kontrolleres." : $"{unsure} utførte endringer er merket som usikre eller ufullstendige og bør kontrolleres.");
        if (pending > 0) list.Add(pending == 1 ? "1 forslag bør gjennomgås." : $"{pending} forslag bør gjennomgås.");
        if (contrast > 0) list.Add(contrast == 1 ? "1 kontrastproblem krever synlig endring." : $"{contrast} kontrastproblemer krever synlige endringer.");
        if (links > 0) list.Add(links == 1 ? "1 lenke har lenketekst som bør forbedres (synlig endring)." : $"{links} lenker har lenketekst som bør forbedres (synlig endring).");
        if (pendingVisible > 0) list.Add(pendingVisible == 1 ? "1 synlig forbedring kan velges i gjennomgangen." : $"{pendingVisible} synlige forbedringer kan velges i gjennomgangen.");
        if (s.HasVisibleVariant)
        {
            var vis = s.Changes.Count(c => c.VisibleVariantOnly && c.Status is ChangeStatus.Applied or ChangeStatus.Verified);
            list.Add($"En egen fil med {vis} synlige {(vis == 1 ? "forbedring" : "forbedringer")} du har valgt, er laget. Den forbedrede filen uten synlige endringer finnes fortsatt.");
        }
        if (discarded > 0) list.Add($"{discarded} {(discarded == 1 ? "endring ble" : "endringer ble")} forkastet fordi forutsetningene ikke holdt eller resultatet avvek.");
        if (alt + dec + titles + moved + heads + tbl == 0 && !s.Changes.Any(Done)) list.Add("Ingen endringer ble gjort i filen.");
        list.Add(visual);
        if (s.AiProvider == null) list.Add("Ingen KI ble brukt. Beskrivelser er bare laget fra dokumentets egne data (diagrammer, SmartArt, QR, tekst i bilder).");
        return new ReportSummary(list, alt, dec, titles, pending, contrast, links, discarded, visual, remaining);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string BuildJson(JobState s, string displayName)
    {
        var reg = RuleRegistry.Default;
        return JsonSerializer.Serialize(new
        {
            format = "uudok-rapport/1",
            file = displayName,
            created = s.Created,
            kind = s.Kind,
            options = s.Options,
            registry = new { reg.Version, reg.SourcesChecked, reg.Disclaimer },
            summary = Summarize(s),
            verification = s.Verification,
            visibleVariantVerification = s.VisibleVerification,
            ai = new { provider = s.AiProvider, local = s.AiLocal, calls = s.AiCalls, failures = s.AiFailures },
            ocr = new { engine = s.OcrEngine, images = s.OcrImages },
            ruleOutcomes = reg.Rules.Select(r => new { rule = r.Id, r.Title, outcome = s.Outcomes.GetValueOrDefault(r.Id, RuleOutcome.NotTested), r.Check, r.Refs }),
            findings = s.Findings,
            changes = s.Changes.Select(c => new { c.Id, c.Kind, target = c.Target.ToString(), c.Value, c.OldValue, c.Origin, c.Status, c.StatusNote, c.PendingReview, c.RequiresReview, c.UncertaintyReasons, c.Risk, c.VisibleVariantOnly, c.FindingIds, c.Reason }),
            notes = s.IntakeNotes.Concat(s.Model.Notes),
        }, Json);
    }

    public static string OutcomeText(RuleOutcome o) => o switch
    {
        RuleOutcome.Passed => "Bestått", RuleOutcome.Failed => "Ikke bestått", RuleOutcome.NeedsReview => "Krever vurdering",
        RuleOutcome.NotApplicable => "Ikke relevant", _ => "Ikke testet",
    };

    public static string StateText(FindingState s) => s switch
    {
        FindingState.Detected => "Oppdaget", FindingState.Proposed => "Foreslått", FindingState.Changed => "Endret",
        FindingState.Verified => "Endret og verifisert", FindingState.Discarded => "Forslag forkastet", _ => "Utsatt",
    };

    public static string ChangeStatusText(PlannedChange c) => c.PendingReview ? "Venter på gjennomgang" : c.Status switch
    {
        ChangeStatus.Verified => "Utført og verifisert", ChangeStatus.Applied => "Utført (ikke visuelt kontrollert)",
        ChangeStatus.Discarded => "Forkastet", ChangeStatus.RolledBack => "Rullet tilbake", ChangeStatus.Skipped => "Ikke utført", _ => "Planlagt",
    };

    public static string KindText(ChangeKind k) => k switch
    {
        ChangeKind.SetAltText => "Alternativ tekst", ChangeKind.SetDecorative => "Markert som dekorativ", ChangeKind.SetDocumentTitle => "Dokumenttittel",
        ChangeKind.SetDocumentLanguage => "Dokumentspråk", ChangeKind.SetOutlineLevel => "Overskriftsnivå", ChangeKind.AddHiddenSlideTitle => "Skjult lysbildetittel",
        ChangeKind.MarkTableHeaderRow => "Tabelloverskrift", ChangeKind.MoveToReadingStart => "Leserekkefølge", ChangeKind.SetVisibleLinkText => "Lenketekst (synlig)",
        ChangeKind.SetTextColor => "Tekstfarge (synlig)", _ => k.ToString(),
    };

    public static string BuildHtml(JobState s, string displayName, string profileId = "no")
    {
        var reg = RuleRegistry.Default;
        var profile = reg.Profiles.FirstOrDefault(p => p.Id == profileId) ?? reg.Profiles[0];
        var sum = Summarize(s);
        string E(string? x) => WebUtility.HtmlEncode(x ?? "");
        var sb = new StringBuilder();
        sb.Append("""
        <!doctype html>
        <html lang="nb">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:">
        """);
        sb.Append($"<title>Tilgjengelighetsrapport – {E(displayName)}</title>");
        sb.Append("""
        <style>
        :root{color-scheme:light dark;--bg:#fff;--fg:#1a1a1a;--muted:#4a4a4a;--line:#c8c8c8;--accent:#0b5cad;--ok:#1b6e2e;--bad:#a4161a;--warn:#7a4b00}
        @media (prefers-color-scheme:dark){:root{--bg:#141414;--fg:#f0f0f0;--muted:#c4c4c4;--line:#555;--accent:#8cc4ff;--ok:#7fd99a;--bad:#ff9a9a;--warn:#ffcf7a}}
        body{background:var(--bg);color:var(--fg);font:1rem/1.55 system-ui,"Segoe UI",sans-serif;max-width:70rem;margin:0 auto;padding:1rem}
        h1{font-size:1.6rem}h2{font-size:1.3rem;margin-top:2rem;border-bottom:1px solid var(--line)}h3{font-size:1.1rem}
        table{border-collapse:collapse;width:100%;margin:.5rem 0}th,td{border:1px solid var(--line);padding:.4rem .5rem;text-align:left;vertical-align:top}
        th{background:color-mix(in srgb,var(--fg) 6%,transparent)}caption{text-align:left;font-weight:600;padding:.3rem 0}
        .ok{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--warn)}.muted{color:var(--muted)}
        a{color:var(--accent)}ul.summary li{margin:.2rem 0}
        .wrap{overflow-x:auto}
        @media print{a{color:inherit}}
        </style>
        </head>
        <body>
        <main>
        """);
        sb.Append($"<h1>Tilgjengelighetsrapport</h1><p><strong>Fil:</strong> {E(displayName)}<br><strong>Laget:</strong> {s.Created.ToLocalTime().ToString("d. MMMM yyyy 'kl.' HH:mm", Nb)}<br>");
        sb.Append($"<strong>Modus:</strong> {(s.Options.Mode == ProcessingMode.PreserveAppearance ? "Bevar utseendet" : "Tillat synlige forbedringer")}<br>");
        sb.Append($"<strong>KI:</strong> {E(s.AiProvider == null ? "Ikke brukt" : s.AiProvider + (s.AiLocal ? " – lokal behandling" : " – skytjeneste valgt av brukeren"))}<br>");
        sb.Append($"<strong>Tekst i bilder:</strong> {E(s.OcrEngine ?? "Ikke lest (OCR ikke tilgjengelig)")}</p>");

        sb.Append("<h2 id=\"oppsummering\">Oppsummering</h2><ul class=\"summary\">");
        foreach (var x in sum.Sentences) sb.Append($"<li>{E(x)}</li>");
        sb.Append("</ul>");
        sb.Append($"<p class=\"muted\">{E(reg.Disclaimer)} Rapporten sier ikke at dokumentet er «universelt utformet» eller oppfyller WCAG fullt ut.</p>");

        // Regelresultater
        sb.Append($"<h2 id=\"regler\">Kontrollresultater – {E(profile.Name)}</h2><p>{E(profile.Basis)}</p><p class=\"muted\">«Resultat» gjelder originalfilen. «Etter behandling» viser hvor mange funn som er løst i den forbedrede filen.</p><div class=\"wrap\"><table><caption>Resultat per regel</caption><thead><tr><th scope=\"col\">Regel</th><th scope=\"col\">Kriterium</th><th scope=\"col\">Kontroll</th><th scope=\"col\">Resultat</th><th scope=\"col\">Etter behandling</th></tr></thead><tbody>");
        foreach (var r in reg.Rules)
        {
            var o = s.Outcomes.GetValueOrDefault(r.Id, RuleOutcome.NotTested);
            if (o == RuleOutcome.NotApplicable && !r.AppliesTo.Contains(s.Kind == DocumentKind.Docx ? "docx" : "pptx")) continue;
            var cls = o switch { RuleOutcome.Passed => "ok", RuleOutcome.Failed => "bad", RuleOutcome.NeedsReview => "warn", _ => "muted" };
            var crit = r.Refs.GetValueOrDefault(profile.Id) is { } refs ? string.Join("; ", refs) : (r.Kind == "bestPractice" ? "Beste praksis" : "–");
            var check = r.Check switch { "auto" => "Automatisk", "heuristic" => "Heuristisk", _ => "Manuell" };
            var fs = s.Findings.Where(f => f.RuleId == r.Id).ToList();
            var solved = fs.Count(f => f.State is FindingState.Changed or FindingState.Verified);
            var after = fs.Count == 0 ? "–" : solved == fs.Count ? $"Alle {fs.Count} funn er behandlet" : $"{solved} av {fs.Count} funn behandlet";
            sb.Append($"<tr><td>{E(r.Title)} <span class=\"muted\">({E(r.Id)})</span></td><td>{E(crit)}</td><td>{check}</td><td class=\"{cls}\">{OutcomeText(o)}</td><td>{after}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // Endringer
        sb.Append("<h2 id=\"endringer\">Endringer</h2>");
        var shown = s.Changes.Where(c => !(c.PendingReview && c.Value.Length == 0 && c.VisibleVariantOnly)).ToList();
        if (shown.Count == 0) sb.Append("<p>Ingen endringer.</p>");
        else
        {
            sb.Append("<div class=\"wrap\"><table><caption>Sporbar oversikt over hver endring</caption><thead><tr><th scope=\"col\">ID</th><th scope=\"col\">Sted</th><th scope=\"col\">Endring</th><th scope=\"col\">Ny verdi</th><th scope=\"col\">Kilde</th><th scope=\"col\">Status</th><th scope=\"col\">Usikkerhet</th></tr></thead><tbody>");
            foreach (var c in shown)
            {
                var origin = c.Origin switch { ChangeOrigin.Ai => "KI-forslag", ChangeOrigin.Ocr => "Lokal tekstgjenkjenning", ChangeOrigin.User => "Bruker", _ => "Regel" };
                sb.Append($"<tr><td>{E(c.Id)}</td><td>{E(c.Display.GetValueOrDefault("location") ?? c.Target.ToString())}</td><td>{E(KindText(c.Kind))}</td><td>{E(c.Value)}");
                if (c.OldValue is { Length: > 0 } old) sb.Append($"<br><span class=\"muted\">Før: {E(old)}</span>");
                sb.Append($"</td><td>{origin}</td><td>{E(ChangeStatusText(c))}{(c.StatusNote != null && !c.PendingReview ? "<br><span class=\"muted\">" + E(c.StatusNote) + "</span>" : "")}</td><td>{E(string.Join(" ", c.UncertaintyReasons))}</td></tr>");
            }
            sb.Append("</tbody></table></div>");
        }

        // Lange beskrivelser (utkast)
        var longs = s.Changes.Where(c => c.Display.ContainsKey("longDescription")).ToList();
        if (longs.Count > 0)
        {
            sb.Append("<h2 id=\"lange\">Utkast til lange beskrivelser</h2><p>Utkastene er ikke lagt inn i filen. Vurder å legge informasjonen inn som tekst eller tabell i selve dokumentet; en egen rapport eller notatfeltet er ikke en fullverdig erstatning.</p>");
            foreach (var c in longs) sb.Append($"<h3>{E(c.Display.GetValueOrDefault("location"))}</h3><p>{E(c.Display["longDescription"])}</p>");
        }

        // Gjenstående funn
        sb.Append("<h2 id=\"funn\">Gjenstående funn</h2>");
        var rem = s.Findings.Where(f => f.State is not (FindingState.Changed or FindingState.Verified) || (f.State == FindingState.Changed && f.ResultNote != null)).ToList();
        if (rem.Count == 0) sb.Append("<p>Ingen gjenstående funn fra de automatiske kontrollene. Manuelle kontroller står fortsatt som «ikke testet».</p>");
        foreach (var g in rem.GroupBy(f => f.Group ?? f.RuleId))
        {
            sb.Append($"<h3>{E(g.Key)} ({g.Count()})</h3><div class=\"wrap\"><table><thead><tr><th scope=\"col\">Sted</th><th scope=\"col\">Beskrivelse</th><th scope=\"col\">Tiltak</th><th scope=\"col\">Status</th></tr></thead><tbody>");
            foreach (var f in g.Take(300))
                sb.Append($"<tr><td>{E(f.Location)}</td><td>{E(f.Description)}{(f.Uncertainty != null ? "<br><span class=\"muted\">Usikkerhet: " + E(f.Uncertainty) + "</span>" : "")}</td><td>{E(f.ResultNote ?? f.SuggestedAction)}</td><td>{StateText(f.State)}{(f.RequiresVisibleChange ? " – krever synlig endring" : "")}</td></tr>");
            if (g.Count() > 300) sb.Append($"<tr><td colspan=\"4\">… og {g.Count() - 300} til (se JSON-rapporten).</td></tr>");
            sb.Append("</tbody></table></div>");
        }

        // Kontroll
        var v = s.Verification;
        sb.Append("<h2 id=\"kontroll\">Kontroll av filen</h2><ul>");
        sb.Append($"<li>Originalfilen er ikke endret. Den forbedrede filen er en kopi der bare nødvendige deler er endret.</li>");
        sb.Append($"<li>Urørte pakkedeler: {v.UntouchedParts} med identisk innhold ({(v.UntouchedPartsIdentical ? "kontrollert" : "<strong>avvik funnet – endringer rullet tilbake</strong>")}).</li>");
        sb.Append($"<li>Endrede deler: {E(string.Join(", ", v.ChangedParts))}{(v.ChangedParts.Count == 0 ? "ingen" : "")}.</li>");
        sb.Append($"<li>Open XML-validering: {v.ValidationErrorsBefore} funn før og {v.ValidationErrorsAfter} etter; ingen nye feil er tillatt.</li>");
        sb.Append($"<li>{E(sum.VisualStatement)}{(v.Engine != null ? " (" + E(v.Engine) + ")" : "")}</li>");
        foreach (var n in v.VisualNotes.Distinct().Take(20)) sb.Append($"<li>{E(n)}</li>");
        sb.Append("<li>Statisk rendering viser ikke om animasjoner og interaksjoner fungerer likt. Antall animasjoner, overganger og medier sammenlignes strukturelt.</li>");
        sb.Append("</ul>");
        if (s.IntakeNotes.Count + s.Model.Notes.Count > 0)
        {
            sb.Append("<h2 id=\"merknader\">Merknader</h2><ul>");
            foreach (var n in s.IntakeNotes.Concat(s.Model.Notes).Distinct().Take(50)) sb.Append($"<li>{E(n)}</li>");
            sb.Append("</ul>");
        }

        sb.Append($"<h2 id=\"kilder\">Regelgrunnlag</h2><p>Regelregister versjon {E(reg.Version)}, kilder kontrollert {E(reg.SourcesChecked)}.</p><ul>");
        foreach (var src in reg.Sources) sb.Append($"<li>{E(src.Title)} – {E(src.Url)} (kontrollert {E(src.Checked)}){(src.Note != null ? ". " + E(src.Note) : "")}</li>");
        sb.Append("</ul></main></body></html>");
        return sb.ToString();
    }
}

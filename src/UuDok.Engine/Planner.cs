using System.Globalization;
using UuDok.Ai;
using UuDok.Core;
using UuDok.Core.Rules;

namespace UuDok.Engine;

public sealed record PlanInput(
    DocumentModel Model, AnalysisResult Analysis, IReadOnlyDictionary<string, OcrInfo> Ocr,
    IReadOnlyDictionary<string, AiResult> Ai, IReadOnlyDictionary<string, string> QrPayloads,
    JobOptions Options, bool RenderAvailable);

/// <summary>
/// Lager endringsplanen. Hver endring har forutsetning (fingeravtrykk), risiko og opprinnelse.
/// Usikre forslag legges til gjennomgang; synlige endringer hører bare til egen eksportvariant.
/// </summary>
public static class Planner
{
    public static ChangePlan Build(PlanInput p)
    {
        var plan = new ChangePlan { Mode = p.Options.Mode };
        var m = p.Model;
        if (m.HasProtection || m.IsProtectedFromIntake) return plan;               // aldri endre beskyttede filer
        if (m.IsSigned && !p.Options.AllowSignatureInvalidation) return plan;

        int n = 0;
        var byRef = m.Objects.GroupBy(o => o.Ref.ToString()).ToDictionary(g => g.Key, g => g.First());
        var findingsByObj = p.Analysis.Findings.GroupBy(f => f.Object.ToString()).ToDictionary(g => g.Key, g => g.ToList());
        var altPlanned = new HashSet<string>();
        var titledByOcr = new Dictionary<int, (string Title, string Pic)>();

        PlannedChange Add(ChangeKind kind, ObjectRef target, string value, ChangeOrigin origin, IEnumerable<Finding> fs,
            string reason, bool review, VisualRisk risk = VisualRisk.None, string? old = null, string? fp = null,
            IEnumerable<string>? uncertainty = null, Dictionary<string, string>? display = null, string? group = null, bool visibleOnly = false)
        {
            var c = new PlannedChange
            {
                Id = $"C{++n:D4}", Kind = kind, Target = target, Value = value, OldValue = old, ExpectedFingerprint = fp, Origin = origin,
                Risk = risk, Reason = reason, RequiresReview = review, FindingIds = fs.Select(f => f.Id).ToList(),
                UncertaintyReasons = uncertainty?.ToList() ?? [], Display = display ?? [], Group = group, VisibleVariantOnly = visibleOnly,
                Visible = visibleOnly,
            };
            plan.Changes.Add(c);
            foreach (var f in fs) f.ChangeIds.Add(c.Id);
            return c;
        }

        // ---------- 1. Lysbildetitler (inkludert tekst i KI-genererte bilder) ----------
        foreach (var f in p.Analysis.Findings.Where(f => f.RuleId == "SLIDE-TITLE"))
        {
            var slide = m.Slides.First(s => s.PartUri == f.Object.PartUri);
            var cand = OcrTitle(m, slide, p.Ocr);
            if (cand == null) { f.ResultNote = "Ingen tittel funnet i tekst eller bilder. Legg til en tittel manuelt."; continue; }
            var (title, pic, agree) = cand.Value;
            var review = !agree || title.Length < 3;
            Add(ChangeKind.AddHiddenSlideTitle, new ObjectRef(slide.PartUri, "part"), title, ChangeOrigin.Ocr, [f],
                $"Lysbildet mangler tittel; tittelen er lest fra teksten i bildet «{pic.Name}».", review,
                uncertainty: agree ? [] : ["Teksten i bildet ble lest ulikt i to forsøk."],
                display: new() { ["slide"] = slide.Number.ToString(), ["source"] = "OCR", ["image"] = ImageKey(pic) }, group: "Lysbildetitler fra bilder");
            titledByOcr[slide.Number] = (title, pic.Ref.ToString());
        }

        // Sikre dekorasjoner (linjer) håndteres i steg 3, med mindre KI har et annet forslag.
        var decorSure = p.Analysis.Findings.Where(f => f.RuleId == "DECOR-CANDIDATE" && f.Outcome == RuleOutcome.Failed)
            .Select(f => f.Object.ToString()).ToHashSet();

        // ---------- 2. Alternativ tekst ----------
        foreach (var f in p.Analysis.Findings.Where(f => f.RuleId is "ALT-MISSING" or "ALT-POOR" or "ALT-TEXTIMAGE"))
        {
            var key = f.Object.ToString();
            // Geometrisk sikre linjer håndteres som dekorasjon; KI-tolkning av dem er observert å dikte opp innhold.
            if (decorSure.Contains(key)) continue;
            if (!byRef.TryGetValue(key, out var o) || !altPlanned.Add(key)) { LinkExisting(plan, key, f); continue; }
            var related = findingsByObj.GetValueOrDefault(key, [f]).Where(x => x.RuleId is "ALT-MISSING" or "ALT-POOR" or "ALT-TEXTIMAGE" or "ALT-COMPLEX").ToList();
            var display = new Dictionary<string, string>
            {
                ["image"] = ImageKey(o), ["existing"] = o.AltText ?? "", ["context"] = Trunc(string.Join(" … ", new[] { o.ContextBefore, o.ContextAfter }.Where(x => x != null)), 400),
                ["caption"] = o.Caption ?? "", ["location"] = f.Location,
            };
            if (o.SlideNumber is { } sn) display["slideTitle"] = m.Slides.First(s => s.Number == sn).TitleText ?? (titledByOcr.TryGetValue(sn, out var tt) ? tt.Title + " (skjult tittel)" : "");
            p.Ocr.TryGetValue(key, out var ocr);
            if (ocr != null) display["ocr"] = ocr.Text;
            var poor = f.RuleId == "ALT-POOR" || (!string.IsNullOrWhiteSpace(o.AltText));
            var repeatsCaption = poor && o.AltText != null && RuleEngine.AltProblem(o.AltText, o) == "gjentar bildeteksten ordrett";

            // Lokalt dekodet QR-innhold er fakta og går foran KI-tolkning.
            if (p.QrPayloads.TryGetValue(key, out var qrFirst) && !poor)
            {
                Add(ChangeKind.SetAltText, o.Ref, $"QR-kode som peker til {qrFirst}", ChangeOrigin.Ocr, related,
                    "QR-koden er dekodet lokalt. Adressen er ikke åpnet.", false, fp: o.Fingerprint, display: display, group: "QR-koder");
                continue;
            }

            List<string>? aiNeedsHuman = null;      // KI hadde ikke grunnlag: prøv faktabaserte kilder før tom gjennomgang
            bool aiPending = false;                 // KI-forslag venter: bruk faktabasert tekst nå, KI-forslaget kan erstatte den
            if (p.Ai.TryGetValue(key, out var ai) && ai.Suggestion is { } s)
            {
                display["role"] = s.FunctionalRole.ToString();
                display["evidence"] = string.Join("; ", s.Evidence);
                display["provider"] = $"{ai.Provider} ({ai.Model}, {(ai.Local ? "lokal" : "sky")})";
                if (s.ProposedLongDescription != null) display["longDescription"] = s.ProposedLongDescription;
                var fabricated = ResponseValidator.HasFabricationFlags(s);
                bool review = p.Options.AiMode switch
                {
                    AiMode.BestEffort => fabricated || repeatsCaption,
                    _ => s.RequiresReview || repeatsCaption,
                };
                switch (s.SuggestedAction)
                {
                    case SuggestedAction.setAltText when s.ProposedAltText.Length > 0:
                        Add(ChangeKind.SetAltText, o.Ref, s.ProposedAltText, ChangeOrigin.Ai, related,
                            poor ? "Erstatter en svak beskrivelse." : "Bildet manglet beskrivelse.", review, old: o.AltText, fp: o.Fingerprint,
                            uncertainty: s.UncertaintyReasons, display: display, group: GroupFor(o, poor));
                        aiPending = review && !poor;
                        break;
                    case SuggestedAction.markDecorative when string.IsNullOrWhiteSpace(o.AltText):
                        Add(ChangeKind.SetDecorative, o.Ref, "1", ChangeOrigin.Ai, related, "KI vurderte objektet som ren dekorasjon.",
                            review || s.FunctionalRole != FunctionalRole.decorative, fp: o.Fingerprint, uncertainty: s.UncertaintyReasons, display: display, group: "Dekorasjoner");
                        break;
                    case SuggestedAction.keepExisting when !string.IsNullOrWhiteSpace(o.AltText):
                        foreach (var r in related) r.ResultNote = "KI vurderte den eksisterende teksten som dekkende. Den er beholdt.";
                        break;
                    default:
                        aiNeedsHuman = s.UncertaintyReasons;
                        break;
                }
                if (aiNeedsHuman == null && !aiPending) continue;
            }
            if (p.Ai.TryGetValue(key, out var failed) && failed.Error != null) display["aiError"] = failed.Error;

            // Uten KI: bare faktabaserte beskrivelser fra dokumentets egne data.
            if (o.Extra.TryGetValue("chartData", out var cd) && !poor)
            {
                Add(ChangeKind.SetAltText, o.Ref, Trunc(cd, 600), ChangeOrigin.Deterministic, related,
                    "Beskrivelse laget fra diagrammets egne data.", false, fp: o.Fingerprint, display: display, group: "Diagrammer");
                continue;
            }
            if (o.Extra.TryGetValue("smartArtText", out var st) && st.Length > 0 && !poor)
            {
                Add(ChangeKind.SetAltText, o.Ref, $"SmartArt: {Trunc(st, 500)}", ChangeOrigin.Deterministic, related,
                    "Beskrivelse laget fra SmartArt-teksten.", false, fp: o.Fingerprint, display: display, group: "SmartArt");
                continue;
            }
            if (ocr is { Lines.Count: > 0 })
            {
                var usedTitle = o.SlideNumber is { } sn2 && titledByOcr.TryGetValue(sn2, out var ttl) && ttl.Pic == key ? ttl.Title : null;
                var rest = ocr.Lines.Select(l => l.Text).Where(t => usedTitle == null || !Same(t, usedTitle)).ToList();
                if (rest.Count > 0)
                {
                    var text = $"Tekst i bildet: «{string.Join(" ", rest)}»";
                    Add(ChangeKind.SetAltText, o.Ref, poor ? text : text, ChangeOrigin.Ocr, related,
                        "Teksten i bildet er lest lokalt (OCR). Illustrasjonen er ikke beskrevet uten KI.", !ocr.Agreement || poor,
                        old: o.AltText, fp: o.Fingerprint, uncertainty: ocr.Agreement ? ["Bare teksten i bildet er gjengitt; selve illustrasjonen er ikke beskrevet."] : ["Teksten i bildet ble lest ulikt i to forsøk."],
                        display: display, group: "Tekst i bilder");
                    continue;
                }
            }
            if (aiPending) continue;                // KI-forslaget venter; ingen faktabasert tekst å bruke i mellomtiden
            if (aiNeedsHuman != null)
            {
                Add(ChangeKind.SetAltText, o.Ref, "", ChangeOrigin.Ai, related, "KI hadde ikke nok grunnlag. Skriv beskrivelsen selv.", true,
                    old: o.AltText, fp: o.Fingerprint, uncertainty: aiNeedsHuman, display: display, group: "Trenger menneskelig beskrivelse");
                continue;
            }
            foreach (var r in related) r.ResultNote ??= p.Options.AiMode == AiMode.None
                ? "Ingen KI valgt: beskrivelsen må skrives manuelt." : "Ingen forslag kunne lages.";
        }

        // ---------- 3. Dekorasjoner ----------
        foreach (var f in p.Analysis.Findings.Where(f => f.RuleId == "DECOR-CANDIDATE"))
        {
            var key = f.Object.ToString();
            if (plan.Changes.Any(c => c.Target.ToString() == key && c.Kind is ChangeKind.SetAltText or ChangeKind.SetDecorative) || !byRef.TryGetValue(key, out var o)) continue;
            var linked = findingsByObj.GetValueOrDefault(key, [f]).Where(x => x.RuleId is "DECOR-CANDIDATE" or "ALT-MISSING").ToList();
            Add(ChangeKind.SetDecorative, o.Ref, "1", ChangeOrigin.Deterministic, linked, f.Description, f.Outcome != RuleOutcome.Failed, fp: o.Fingerprint,
                uncertainty: f.Uncertainty != null ? [f.Uncertainty] : [], display: new() { ["image"] = ImageKey(o), ["location"] = f.Location }, group: "Dekorasjoner");
        }

        // ---------- 4. Dokumenttittel ----------
        if (p.Analysis.Findings.FirstOrDefault(f => f.RuleId == "DOC-TITLE") is { } dt)
        {
            var (title, sure) = TitleCandidate(m, titledByOcr);
            if (title != null)
                Add(ChangeKind.SetDocumentTitle, new ObjectRef("/docProps/core.xml", "part"), Trunc(title, 250), ChangeOrigin.Deterministic, [dt],
                    sure ? "Tittel hentet fra første overskrift/lysbildetittel." : "Tittel foreslått fra tekst som ser ut som en overskrift.", !sure,
                    display: new() { ["location"] = "Dokumentegenskaper" }, group: "Dokumenttittel");
            else dt.ResultNote = "Fant ingen tydelig tittel. Sett tittel i Fil → Info → Egenskaper.";
        }

        // ---------- 5. Språk (DOCX) ----------
        foreach (var f in p.Analysis.Findings.Where(f => f.RuleId == "LANG-DOC" && m.Kind == DocumentKind.Docx && m.DefaultLanguage == null))
        {
            var text = string.Join(" ", m.Objects.Where(o => o.Type is ObjectType.Paragraph or ObjectType.Heading).Select(o => o.Text));
            var g = LanguageGuess.Detect(text);
            if (g.Language == null) { f.ResultNote = "Språket kunne ikke gjenkjennes sikkert."; continue; }
            Add(ChangeKind.SetDocumentLanguage, new ObjectRef("/word/styles.xml", "part"), g.Language, ChangeOrigin.Deterministic, [f],
                $"Standardspråk satt til {g.Language} ut fra teksten.", g.Confidence < 0.3, VisualRisk.Low,
                display: new() { ["location"] = "Standardspråk" }, group: "Språk");
        }

        // ---------- 6. Leserekkefølge (PPTX) ----------
        foreach (var f in p.Analysis.Findings.Where(f => f.RuleId == "ORDER-SLIDE" && f.AutoRepairSupported))
            if (byRef.TryGetValue(f.Object.ToString(), out var o))
                Add(ChangeKind.MoveToReadingStart, o.Ref, "first", ChangeOrigin.Deterministic, [f], "Tittelen flyttes først i leserekkefølgen (overlapper ikke det den passerer).",
                    !p.RenderAvailable, VisualRisk.Low, fp: o.Fingerprint,
                    uncertainty: p.RenderAvailable ? [] : ["Kan ikke kontrolleres visuelt på denne maskinen."], display: new() { ["location"] = f.Location }, group: "Leserekkefølge");

        // ---------- 7. Overskrifter uten synlig endring (DOCX) ----------
        foreach (var f in p.Analysis.Findings.Where(f => f.RuleId == "HEAD-FAKE"))
            if (byRef.TryGetValue(f.Object.ToString(), out var o) && o.Extra.TryGetValue("looksLikeHeading", out var lvl))
                Add(ChangeKind.SetOutlineLevel, o.Ref, lvl, ChangeOrigin.Deterministic, [f], $"Merk «{Trunc(o.Text, 60)}» som overskrift nivå {lvl} (utseendet beholdes).",
                    true, fp: o.Fingerprint, uncertainty: [f.Uncertainty ?? ""], display: new() { ["text"] = o.Text ?? "", ["location"] = f.Location, ["level"] = lvl }, group: "Mulige overskrifter");

        // ---------- 8. Tabelloverskrifter (DOCX) ----------
        foreach (var f in p.Analysis.Findings.Where(f => f.RuleId == "TABLE-HEADER" && m.Kind == DocumentKind.Docx))
            if (byRef.TryGetValue(f.Object.ToString(), out var o))
                Add(ChangeKind.MarkTableHeaderRow, o.Ref, "1", ChangeOrigin.Deterministic, [f], "Første rad markeres som overskriftsrad.",
                    !p.RenderAvailable, VisualRisk.Low, fp: o.Fingerprint,
                    uncertainty: ["Første rad antas å inneholde overskrifter."], display: new() { ["text"] = o.Text ?? "", ["location"] = f.Location }, group: "Tabeller");

        // ---------- 9. Synlige forbedringer (egen variant, alltid valgt av brukeren) ----------
        if (p.Options.Mode == ProcessingMode.AllowVisibleChanges)
        {
            foreach (var f in p.Analysis.Findings.Where(f => f.RuleId == "CONTRAST-TEXT"))
                if (byRef.TryGetValue(f.Object.ToString(), out var o) && Rgb.TryParse(o.Extra["fg"].TrimStart('#')) is { } fg && Rgb.TryParse(o.Extra["bg"].TrimStart('#')) is { } bg)
                {
                    var need = o.Extra["large"] == "1" ? 3.0 : 4.5;
                    var fixedColor = Darken(fg, bg, need + 0.2);
                    Add(ChangeKind.SetTextColor, o.Ref, fixedColor.ToString().TrimStart('#'), ChangeOrigin.Deterministic, [f],
                        $"Tekstfargen endres fra {fg} til {fixedColor} (kontrast {Rgb.Contrast(fixedColor, bg).ToString("0.0", CultureInfo.GetCultureInfo("nb-NO"))}:1). Dette er en synlig endring.",
                        true, VisualRisk.Medium, old: fg.ToString().TrimStart('#'), fp: o.Fingerprint,
                        display: new() { ["text"] = o.Text ?? "", ["from"] = fg.ToString(), ["to"] = fixedColor.ToString(), ["bg"] = bg.ToString(), ["location"] = f.Location },
                        group: "Kontrast (synlig endring)", visibleOnly: true);
                }
            foreach (var f in p.Analysis.Findings.Where(f => f.RuleId is "LINK-PURPOSE" or "LINK-RAWURL"))
                if (byRef.TryGetValue(f.Object.ToString(), out var o))
                    Add(ChangeKind.SetVisibleLinkText, o.Ref, "", ChangeOrigin.User, [f],
                        "Skriv en lenketekst som sier hvor lenken går. Teksten blir synlig i dokumentet.", true, VisualRisk.Medium, old: o.Text, fp: o.Fingerprint,
                        display: new() { ["text"] = o.Text ?? "", ["target"] = o.HyperlinkTarget ?? "", ["context"] = Trunc(o.ContextBefore, 300), ["location"] = f.Location },
                        group: "Lenketekster (synlig endring)", visibleOnly: true);
        }

        // Endringer som krever gjennomgang venter; resten er planlagt.
        foreach (var c in plan.Changes)
            if (c.RequiresReview || c.Value.Length == 0) { c.PendingReview = true; c.Status = ChangeStatus.Skipped; c.StatusNote = "Venter på gjennomgang."; }
        return plan;
    }

    private static void LinkExisting(ChangePlan plan, string key, Finding f)
    {
        var c = plan.Changes.FirstOrDefault(c => c.Target.ToString() == key && c.Kind is ChangeKind.SetAltText or ChangeKind.SetDecorative);
        if (c != null && !c.FindingIds.Contains(f.Id)) { c.FindingIds.Add(f.Id); f.ChangeIds.Add(c.Id); }
    }

    /// <summary>Tittel fra tekst i et stort bilde på lysbildet (typisk KI-generert lysbildebilde).</summary>
    public static (string Title, DocObject Picture, bool Agreement)? OcrTitle(DocumentModel m, SlideInfo slide, IReadOnlyDictionary<string, OcrInfo> ocr)
    {
        double slideArea = (double)m.SlideWidthEmu * m.SlideHeightEmu;
        var pics = m.Objects.Where(o => o.SlideNumber == slide.Number && o.Type == ObjectType.Picture && o.Bounds != null)
            .Where(o => o.Bounds!.Cx * (double)o.Bounds.Cy >= 0.3 * slideArea)
            .OrderByDescending(o => o.Bounds!.Cx * (double)o.Bounds.Cy);
        foreach (var pic in pics)
        {
            if (!ocr.TryGetValue(pic.Ref.ToString(), out var info) || info.Lines.Count == 0) continue;
            var lines = info.Lines.OrderByDescending(l => l.H).ToList();
            var top = lines[0];
            var second = lines.Skip(1).FirstOrDefault();
            if (second != null && top.H < 1.3 * second.H) continue;           // ingen tydelig tittel
            var t = top.Text.Trim();
            if (t.Length is < 2 or > 90) continue;
            var letters = t.Count(char.IsLetter) / (double)t.Length;
            if (letters < 0.6) continue;
            if (top.Y > info.ImageHeight * 0.7) continue;                     // tittel nederst er uvanlig
            return (t, pic, info.Agreement);
        }
        return null;
    }

    private static (string? Title, bool Sure) TitleCandidate(DocumentModel m, Dictionary<int, (string Title, string Pic)> ocrTitles)
    {
        if (m.Kind == DocumentKind.Pptx)
        {
            var first = m.Slides.FirstOrDefault();
            if (first?.TitleText is { } t) return (t.Replace('\n', ' '), true);
            if (first != null && ocrTitles.TryGetValue(first.Number, out var o)) return (o.Title, false);
            return (null, false);
        }
        var main = m.Objects.Where(o => o.Extra.GetValueOrDefault("part") == "Hoveddokument");
        var h1 = main.FirstOrDefault(o => o.Type == ObjectType.Heading && o.HeadingLevel == 1 && !string.IsNullOrWhiteSpace(o.Text));
        var fake = main.FirstOrDefault(o => o.Extra.GetValueOrDefault("looksLikeHeading") == "1");
        // Første tekst i dokumentet som ser ut som tittel slår en senere ekte overskrift.
        if (fake != null && (h1 == null || fake.TreeIndex < h1.TreeIndex)) return (fake.Text!.Trim(), false);
        return h1 != null ? (h1.Text!.Trim(), true) : (null, false);
    }

    private static Rgb Darken(Rgb fg, Rgb bg, double target)
    {
        var bgLight = bg.Luminance() > 0.5;
        for (int k = 100; k >= 0; k -= 2)
        {
            var c = bgLight ? fg.ApplyLum(k * 1000, null) : fg.ApplyLum(100000, (100 - k) * 1000);
            if (Rgb.Contrast(c, bg) >= target) return c;
        }
        return bgLight ? Rgb.Black : Rgb.White;
    }

    public static string ImageKey(DocObject o) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(o.Ref.ToString())))[..16].ToLowerInvariant();
    private static string GroupFor(DocObject o, bool poor) => poor ? "Svake beskrivelser erstattes" : o.Type switch
    {
        ObjectType.Chart => "Diagrammer", ObjectType.SmartArt => "SmartArt", _ => "Bilder"
    };
    private static bool Same(string a, string b) => string.Equals(new string(a.Where(char.IsLetterOrDigit).ToArray()), new string(b.Where(char.IsLetterOrDigit).ToArray()), StringComparison.OrdinalIgnoreCase);
    private static string Trunc(string? s, int n) => s == null ? "" : s.Length <= n ? s : s[..n] + "…";
}

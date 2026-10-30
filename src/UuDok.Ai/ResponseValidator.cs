using System.Text.Json;
using System.Text.RegularExpressions;

namespace UuDok.Ai;

/// <summary>
/// Validerer KI-svar mot skjemaet og legger på egne usikkerhetsvurderinger. Modellens egen vurdering
/// er ikke en kalibrert sannsynlighet; appen kombinerer den med OCR-samsvar, bildestørrelse, rolle,
/// tilgjengelige originaldata og kontroll av tall/navn mot kildene.
/// </summary>
public static partial class ResponseValidator
{
    public const int HardMaxAlt = 1000;          // vern mot løpsk utdata, ikke en anbefalt lengde
    public const int HardMaxLong = 6000;

    public static (AltTextSuggestion? Suggestion, List<string> Errors) Parse(string json, AltTextRequest req)
    {
        var errors = new List<string>();
        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement; }
        catch (JsonException) { return (null, ["Svaret er ikke gyldig JSON."]); }
        if (root.ValueKind != JsonValueKind.Object) return (null, ["Svaret er ikke et JSON-objekt."]);

        var allowed = new HashSet<string> { "objectId", "functionalRole", "proposedAltText", "proposedLongDescription", "language", "evidence", "uncertaintyReasons", "requiresReview", "suggestedAction" };
        foreach (var p in root.EnumerateObject()) if (!allowed.Contains(p.Name)) errors.Add($"Ukjent felt: {p.Name}");
        foreach (var r in allowed) if (!root.TryGetProperty(r, out _)) errors.Add($"Mangler felt: {r}");
        if (errors.Count > 0) return (null, errors);

        string Str(string n) => root.GetProperty(n).ValueKind == JsonValueKind.String ? root.GetProperty(n).GetString()! : throw new FormatException(n);
        List<string> Arr(string n) => root.GetProperty(n).ValueKind == JsonValueKind.Array
            ? root.GetProperty(n).EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new FormatException(n)).ToList()
            : throw new FormatException(n);
        try
        {
            var id = Str("objectId");
            if (id != req.ObjectId) return (null, ["objectId stemmer ikke med forespørselen."]);
            if (!Enum.TryParse<FunctionalRole>(Str("functionalRole"), out var role)) return (null, ["Ugyldig functionalRole."]);
            if (!Enum.TryParse<SuggestedAction>(Str("suggestedAction"), out var action)) return (null, ["Ugyldig suggestedAction."]);
            var alt = Clean(Str("proposedAltText"));
            var longEl = root.GetProperty("proposedLongDescription");
            string? longDesc = longEl.ValueKind == JsonValueKind.Null ? null : longEl.ValueKind == JsonValueKind.String ? Clean(longEl.GetString()!) : throw new FormatException("proposedLongDescription");
            if (root.GetProperty("requiresReview").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return (null, ["requiresReview må være boolsk."]);
            if (alt.Length > HardMaxAlt) return (null, ["Alternativ tekst er urimelig lang."]);
            if (longDesc?.Length > HardMaxLong) longDesc = null;
            if (action == SuggestedAction.setAltText && alt.Length == 0) return (null, ["Tom alternativ tekst."]);

            var s = new AltTextSuggestion
            {
                ObjectId = id, FunctionalRole = role, ProposedAltText = StripImageOf(alt), ProposedLongDescription = string.IsNullOrWhiteSpace(longDesc) ? null : longDesc,
                Language = Str("language"), Evidence = Arr("evidence").Take(10).Select(Clean).ToList(),
                UncertaintyReasons = Arr("uncertaintyReasons").Take(10).Select(Clean).ToList(),
                RequiresReview = root.GetProperty("requiresReview").GetBoolean(), SuggestedAction = action,
            };
            return (Assess(s, req), []);
        }
        catch (FormatException ex) { return (null, [$"Feil type i felt {ex.Message}."]); }
    }

    /// <summary>Appens egen usikkerhetsvurdering. Kan bare øke behovet for gjennomgang, aldri senke det.</summary>
    public static AltTextSuggestion Assess(AltTextSuggestion s, AltTextRequest req)
    {
        var reasons = new List<string>(s.UncertaintyReasons);
        var review = s.RequiresReview;
        var sources = string.Join(" ", req.OcrText, req.ChartData, req.Caption, req.NearbyText, req.HeadingOrSlideTitle, req.ExistingAltText, req.QrPayload, req.TextUsedAsSlideTitle, req.TextAlreadyOnSlide);

        // Lite bilde: lite areal eller kort lengste side. Smale skillelinjer (lange, tynne) regnes ikke som små.
        if (req.ImageWidth > 0 && req.ImageHeight > 0 && (Math.Max(req.ImageWidth, req.ImageHeight) < 120 || req.ImageWidth * req.ImageHeight < 64 * 64))
        { review = true; reasons.Add("Bildet er svært lite; detaljer kan være feiltolket."); }
        if (s.FunctionalRole is FunctionalRole.map or FunctionalRole.composite or FunctionalRole.formula or FunctionalRole.unknown)
        { review = true; reasons.Add($"Rollen «{s.FunctionalRole}» er kompleks eller usikker."); }
        if (s.FunctionalRole == FunctionalRole.chart && string.IsNullOrEmpty(req.ChartData))
        { review = true; reasons.Add("Graf uten tilgang til underliggende data; tall kan ikke kontrolleres."); }
        if (req.OcrText != null && !req.OcrAgreement) { review = true; reasons.Add("Teksten i bildet ble lest ulikt i to forsøk."); }

        var unsupportedNumbers = Numbers().Matches(s.ProposedAltText + " " + s.ProposedLongDescription).Select(m => m.Value)
            .Where(n => !sources.Contains(n, StringComparison.Ordinal)).Distinct().ToList();
        if (unsupportedNumbers.Count > 0) { review = true; reasons.Add($"Tall som ikke finnes i kildene: {string.Join(", ", unsupportedNumbers.Take(5))}."); }

        // Antall skrevet med ord («fem former») er like risikabelt som sifre: små modeller teller ofte feil.
        var numberWords = NumberWords().Matches(s.ProposedAltText + " " + s.ProposedLongDescription).Select(m => m.Value.ToLowerInvariant())
            .Where(w => !Regex.IsMatch(sources, $@"\b{w}\b", RegexOptions.IgnoreCase)).Distinct().ToList();
        if (numberWords.Count > 0) { review = true; reasons.Add($"Antall som ikke finnes i kildene: {string.Join(", ", numberWords.Take(5))}."); }

        var names = ProperNouns(s.ProposedAltText).Where(w => !sources.Contains(w, StringComparison.Ordinal)).Distinct().ToList();
        if (names.Count > 0) { review = true; reasons.Add($"Mulige navn som ikke finnes i kildene: {string.Join(", ", names.Take(5))}."); }

        if (req.TextUsedAsSlideTitle is { Length: > 3 } t && s.ProposedAltText.Contains(t, StringComparison.OrdinalIgnoreCase) && s.FunctionalRole != FunctionalRole.textImage)
        { review = true; reasons.Add("Gjentar lysbildetittelen, som allerede leses."); }
        if (req.TextAlreadyOnSlide is { } onSlide && s.FunctionalRole != FunctionalRole.textImage &&
            onSlide.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3 &&
            s.ProposedAltText.Contains(string.Join(' ', onSlide.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3)), StringComparison.OrdinalIgnoreCase))
        { review = true; reasons.Add("Gjentar tekst som allerede ligger som tittel og brødtekst."); }
        if (req.NearbyText is { Length: > 20 } nb && s.ProposedAltText.Length > 20 && nb.Contains(s.ProposedAltText, StringComparison.OrdinalIgnoreCase))
        { review = true; reasons.Add("Teksten gjentar tekst som allerede leses rundt bildet."); }
        if (s.SuggestedAction == SuggestedAction.markDecorative && !string.IsNullOrWhiteSpace(req.ExistingAltText))
        { review = true; reasons.Add("Objektet har allerede en beskrivelse."); }

        // Observert med gemma3:12b: handlingen motsier innholdet (informativ beskrivelse + «marker som dekorativ»
        // eller «trenger menneske»). Teksten beholdes som forslag til gjennomgang; ingenting brukes automatisk.
        var action = s.SuggestedAction;
        if (s.ProposedAltText.Length > 0 && s.FunctionalRole != FunctionalRole.decorative &&
            action is SuggestedAction.markDecorative or SuggestedAction.needsHuman)
        {
            review = true;
            reasons.Add("Modellens svar var selvmotsigende (beskrivelse, men foreslått handling passet ikke).");
            action = SuggestedAction.setAltText;
        }
        if (action == SuggestedAction.markDecorative && s.FunctionalRole != FunctionalRole.decorative)
        { review = true; reasons.Add("Modellen foreslo dekorativ uten å klassifisere bildet som dekorasjon."); }

        // Observert med gemma3:12b: «behold eksisterende» for bilder uten beskrivelse. Det er ikke et gyldig svar.
        if (action == SuggestedAction.keepExisting && string.IsNullOrWhiteSpace(req.ExistingAltText))
        {
            review = true;
            reasons.Add("Modellen foreslo å beholde en beskrivelse som ikke finnes.");
            action = s.ProposedAltText.Length > 0 ? SuggestedAction.setAltText : SuggestedAction.needsHuman;
        }

        return s with { RequiresReview = review, SuggestedAction = action, UncertaintyReasons = reasons.Distinct().ToList() };
    }

    /// <summary>Om forslaget har tegn på oppdiktede opplysninger. Slike forslag brukes aldri automatisk.</summary>
    public static bool HasFabricationFlags(AltTextSuggestion s) =>
        s.UncertaintyReasons.Any(r => r.StartsWith("Tall som ikke finnes") || r.StartsWith("Antall som ikke finnes") || r.StartsWith("Mulige navn"));

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")] private static partial Regex Numbers();
    // «en/ett/ei» utelatt: de er oftest ubestemt artikkel, ikke antall.
    [GeneratedRegex(@"\b(to|tre|fire|fem|seks|sju|syv|åtte|ni|ti|elleve|tolv|tretten|fjorten|femten|tjue|hundre|tusen|dusin|par|two|three|four|five|six|seven|eight|nine|ten)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NumberWords();
    [GeneratedRegex(@"(?<=[\p{Ll},;:]\s)\p{Lu}[\p{Ll}]{2,}")] private static partial Regex Proper();
    [GeneratedRegex(@"^(et |ett |en )?(bilde|foto|fotografi|illustrasjon) (av|som viser|med) ", RegexOptions.IgnoreCase)] private static partial Regex ImageOf();
    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]")] private static partial Regex Control();

    private static IEnumerable<string> ProperNouns(string s) => Proper().Matches(s).Select(m => m.Value);

    public static string Clean(string s) => Control().Replace(s, "").Replace("<", "‹").Replace(">", "›").Trim();

    private static string StripImageOf(string alt)
    {
        var m = ImageOf().Match(alt);
        if (!m.Success) return alt;
        var rest = alt[m.Length..];
        return rest.Length > 0 ? char.ToUpper(rest[0]) + rest[1..] : alt;
    }
}

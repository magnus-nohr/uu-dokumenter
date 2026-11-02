using System.Text.Json;
using System.Text.Json.Serialization;

namespace UuDok.Web;

/// <summary>Brukerinnstillinger (lagres som JSON i datamappen). API-nøkler lagres aldri her.</summary>
public sealed record AppSettings
{
    public string LocalAiUrl { get; init; } = "http://127.0.0.1:11434";
    public string? LocalAiModel { get; init; }
    /// <summary>Modellen ble funnet automatisk (ikke valgt av brukeren).</summary>
    public bool LocalAiAutoDetected { get; init; }
    /// <summary>Brukeren har slått av lokal KI (tomt modellnavn); da søkes det ikke automatisk igjen.</summary>
    public bool LocalAiDisabledByUser { get; init; }
    public string CloudProvider { get; init; } = "anthropic";
    public string CloudModel { get; init; } = UuDok.Ai.AnthropicProvider.DefaultModel;
    /// <summary>Tidspunkt brukeren godkjente at bilder og nærliggende tekst sendes til skyleverandøren. Null = ikke godkjent.</summary>
    public DateTimeOffset? CloudConsentAt { get; init; }
    // ChatGPT («Sign in with ChatGPT»). Tokens lagres i Windows Legitimasjonsbehandling, ikke her.
    public string? ChatGptHostId { get; init; }          // stabil, ugjennomsiktig installasjons-ID (urn:uuid)
    public string? ChatGptClientId { get; init; }        // utstedt ved første innlogging (oaiapp_…)
    public string? ChatGptModel { get; init; }
    public string? ChatGptAccount { get; init; }         // visningsnavn/e-post fra ID-tokenet
    public DateTimeOffset? ChatGptConsentAt { get; init; }
    public int RetentionDays { get; init; } = 7;
    public bool Rendering { get; init; } = true;
    public string DefaultProfile { get; init; } = "no";

    public const string ChatGptConsentText =
        "Når du bruker ChatGPT, sendes bildet slik det vises i dokumentet og kort tekst rundt bildet (overskrift, bildetekst, " +
        "eksisterende beskrivelse og tekst lest fra bildet) til OpenAI. Filnavn og resten av dokumentet sendes ikke, og appen ber " +
        "OpenAI om ikke å lagre svarene (store=false). Bruken trekkes fra ditt eget ChatGPT Plus- eller Pro-abonnement. " +
        "En privat ChatGPT-konto er ikke dekket av skolens avtaler: ikke bruk den for dokumenter med personopplysninger " +
        "eller annet innhold som ikke kan deles med OpenAI.";

    public const string CloudConsentText =
        "Når du velger skytjeneste, sendes bildet slik det vises i dokumentet, eventuelt originalbildet, og kort tekst rundt bildet " +
        "(overskrift, bildetekst, eksisterende beskrivelse, tekst lest fra bildet) til Anthropic for å lage forslag. " +
        "Filnavn og resten av dokumentet sendes ikke. Ikke bruk skytjenesten for dokumenter med personopplysninger eller annet " +
        "innhold som ikke kan deles med en ekstern leverandør etter reglene hos deg.";
}

public sealed class SettingsService(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly Lock _lock = new();

    public AppSettings Get()
    {
        lock (_lock)
        {
            if (!File.Exists(path)) return new AppSettings();
            try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings(); }
            catch (JsonException) { return new AppSettings(); }
        }
    }

    public void Save(AppSettings s)
    {
        lock (_lock) File.WriteAllText(path, JsonSerializer.Serialize(s, Json));
    }
}

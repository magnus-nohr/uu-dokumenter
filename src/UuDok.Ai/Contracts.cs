using System.Text.Json;
using System.Text.Json.Serialization;

namespace UuDok.Ai;

[JsonConverter(typeof(JsonStringEnumConverter<FunctionalRole>))]
public enum FunctionalRole { informative, decorative, chart, screenshot, textImage, map, formula, qrCode, composite, unknown }

[JsonConverter(typeof(JsonStringEnumConverter<SuggestedAction>))]
public enum SuggestedAction { setAltText, markDecorative, keepExisting, needsHuman }

/// <summary>Det KI-en får se. Inneholder aldri filnavn, dokumentstier eller identifikatorer utover objekt-ID.</summary>
public sealed record AltTextRequest
{
    public required string ObjectId { get; init; }
    public required byte[] DisplayedImagePng { get; init; }      // bildet slik det vises (beskjært)
    public byte[]? OriginalImagePng { get; init; }               // bare når beskjæringen fjerner mye
    public required string DocumentKind { get; init; }           // «presentasjon» / «dokument»
    public string? HeadingOrSlideTitle { get; init; }
    public string? Caption { get; init; }
    public string? NearbyText { get; init; }
    public string? ExistingAltText { get; init; }
    public string? OcrText { get; init; }
    public bool OcrAgreement { get; init; }
    public string? TextUsedAsSlideTitle { get; init; }           // gjentas ikke i alt-teksten
    public string? TextAlreadyOnSlide { get; init; }             // tekst fra bildet som nå ligger som tittel/brødtekst
    public string? ChartData { get; init; }
    public string? QrPayload { get; init; }
    public required string Language { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
}

/// <summary>Validert KI-svar. KI-en foreslår bare innhold; den kan aldri velge filstier, XML eller handlinger utenfor dette skjemaet.</summary>
public sealed record AltTextSuggestion
{
    public required string ObjectId { get; init; }
    public required FunctionalRole FunctionalRole { get; init; }
    public required string ProposedAltText { get; init; }
    public string? ProposedLongDescription { get; init; }
    public required string Language { get; init; }
    public required List<string> Evidence { get; init; }
    public required List<string> UncertaintyReasons { get; init; }
    public required bool RequiresReview { get; init; }
    public required SuggestedAction SuggestedAction { get; init; }
}

public sealed record AiResult(AltTextSuggestion? Suggestion, string? Error, string Provider, string Model, bool Local)
{
    public static AiResult Fail(string provider, string model, bool local, string error) => new(null, error, provider, model, local);
}

public interface IAltTextProvider
{
    string Id { get; }
    string Model { get; }
    bool IsLocal { get; }
    Task<AiResult> SuggestAsync(AltTextRequest request, CancellationToken ct);
}

public static class AiSchema
{
    /// <summary>JSON-skjema brukt både til strukturert utdata hos leverandøren og til lokal validering.</summary>
    public static readonly string Json = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["objectId","functionalRole","proposedAltText","proposedLongDescription","language","evidence","uncertaintyReasons","requiresReview","suggestedAction"],
      "properties": {
        "objectId": { "type": "string" },
        "functionalRole": { "type": "string", "enum": ["informative","decorative","chart","screenshot","textImage","map","formula","qrCode","composite","unknown"] },
        "proposedAltText": { "type": "string" },
        "proposedLongDescription": { "type": ["string","null"] },
        "language": { "type": "string" },
        "evidence": { "type": "array", "items": { "type": "string" } },
        "uncertaintyReasons": { "type": "array", "items": { "type": "string" } },
        "requiresReview": { "type": "boolean" },
        "suggestedAction": { "type": "string", "enum": ["setAltText","markDecorative","keepExisting","needsHuman"] }
      }
    }
    """;

    public static Dictionary<string, JsonElement> AsDictionary() =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json)!;
}

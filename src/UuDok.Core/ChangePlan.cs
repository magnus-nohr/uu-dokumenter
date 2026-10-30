using System.Text.Json.Serialization;

namespace UuDok.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeKind
{
    SetAltText,          // descr på cNvPr/docPr
    SetDecorative,       // a16:decorative-utvidelse
    SetDocumentTitle,    // docProps/core.xml dc:title
    SetDocumentLanguage, // w:lang i docDefaults / a:endParaRPr lang (kun når det mangler)
    SetOutlineLevel,     // w:outlineLvl (navigasjon uten synlig endring)
    AddHiddenSlideTitle, // tittelplassholder utenfor lysbildet
    MarkTableHeaderRow,  // w:tblHeader / a:tbl firstRow
    MoveToReadingStart,  // flytt figur først i spTree (bare uten overlapping)
    SetVisibleLinkText,  // kun i «Tillat synlige forbedringer»
    SetTextColor         // kun i «Tillat synlige forbedringer»
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeStatus { Planned, Applied, Verified, Discarded, Skipped, RolledBack }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeOrigin { Deterministic, Ai, Ocr, User }

/// <summary>
/// Én planlagt endring. Forutsetningen er fingeravtrykket av målelementet slik det var under analysen;
/// stemmer det ikke lenger, forkastes endringen.
/// </summary>
public sealed class PlannedChange
{
    public required string Id { get; init; }
    public required ChangeKind Kind { get; init; }
    public required ObjectRef Target { get; init; }
    public required string Value { get; init; }
    public string? OldValue { get; init; }
    public string? ExpectedFingerprint { get; init; }
    public required ChangeOrigin Origin { get; init; }
    public VisualRisk Risk { get; init; }
    public bool Visible { get; init; }
    public bool RequiresReview { get; set; }
    public string? Reason { get; init; }
    public List<string> FindingIds { get; init; } = [];
    public ChangeStatus Status { get; set; } = ChangeStatus.Planned;
    public string? StatusNote { get; set; }
    /// <summary>Forslag som venter på brukerens gjennomgang (ikke brukt i filen ennå).</summary>
    public bool PendingReview { get; set; }
    /// <summary>Usikkerhet: bare markert, også når endringen er brukt («Gjør det beste du kan»).</summary>
    public List<string> UncertaintyReasons { get; init; } = [];
    /// <summary>Data til gjennomgangsvisningen (kontekst, rolle, eksisterende tekst …). Aldri brukt til patching.</summary>
    public Dictionary<string, string> Display { get; init; } = [];
    public string? Group { get; init; }
    /// <summary>Endringer som bare hører hjemme i varianten med synlige forbedringer.</summary>
    public bool VisibleVariantOnly { get; init; }
}

public sealed class ChangePlan
{
    public List<PlannedChange> Changes { get; } = [];
    public ProcessingMode Mode { get; init; }
}

using System.Text.Json.Serialization;

namespace UuDok.Core;

public enum DocumentKind { Docx, Pptx }

/// <summary>
/// Stabil identitet for et objekt: pakkedel + lokator. Lokatoren bygger på eksisterende
/// objekt-ID (cNvPr/docPr) når den finnes, ellers på posisjon i dokumentrekkefølge.
/// Samme lokator brukes av analyse og patching (se <c>Locators</c> i UuDok.Patching).
/// </summary>
public sealed record ObjectRef(string PartUri, string Locator)
{
    public override string ToString() => $"{PartUri}#{Locator}";
    public static ObjectRef Parse(string s)
    {
        var i = s.IndexOf('#');
        if (i <= 0) throw new FormatException("Ugyldig objektreferanse.");
        return new ObjectRef(s[..i], s[(i + 1)..]);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ObjectType
{
    Document, Section, Slide, Paragraph, Heading, ListItem, TextRun, Picture, Shape, Group,
    Chart, SmartArt, Table, TableCell, Hyperlink, Media, TextBox, Equation, OleObject, Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Severity { Info, Low, Medium, High, Critical }

/// <summary>Livsløp for et funn: oppdaget → foreslått → endret → verifisert (eller forkastet).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FindingState { Detected, Proposed, Changed, Verified, Discarded, Deferred }

/// <summary>Regelresultat. Manglende teststøtte gir aldri «Passed».</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleOutcome { Passed, Failed, NeedsReview, NotApplicable, NotTested }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualRisk { None, Low, Medium, High }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcessingMode { PreserveAppearance, AllowVisibleChanges }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiMode { None, Automatic, Review, BestEffort }

/// <summary>Kildehenvisning til original OOXML.</summary>
public sealed record SourceRef(string PartUri, string XPath, string? Snippet = null);

public sealed record Evidence(string Kind, string Text);

public sealed class Finding
{
    public required string Id { get; init; }
    public required string RuleId { get; init; }
    public required string Profile { get; init; }
    public required ObjectRef Object { get; init; }
    public required string Location { get; init; }        // forståelig: «Lysbilde 3, «Bilde 4»»
    public required string Description { get; init; }
    public List<Evidence> Evidence { get; init; } = [];
    public Severity Severity { get; init; }
    public List<string> Criteria { get; init; } = [];
    public string? SuggestedAction { get; set; }
    public string? Uncertainty { get; set; }
    public VisualRisk VisualRisk { get; init; }
    public bool AutoRepairSupported { get; init; }
    public bool RequiresVisibleChange { get; init; }
    public RuleOutcome Outcome { get; set; } = RuleOutcome.Failed;
    public FindingState State { get; set; } = FindingState.Detected;
    public string? ResultNote { get; set; }
    public List<string> ChangeIds { get; init; } = [];
    public string? Group { get; init; }                   // for gruppering i UI
}

namespace UuDok.Core;

/// <summary>
/// Felles, lesende dokumentmodell. Den brukes til analyse og rapport – aldri til å bygge
/// dokumentet på nytt. All skriving skjer som målrettede endringer i originalpakken.
/// </summary>
public sealed class DocumentModel
{
    public required DocumentKind Kind { get; init; }
    public string? Title { get; set; }
    public string? DefaultLanguage { get; set; }
    public bool IsSigned { get; set; }
    public bool IsProtectedFromIntake { get; set; }
    public bool HasProtection { get; set; }
    public bool HasTrackedChanges { get; set; }
    public bool HasComments { get; set; }
    public int SlideOrPageCount { get; set; }
    public long SlideWidthEmu { get; set; }
    public long SlideHeightEmu { get; set; }
    public List<SlideInfo> Slides { get; } = [];
    public List<DocObject> Objects { get; } = [];
    public List<string> Notes { get; } = [];             // tekniske merknader fra analysen
    public Dictionary<string, int> RunLanguages { get; } = []; // språkmerking på tekstløp (antall tegn)

    public IEnumerable<DocObject> OfType(ObjectType t) => Objects.Where(o => o.Type == t);
}

public sealed class SlideInfo
{
    public required int Number { get; init; }           // 1-basert visningsrekkefølge
    public required string PartUri { get; init; }
    public bool Hidden { get; init; }
    public string? LayoutName { get; init; }
    public string? TitleText { get; set; }
    public bool HasTitlePlaceholder { get; set; }
    public bool TitleIsHiddenOffSlide { get; set; }
    public List<string> ReadingOrder { get; } = [];     // lokatorer i figurtreets rekkefølge
    public bool HasAnimations { get; set; }
    public bool HasTransition { get; set; }
    public string? NotesText { get; set; }
    public string AllVisibleText { get; set; } = "";
}

public sealed record Bounds(long X, long Y, long Cx, long Cy, int RotationDeg60k = 0)
{
    public bool Intersects(Bounds o) => X < o.X + o.Cx && o.X < X + Cx && Y < o.Y + o.Cy && o.Y < Y + Cy;
    public bool Contains(Bounds o) => o.X >= X && o.Y >= Y && o.X + o.Cx <= X + Cx && o.Y + o.Cy <= Y + Cy;
    public bool IsOutside(long w, long h) => X + Cx <= 0 || Y + Cy <= 0 || X >= w || Y >= h;
}

public sealed record Crop(int L, int T, int R, int B); // a:srcRect i 1/1000 prosent

public sealed class DocObject
{
    public required ObjectRef Ref { get; init; }
    public required ObjectType Type { get; init; }
    public string? Name { get; init; }
    public int? NativeId { get; init; }
    public string? AltText { get; set; }
    public string? AltTitle { get; set; }
    public bool Decorative { get; set; }
    public string? Text { get; init; }
    public string? Language { get; init; }
    public int? HeadingLevel { get; init; }
    public string? StyleId { get; init; }
    public int? SlideNumber { get; init; }
    public int? PageHint { get; init; }
    public string? ParentLocator { get; init; }
    public int Depth { get; init; }
    public int TreeIndex { get; init; }                  // rekkefølge i figurtre / dokument
    public Bounds? Bounds { get; init; }
    public string? ImagePartUri { get; init; }
    public string? ImageContentType { get; init; }
    public Crop? Crop { get; init; }
    public string? HyperlinkTarget { get; init; }
    public bool HyperlinkExternal { get; init; }
    public string? PlaceholderType { get; init; }        // title, ctrTitle, body, ...
    public bool IsPlaceholder { get; init; }
    public string? ContextBefore { get; init; }
    public string? ContextAfter { get; init; }
    public string? Caption { get; init; }
    public Dictionary<string, string> Extra { get; init; } = [];
    public SourceRef? Source { get; init; }
    public string? Fingerprint { get; init; }            // SHA-256 av elementets XML ved analyse
}

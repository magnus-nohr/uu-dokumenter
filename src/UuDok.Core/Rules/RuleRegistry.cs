using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UuDok.Core.Rules;

public sealed class RuleRegistry
{
    public required string Version { get; init; }
    public required string SourcesChecked { get; init; }
    public required string Disclaimer { get; init; }
    public required List<SourceDef> Sources { get; init; }
    public required List<ProfileDef> Profiles { get; init; }
    public required List<RuleDef> Rules { get; init; }

    public RuleDef Get(string id) => Rules.First(r => r.Id == id);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static RuleRegistry? _default;

    public static RuleRegistry Default => _default ??= Load();

    public static RuleRegistry Load()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("UuDok.Core.Rules.regelregister.json")
            ?? throw new InvalidOperationException("Regelregisteret mangler i assembly.");
        var r = JsonSerializer.Deserialize<RuleRegistry>(s, Json) ?? throw new InvalidDataException("Ugyldig regelregister.");
        r.Validate();
        return r;
    }

    /// <summary>Konsistenskontroll: alle profiler/kilder finnes, og manuelle regler kan aldri bli grønne.</summary>
    public void Validate()
    {
        var profiles = Profiles.Select(p => p.Id).ToHashSet();
        var sources = Sources.Select(s => s.Id).ToHashSet();
        foreach (var p in Profiles)
            foreach (var s in p.Sources) if (!sources.Contains(s)) throw new InvalidDataException($"Profil {p.Id} viser til ukjent kilde {s}.");
        foreach (var r in Rules)
        {
            foreach (var k in r.Refs.Keys) if (!profiles.Contains(k)) throw new InvalidDataException($"Regel {r.Id} viser til ukjent profil {k}.");
            if (r.Check == "manual" && r.CanPass) throw new InvalidDataException($"Manuell regel {r.Id} kan ikke ha canPass=true.");
            if (r.Check == "heuristic" && r.CanPass) throw new InvalidDataException($"Heuristisk regel {r.Id} kan ikke ha canPass=true.");
        }
        if (Rules.Select(r => r.Id).Distinct().Count() != Rules.Count) throw new InvalidDataException("Dupliserte regel-ID-er.");
    }
}

public sealed record SourceDef(string Id, string Title, string Url, string Checked, string? Note);
public sealed record ProfileDef(string Id, string Name, bool Normative, string Basis, List<string> Sources, string? Note);

public sealed class RuleDef
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Kind { get; init; }
    public required List<string> AppliesTo { get; init; }
    public string? Wcag { get; init; }
    public string? Level { get; init; }
    public required string Check { get; init; }          // auto | heuristic | manual
    public required string Repair { get; init; }         // auto | suggestion | visible | none
    public bool CanPass { get; init; }
    public Dictionary<string, List<string>> Refs { get; init; } = [];
    public required string TestMethod { get; init; }
    public required string Limitations { get; init; }

    [JsonIgnore] public bool AppliesToDocx => AppliesTo.Contains("docx");
    [JsonIgnore] public bool AppliesToPptx => AppliesTo.Contains("pptx");
}

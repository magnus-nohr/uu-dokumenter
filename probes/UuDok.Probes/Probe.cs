using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UuDok.Core;
using UuDok.Core.Opc;
using UuDok.Patching;
using UuDok.Rendering;

static class Probe
{
    public sealed class Result
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public List<string> Observations { get; } = [];
        public string Status { get; set; } = "ikke kjørt";
        public string? Conclusion { get; set; }
        public void Observe(string s) { Observations.Add(s); Console.WriteLine("  · " + s); }
        public void Pass(bool ok, string conclusion)
        {
            if (Status == "feilet") return;              // et tidligere delresultat feilet
            Status = ok ? "bekreftet" : "feilet"; Conclusion = conclusion;
        }
    }

    public sealed class Recorder
    {
        public Dictionary<string, string> Meta { get; } = [];
        public List<Result> Results { get; } = [];

        public void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "resultater.json"),
                JsonSerializer.Serialize(new { meta = Meta, results = Results }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            var sb = new StringBuilder();
            sb.AppendLine("# Resultater fra tekniske bevis (automatisk generert)");
            sb.AppendLine();
            sb.AppendLine("Generert av `probes/UuDok.Probes`. Ikke rediger for hånd – kjør `kjor-bevis.bat` på nytt.");
            sb.AppendLine();
            foreach (var (k, v) in Meta) sb.AppendLine($"- **{k}**: {v}");
            sb.AppendLine();
            foreach (var r in Results)
            {
                sb.AppendLine($"## {r.Id}: {r.Title}");
                sb.AppendLine();
                sb.AppendLine($"**Status:** {r.Status}" + (r.Conclusion != null ? $" – {r.Conclusion}" : ""));
                sb.AppendLine();
                foreach (var o in r.Observations) sb.AppendLine($"- {o}");
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(dir, "RESULTATER.md"), sb.ToString());
        }

        public string Summary() => string.Join(Environment.NewLine, Results.Select(r => $"{r.Status,-11} {r.Id}"));
    }

    public static async Task Run(Recorder rec, string id, string title, Func<Result, Task> body)
    {
        var r = new Result { Id = id, Title = title };
        rec.Results.Add(r);
        Console.WriteLine($"== {id}: {title}");
        try { await body(r); }
        catch (Exception ex) { r.Status = "feilet"; r.Conclusion = "Unntak: " + ex.Message; Console.WriteLine("  ! " + ex); }
        Console.WriteLine($"  => {r.Status}");
    }

    public static OpcPackage Load(string path)
    {
        using var fs = File.OpenRead(path);
        return OpcPackage.Load(fs);
    }

    public static string FindPic(OpcPackage pkg, string part, string name)
    {
        var d = SafeXml.Load(pkg.GetBytes(part));
        var nv = Locators.LiveNonVisualProps(d).First(e => (string?)e.Attribute("name") == name);
        return Locators.ForNv(int.Parse((string)nv.Attribute("id")!));
    }

    public static string FindDocPr(OpcPackage pkg, string name)
    {
        var d = SafeXml.Load(pkg.GetBytes("/word/document.xml"));
        var dp = Locators.Live(d, Ns.Wp + "docPr").First(e => (string?)e.Attribute("name") == name);
        var id = (string)dp.Attribute("id")!;
        var occ = Locators.Live(d, Ns.Wp + "docPr").Where(e => (string?)e.Attribute("id") == id).ToList().IndexOf(dp);
        return Locators.ForDocPr(int.Parse(id), occ);
    }

    static int _n;
    public static PlannedChange Change(ChangeKind k, string part, string loc, string value, string? old = null) => new()
    {
        Id = $"c{++_n}", Kind = k, Target = new ObjectRef(part, loc), Value = value, OldValue = old, Origin = ChangeOrigin.Deterministic
    };

    public sealed record Verified(bool Ok, byte[] Output, JsonNode? Before, JsonNode? After);

    /// <summary>Patcher, validerer, skriver fil, rendrer før/etter i Office og sammenligner.</summary>
    public static async Task<Verified> PatchAndVerify(Result r, OpcPackage pkg, List<PlannedChange> changes, DocumentKind kind,
        string originalFile, string dir, IRenderer renderer, PatchEnv env)
    {
        Directory.CreateDirectory(dir);
        var res = new Patcher().Apply(pkg, changes, env);
        foreach (var c in changes) r.Observe($"Endring {c.Kind} {c.Target}: {c.Status}{(c.StatusNote != null ? " – " + c.StatusNote : "")}");
        bool ok = changes.All(c => c.Status == ChangeStatus.Applied);
        r.Observe($"Urørte deler med identiske bytes: {res.UntouchedParts} av {pkg.Entries.Count - res.ChangedParts.Count(p => !p.Created)} (verifisert: {(res.UntouchedVerified ? "ja" : "NEI")}).");
        foreach (var p in res.ChangedParts)
            r.Observe($"Endret del {p.PartUri}{(p.Created ? " (ny)" : "")}: {string.Join("; ", p.Diffs.Take(6))}{(p.Diffs.Count > 6 ? $" … (+{p.Diffs.Count - 6})" : "")}");
        ok &= res.UntouchedVerified;

        var vb = OoxmlValidation.Validate(File.ReadAllBytes(originalFile), kind);
        var va = OoxmlValidation.Validate(res.Output, kind);
        var nw = OoxmlValidation.NewIssues(vb, va);
        r.Observe($"Open XML-validering: {vb.Count} feil før, {va.Count} etter, {nw.Count} nye.{(nw.Count > 0 ? " Nye: " + string.Join(" | ", nw.Take(3).Select(i => i.Description)) : "")}");
        ok &= nw.Count == 0;

        var outFile = Path.Combine(dir, "resultat" + (kind == DocumentKind.Pptx ? ".pptx" : ".docx"));
        File.WriteAllBytes(outFile, res.Output);
        var before = await renderer.RenderAsync(originalFile, kind, Path.Combine(dir, "for"), default);
        var after = await renderer.RenderAsync(outFile, kind, Path.Combine(dir, "etter"), default);
        r.Observe($"{before.EngineVersion} åpnet original og resultat uten feil ({before.Pages.Count} / {after.Pages.Count} {(kind == DocumentKind.Pptx ? "lysbilder" : "sider")}).");
        ok &= before.Pages.Count == after.Pages.Count;
        long maxDiff = 0;
        for (int i = 0; i < Math.Min(before.Pages.Count, after.Pages.Count); i++)
        {
            var d = ImageCompare.Compare(before.Pages[i], after.Pages[i], 0, Path.Combine(dir, $"diff-{i + 1:D3}.png"));
            maxDiff = Math.Max(maxDiff, d.DifferentPixels);
            if (!d.SameSize) { ok = false; r.Observe($"Side {i + 1}: ulik størrelse."); }
        }
        r.Observe($"Visuell sammenligning (toleranse 0): maks {maxDiff} avvikende piksler per {(kind == DocumentKind.Pptx ? "lysbilde" : "side")}.");
        ok &= maxDiff == 0;
        return new Verified(ok, res.Output, before.Structure, after.Structure);
    }

    public static string PageSignature(JsonNode? s) =>
        string.Join(",", s!["paragraphInfo"]!.AsArray().Select(p => (int)p!["endPage"]!));
}

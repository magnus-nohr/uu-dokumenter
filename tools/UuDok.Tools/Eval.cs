using System.Globalization;
using System.Text;
using System.Text.Json;
using UuDok.Ai;

namespace UuDok.Tools;

/// <summary>
/// Evalueringsrutine for KI-modeller før valg av standardmodell. Kjører et evalueringssett mot én leverandør,
/// gjør automatiske kontroller og lager et skjema for menneskelig vurdering etter rubrikken i docs/EVALUERINGSRUBRIKK.md.
/// Automatiske kontroller erstatter ikke menneskelig vurdering.
///   UuDok.Tools eval-ai --set evaluering/sett.json --ollama-model <modell> [--ollama-url http://127.0.0.1:11434] --out evaluering/resultater
/// </summary>
public static class Eval
{
    public static async Task<int> Run(string[] opts)
    {
        string? Opt(string k) { var i = Array.IndexOf(opts, "--" + k); return i >= 0 && i + 1 < opts.Length ? opts[i + 1] : null; }
        var setPath = Opt("set") ?? "evaluering/sett.json";
        var root = Path.GetDirectoryName(Path.GetFullPath(setPath))!;
        while (!File.Exists(Path.Combine(root, "UuDok.slnx")) && Directory.GetParent(root) is { } p) root = p.FullName;
        var model = Opt("ollama-model");
        IAltTextProvider provider = model != null
            ? new OllamaProvider(new Uri(Opt("ollama-url") ?? "http://127.0.0.1:11434"), model)
            : opts.Contains("--mock") ? new MockProvider(r => $$"""{"objectId":"{{r.ObjectId}}","functionalRole":"unknown","proposedAltText":"","proposedLongDescription":null,"language":"nb-NO","evidence":[],"uncertaintyReasons":["mock"],"requiresReview":true,"suggestedAction":"needsHuman"}""")
            : throw new ArgumentException("Oppgi --ollama-model <navn> eller --mock.");
        var outDir = Opt("out") ?? Path.Combine(root, "evaluering", "resultater");
        Directory.CreateDirectory(outDir);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(setPath));
        var csv = new StringBuilder("id;modell;rolle;forventetRolle;rolleOk;altTekst;maaNevneOk;forbudtFunnet;starterMedBildeAv;gjennomgang;usikkerhet;sekunder;" +
                                    "korrekthet(0-2);relevans(0-2);kontekst(0-2);lesbarhet(0-2);unødvendigGjentakelse(0-2);oppdiktedeDetaljer(0-2);kommentar\n");
        int n = 0, roleOk = 0, mustOk = 0, forbidden = 0;
        foreach (var e in doc.RootElement.GetProperty("elementer").EnumerateArray())
        {
            var id = e.GetProperty("id").GetString()!;
            var img = Path.Combine(root, e.GetProperty("bilde").GetString()!);
            var prepared = ImagePrep.Prepare(await File.ReadAllBytesAsync(img), null)!;
            string? S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var req = new AltTextRequest
            {
                ObjectId = "obj-1", DisplayedImagePng = prepared.DisplayedPng, DocumentKind = S("dokumenttype") ?? "dokument",
                HeadingOrSlideTitle = S("lysbildetittel"), NearbyText = S("tekstRundt"), TextUsedAsSlideTitle = S("tittelBruktSomLysbildetittel"),
                ChartData = S("diagramdata"), QrPayload = S("qr"), Language = "nb-NO", ImageWidth = prepared.Width, ImageHeight = prepared.Height,
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await provider.SuggestAsync(req, default);
            var s = r.Suggestion;
            var alt = s?.ProposedAltText ?? "";
            var expectedRoles = e.GetProperty("forventetRolle").EnumerateArray().Select(x => x.GetString()!).ToList();
            var rok = s != null && expectedRoles.Contains(s.FunctionalRole.ToString());
            var must = e.GetProperty("maaNevne").EnumerateArray().All(g => g.EnumerateArray().Any(w => alt.Contains(w.GetString()!, StringComparison.OrdinalIgnoreCase)));
            var bad = e.GetProperty("maaIkkeNevne").EnumerateArray().Select(w => w.GetString()!).Where(w => alt.Contains(w, StringComparison.OrdinalIgnoreCase)).ToList();
            n++; if (rok) roleOk++; if (must) mustOk++; if (bad.Count > 0) forbidden++;
            string Q(string? x) => "\"" + (x ?? "").Replace("\"", "\"\"").Replace("\n", " ") + "\"";
            csv.Append(string.Join(";", id, provider.Model, s?.FunctionalRole.ToString() ?? "FEIL: " + r.Error, string.Join("/", expectedRoles), rok ? "ja" : "nei",
                Q(alt), must ? "ja" : "nei", Q(string.Join(", ", bad)), alt.StartsWith("Bilde av", StringComparison.OrdinalIgnoreCase) ? "ja" : "nei",
                s?.RequiresReview == true ? "ja" : "nei", Q(string.Join(" ", s?.UncertaintyReasons ?? [])), sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),
                "", "", "", "", "", "", "")).Append('\n');
            Console.WriteLine($"{id}: {(s == null ? "FEIL " + r.Error : $"{s.FunctionalRole} – {alt}")}");
        }
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        var file = Path.Combine(outDir, $"eval-{provider.Model.Replace(':', '_').Replace('/', '_')}-{stamp}.csv");
        await File.WriteAllTextAsync(file, csv.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"\nRolle riktig: {roleOk}/{n}. Nødvendige fakta nevnt: {mustOk}/{n}. Forbudt innhold: {forbidden}/{n}.");
        Console.WriteLine($"Skjema for menneskelig vurdering: {file}");
        Console.WriteLine("Merk: automatiske kontroller dokumenterer ikke modellkvalitet alene. Fyll ut rubrikkolonnene.");
        return 0;
    }
}

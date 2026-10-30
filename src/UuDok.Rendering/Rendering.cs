using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UuDok.Core;

namespace UuDok.Rendering;

public sealed record RenderOutput(string Engine, string EngineVersion, IReadOnlyList<string> Pages, JsonNode Structure, TimeSpan Duration);

public interface IRenderer
{
    string Name { get; }
    /// <summary>Om motoren finnes på denne maskinen (uten å starte dokumentbehandling).</summary>
    bool IsAvailable();
    Task<RenderOutput> RenderAsync(string file, DocumentKind kind, string outDir, CancellationToken ct);
}

public sealed class RenderFailedException(string message) : Exception(message);

/// <summary>
/// Office-rendering via UuDok.WinHelper i egen prosess med tidsgrense. Ved tidsavbrudd drepes hjelperen
/// og eventuell Word-prosess som hjelperen selv startet (PID fra UUDOK_PIDFILE). Brukerens egne Office-vinduer røres ikke.
/// </summary>
public sealed class OfficeRenderer(string helperExe, TimeSpan timeout) : IRenderer
{
    public string Name => "Microsoft Office";

    public bool IsAvailable() =>
        OperatingSystem.IsWindows() && File.Exists(helperExe) &&
        Type.GetTypeFromProgID("PowerPoint.Application") != null && Type.GetTypeFromProgID("Word.Application") != null;

    public async Task<RenderOutput> RenderAsync(string file, DocumentKind kind, string outDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outDir);
        var json = Path.Combine(outDir, "structure.json");
        var pidFile = Path.Combine(outDir, "office.pid");
        var cmd = kind == DocumentKind.Pptx ? "render-pptx" : "render-docx";
        var psi = new ProcessStartInfo(helperExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { cmd, "--in", file, "--out", outDir, "--json", json }) psi.ArgumentList.Add(a);
        psi.Environment["UUDOK_PIDFILE"] = pidFile;
        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi) ?? throw new RenderFailedException("Kunne ikke starte renderingshjelperen.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            KillTree(p, pidFile);
            throw new RenderFailedException(ct.IsCancellationRequested ? "Renderingen ble avbrutt." : $"Renderingen tok mer enn {timeout.TotalSeconds:0} sekunder og ble stoppet.");
        }
        if (!File.Exists(json)) throw new RenderFailedException("Renderingshjelperen ga ikke noe resultat.");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(json, ct))!;
        if (p.ExitCode != 0 || node["error"] != null)
            throw new RenderFailedException("Office kunne ikke åpne eller vise filen: " + (string?)node["message"]);
        var pages = kind == DocumentKind.Pptx
            ? node["slides"]!.AsArray().Select(s => (string)s!["png"]!).ToList()
            : node["pngs"]!.AsArray().Select(s => (string)s!).ToList();
        var ver = $"{(string?)node["engine"]} {(string?)node["version"]} (build {(string?)node["build"]})";
        return new RenderOutput(Name, ver, pages, node, sw.Elapsed);
    }

    private static void KillTree(Process p, string pidFile)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
        try
        {
            if (File.Exists(pidFile))
                foreach (var s in File.ReadAllText(pidFile).Split(',', StringSplitOptions.RemoveEmptyEntries))
                    try { Process.GetProcessById(int.Parse(s)).Kill(); } catch { }
        }
        catch { }
    }
}

/// <summary>Plassholder når ingen motor finnes – rapporterer ærlig at visuell kontroll ikke er gjort.</summary>
public sealed class NoRenderer : IRenderer
{
    public string Name => "Ingen";
    public bool IsAvailable() => false;
    public Task<RenderOutput> RenderAsync(string file, DocumentKind kind, string outDir, CancellationToken ct) =>
        throw new RenderFailedException("Ingen renderingsmotor er tilgjengelig.");
}

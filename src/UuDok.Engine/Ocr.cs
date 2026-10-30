using System.Diagnostics;
using System.Text.Json;
using UuDok.Core.Rules;

namespace UuDok.Engine;

public interface IOcrService
{
    bool IsAvailable { get; }
    string Name { get; }
    Task<Dictionary<string, OcrInfo>> RecognizeAsync(IReadOnlyList<(string Id, string PngPath)> images, string workDir, CancellationToken ct);
}

public sealed class NoOcr : IOcrService
{
    public bool IsAvailable => false;
    public string Name => "Ingen OCR";
    public Task<Dictionary<string, OcrInfo>> RecognizeAsync(IReadOnlyList<(string Id, string PngPath)> images, string workDir, CancellationToken ct) => Task.FromResult(new Dictionary<string, OcrInfo>());
}

/// <summary>Windows.Media.Ocr via UuDok.WinHelper (egen prosess, tidsgrense, ingen nettverk).</summary>
public sealed class WindowsOcr(string helperExe, TimeSpan timeout) : IOcrService
{
    public bool IsAvailable => OperatingSystem.IsWindows() && File.Exists(helperExe);
    public string Name => "Windows OCR (lokal)";

    private sealed record Line(string Text, double X, double Y, double W, double H);
    private sealed record Item(string Id, string? Language, List<Line> Lines, bool Agreement, int Width, int Height, string? Error);

    public async Task<Dictionary<string, OcrInfo>> RecognizeAsync(IReadOnlyList<(string Id, string PngPath)> images, string workDir, CancellationToken ct)
    {
        var res = new Dictionary<string, OcrInfo>();
        if (images.Count == 0) return res;
        var manifest = Path.Combine(workDir, "ocr-manifest.json");
        var output = Path.Combine(workDir, "ocr-result.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(images.Select(i => new { id = i.Id, path = i.PngPath })), ct);
        var psi = new ProcessStartInfo(helperExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "ocr-batch", "--manifest", manifest, "--json", output }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } if (ct.IsCancellationRequested) throw; return res; }
        if (!File.Exists(output)) return res;
        var items = JsonSerializer.Deserialize<List<Item>>(await File.ReadAllTextAsync(output, ct), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        foreach (var it in items.Where(i => i.Error == null))
            res[it.Id] = new OcrInfo(it.Id, it.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l => new OcrLine(l.Text.Trim(), l.X, l.Y, l.W, l.H)).ToList(),
                it.Agreement, it.Language, it.Width, it.Height);
        return res;
    }
}

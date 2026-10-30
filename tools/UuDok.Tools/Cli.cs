using UuDok.Ai;
using UuDok.Core;
using UuDok.Engine;
using UuDok.Rendering;
using UuDok.Reporting;

namespace UuDok.Tools;

/// <summary>Kommandolinje for hele flyten (brukes til testing og feilsøking). Ingen sky: bare «none» eller lokal Ollama.</summary>
public static class Cli
{
    public static async Task<int> Process(string input, string outDir, string[] opts)
    {
        string? Opt(string k) { var i = Array.IndexOf(opts, "--" + k); return i >= 0 && i + 1 < opts.Length ? opts[i + 1] : null; }
        var root = AppContext.BaseDirectory;
        var helper = Opt("helper") ?? FindHelper();
        IRenderer renderer = opts.Contains("--no-render") || helper == null ? new NoRenderer() : new OfficeRenderer(helper, TimeSpan.FromMinutes(5));
        IOcrService ocr = helper == null || opts.Contains("--no-ocr") ? new NoOcr() : new WindowsOcr(helper, TimeSpan.FromMinutes(10));
        var aiMode = Enum.Parse<AiMode>(Opt("ai-mode") ?? "None", true);
        Func<IAltTextProvider?> ai = () => Opt("ollama-model") is { } m ? new OllamaProvider(new Uri(Opt("ollama-url") ?? "http://127.0.0.1:11434"), m) : null;
        var options = new JobOptions
        {
            Mode = opts.Contains("--visible") ? ProcessingMode.AllowVisibleChanges : ProcessingMode.PreserveAppearance,
            AiMode = aiMode, EnableRendering = renderer.IsAvailable(),
        };
        var pipe = new Pipeline(renderer, ocr, ai);
        Directory.CreateDirectory(outDir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var state = await pipe.RunAsync(await File.ReadAllBytesAsync(input), outDir, options, new ConsoleProgress(), default);
            var name = Path.GetFileName(input);
            await File.WriteAllTextAsync(Path.Combine(outDir, "rapport.html"), ReportBuilder.BuildHtml(state, name));
            await File.WriteAllTextAsync(Path.Combine(outDir, "rapport.json"), ReportBuilder.BuildJson(state, name));
            Console.WriteLine($"--- {name} ({sw.Elapsed.TotalSeconds:0.0} s)");
            foreach (var s in ReportBuilder.Summarize(state).Sentences) Console.WriteLine("  • " + s);
            return 0;
        }
        catch (JobRejectedException ex) { Console.WriteLine($"AVVIST ({ex.Code}): {ex.Message}"); return 3; }
    }

    private static string? FindHelper()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "src", "UuDok.WinHelper", "bin", "Debug", "net10.0-windows10.0.19041.0", "UuDok.WinHelper.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private sealed class ConsoleProgress : IProgress
    {
        private string? _last;
        public void Report(string stage, int percent, string? detail = null)
        {
            if (stage == _last) return;
            _last = stage;
            Console.WriteLine($"  [{percent,3} %] {stage}{(detail != null ? " – " + detail : "")}");
        }
    }
}

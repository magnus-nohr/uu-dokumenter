using System.Net.Http.Json;
using System.Text.Json;

namespace UuDok.Web;

/// <summary>
/// Finner en lokal bildemodell i Ollama på denne maskinen, slik at KI-beskrivelser virker uten oppsett.
/// Kontakter bare loopback-adressen i innstillingene, med kort tidsgrense.
/// </summary>
public static class LocalAiDetector
{
    // Kjente modellfamilier med bildestøtte, i prioritert rekkefølge (gemma3 er prøvekjørt, se docs/EVALUERINGSRUBRIKK.md).
    private static readonly string[] Preferred = ["gemma3", "qwen3-vl", "qwen2.5vl", "llama3.2-vision", "llava", "minicpm-v", "mistral-small3"];

    public static async Task<string?> DetectAsync(string baseUrl, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback) return null;
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(2) };
            var tags = await http.GetFromJsonAsync<JsonElement>("/api/tags", ct);
            var names = tags.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()).OfType<string>().ToList();
            var ordered = names.OrderBy(n => { var i = Array.FindIndex(Preferred, p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 99 : i; });
            foreach (var name in ordered)
            {
                var resp = await http.PostAsJsonAsync("/api/show", new { model = name }, ct);
                if (!resp.IsSuccessStatusCode) continue;
                var show = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (show.TryGetProperty("capabilities", out var caps) && caps.EnumerateArray().Any(c => c.GetString() == "vision")) return name;
            }
        }
        catch (Exception) { }   // ingen lokal KI-tjeneste – helt i orden
        return null;
    }
}

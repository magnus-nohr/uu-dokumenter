using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace UuDok.Ai;

/// <summary>Ingen KI: appen analyserer og gjør deterministiske reparasjoner, men lager ingen beskrivelser.</summary>
public sealed class NoAiProvider : IAltTextProvider
{
    public string Id => "none";
    public string Model => "-";
    public bool IsLocal => true;
    public Task<AiResult> SuggestAsync(AltTextRequest request, CancellationToken ct) =>
        Task.FromResult(AiResult.Fail(Id, Model, true, "KI er slått av."));
}

/// <summary>
/// Lokal modell via Ollama sitt HTTP-API på maskinen (tekst + bilde, strukturert JSON via «format»).
/// Adressen må være loopback; ellers er det ikke lokal behandling.
/// </summary>
public sealed class OllamaProvider : IAltTextProvider
{
    private readonly HttpClient _http;
    public string Id => "ollama";
    public string Model { get; }
    public bool IsLocal => true;

    public OllamaProvider(Uri baseUrl, string model, HttpMessageHandler? handler = null)
    {
        if (!baseUrl.IsLoopback) throw new ArgumentException("Lokal modell må kjøre på denne maskinen (127.0.0.1/localhost).");
        Model = model;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { UseProxy = false }) { BaseAddress = baseUrl, Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<AiResult> SuggestAsync(AltTextRequest req, CancellationToken ct)
    {
        var images = new JsonArray(Convert.ToBase64String(req.DisplayedImagePng));
        if (req.OriginalImagePng != null) images.Add(Convert.ToBase64String(req.OriginalImagePng));
        var body = new JsonObject
        {
            ["model"] = Model,
            ["stream"] = false,
            ["format"] = JsonNode.Parse(AiSchema.Json),
            ["options"] = new JsonObject { ["temperature"] = 0 },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = Prompt.System },
                new JsonObject { ["role"] = "user", ["content"] = Prompt.User(req), ["images"] = images }),
        };
        try
        {
            using var resp = await _http.PostAsJsonAsync("/api/chat", body, ct);
            if (!resp.IsSuccessStatusCode)
                return AiResult.Fail(Id, Model, true, resp.StatusCode == HttpStatusCode.NotFound
                    ? $"Modellen «{Model}» finnes ikke i den lokale KI-tjenesten." : $"Den lokale KI-tjenesten svarte med feil ({(int)resp.StatusCode}).");
            var json = await resp.Content.ReadFromJsonAsync<JsonObject>(ct);
            var content = (string?)json?["message"]?["content"];
            if (content == null) return AiResult.Fail(Id, Model, true, "Tomt svar fra lokal modell.");
            var (s, errors) = ResponseValidator.Parse(content, req);
            return s != null ? new AiResult(s, null, Id, Model, true) : AiResult.Fail(Id, Model, true, "Ugyldig svar fra modellen: " + string.Join("; ", errors));
        }
        catch (HttpRequestException) { return AiResult.Fail(Id, Model, true, "Fikk ikke kontakt med den lokale KI-tjenesten. Er den startet?"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return AiResult.Fail(Id, Model, true, "Den lokale modellen brukte for lang tid."); }
    }
}

/// <summary>
/// Valgfri skyleverandør (Anthropic). Brukes bare når brukeren eksplisitt har valgt sky og godkjent hva som sendes.
/// Standardmodell kan overstyres i innstillingene. Avvisninger («refusal») gir ikke forslag.
/// </summary>
public sealed class AnthropicProvider(string apiKey, string model = AnthropicProvider.DefaultModel) : IAltTextProvider
{
    public const string DefaultModel = "claude-opus-5-5";
    private readonly AnthropicClient _client = new() { ApiKey = apiKey };
    public string Id => "anthropic";
    public string Model { get; } = model;
    public bool IsLocal => false;

    public async Task<AiResult> SuggestAsync(AltTextRequest req, CancellationToken ct)
    {
        var content = new List<BetaContentBlockParam>
        {
            new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(req.DisplayedImagePng), MediaType = MediaType.ImagePng } },
        };
        if (req.OriginalImagePng != null)
            content.Add(new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(req.OriginalImagePng), MediaType = MediaType.ImagePng } });
        content.Add(new BetaTextBlockParam { Text = Prompt.User(req) });
        try
        {
            var resp = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 4000,
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
                System = Prompt.System,
                OutputConfig = new BetaOutputConfig { Effort = Effort.Medium, Format = new BetaJsonOutputFormat { Schema = AiSchema.AsDictionary() } },
                Messages = [new BetaUserMessageParam { Content = content }],
            }, ct);
            if (resp.StopReason == "refusal") return AiResult.Fail(Id, Model, false, "Leverandøren avviste forespørselen; ingen beskrivelse foreslått.");
            var text = string.Concat(resp.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            var (s, errors) = ResponseValidator.Parse(text, req);
            return s != null ? new AiResult(s, null, Id, Model, false) : AiResult.Fail(Id, Model, false, "Ugyldig svar: " + string.Join("; ", errors));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AiResult.Fail(Id, Model, false, "Skytjenesten svarte ikke som forventet (" + ex.GetType().Name + ").");
        }
    }
}

/// <summary>Testleverandør med forhåndsdefinerte svar. Dokumenterer integrasjon, ikke modellkvalitet.</summary>
public sealed class MockProvider(Func<AltTextRequest, string> respond, bool local = true) : IAltTextProvider
{
    public string Id => "mock";
    public string Model => "mock";
    public bool IsLocal => local;
    public int Calls { get; private set; }
    public Task<AiResult> SuggestAsync(AltTextRequest req, CancellationToken ct)
    {
        Calls++;
        var (s, errors) = ResponseValidator.Parse(respond(req), req);
        return Task.FromResult(s != null ? new AiResult(s, null, Id, Model, IsLocal) : AiResult.Fail(Id, Model, IsLocal, string.Join("; ", errors)));
    }
}

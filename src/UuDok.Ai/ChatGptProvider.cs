using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UuDok.Ai;

/// <summary>
/// ChatGPT via «Sign in with ChatGPT»: brukerens Plus/Pro-abonnement brukes til Responses API-kall.
/// Krav i denne flyten (developers.openai.com/siwc): store=false, stream=true, ingen temperature/max_output_tokens m.m.
/// Strukturert utdata (text.format) er ikke dokumentert for flyten; avvises den, sendes forespørselen på nytt uten,
/// og svaret valideres uansett mot skjemaet lokalt.
/// </summary>
public sealed class ChatGptProvider : IAltTextProvider
{
    public const string ApiBase = "https://api.openai.com/v1/";
    private readonly Func<CancellationToken, Task<string>> _token;
    private readonly HttpClient _http;
    private bool _formatUnsupported;

    public string Id => "chatgpt";
    public string Model { get; }
    public bool IsLocal => false;

    public ChatGptProvider(Func<CancellationToken, Task<string>> accessToken, string model, HttpMessageHandler? handler = null)
    {
        _token = accessToken;
        Model = model;
        _http = new HttpClient(handler ?? new SocketsHttpHandler()) { BaseAddress = new Uri(ApiBase), Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<AiResult> SuggestAsync(AltTextRequest req, CancellationToken ct)
    {
        try
        {
            var (status, text, code) = await Send(req, withFormat: !_formatUnsupported, ct);
            if (status == HttpStatusCode.BadRequest && code == "subscription_sharing_unsupported_capability" && !_formatUnsupported)
            {
                _formatUnsupported = true;
                (status, text, code) = await Send(req, withFormat: false, ct);
            }
            if (status != HttpStatusCode.OK) return AiResult.Fail(Id, Model, false, ErrorText(status, code));
            var json = ExtractJson(text);
            var (s, errors) = ResponseValidator.Parse(json, req);
            return s != null ? new AiResult(s, null, Id, Model, false) : AiResult.Fail(Id, Model, false, "Ugyldig svar fra ChatGPT: " + string.Join("; ", errors));
        }
        catch (ChatGptAuthException ex) { return AiResult.Fail(Id, Model, false, ex.Message); }
        catch (HttpRequestException) { return AiResult.Fail(Id, Model, false, "Fikk ikke kontakt med ChatGPT. Sjekk internettforbindelsen."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return AiResult.Fail(Id, Model, false, "ChatGPT brukte for lang tid."); }
    }

    private async Task<(HttpStatusCode Status, string Text, string? Code)> Send(AltTextRequest req, bool withFormat, CancellationToken ct)
    {
        var content = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = Prompt.User(req) },
            new JsonObject { ["type"] = "input_image", ["image_url"] = "data:image/png;base64," + Convert.ToBase64String(req.DisplayedImagePng) });
        if (req.OriginalImagePng != null)
            content.Add(new JsonObject { ["type"] = "input_image", ["image_url"] = "data:image/png;base64," + Convert.ToBase64String(req.OriginalImagePng) });
        var body = new JsonObject
        {
            ["model"] = Model,
            ["instructions"] = Prompt.System + (withFormat ? "" : "\nSvar med ett JSON-objekt og ingenting annet."),
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            ["store"] = false,
            ["stream"] = true,
        };
        if (withFormat)
            body["text"] = new JsonObject { ["format"] = new JsonObject { ["type"] = "json_schema", ["name"] = "alt_text", ["strict"] = true, ["schema"] = JsonNode.Parse(AiSchema.Json) } };

        using var msg = new HttpRequestMessage(HttpMethod.Post, "responses") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token(ct));
        using var resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            return (resp.StatusCode, "", ErrorCode(err));
        }
        // Server-sent events: samle tekst fra response.output_text.delta; ferdig først ved response.completed.
        var sb = new StringBuilder();
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (!line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data is "" or "[DONE]") continue;
            JsonNode? ev;
            try { ev = JsonNode.Parse(data); } catch (JsonException) { continue; }
            switch ((string?)ev?["type"])
            {
                case "response.output_text.delta": sb.Append((string?)ev!["delta"]); break;
                case "response.completed": return (HttpStatusCode.OK, sb.ToString(), null);
                case "response.failed" or "response.incomplete" or "error":
                    var code = (string?)ev!["response"]?["error"]?["code"] ?? (string?)ev["error"]?["code"] ?? (string?)ev["code"];
                    return (code == "subscription_sharing_usage_limit_exceeded" ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadGateway, "", code);
            }
        }
        return (HttpStatusCode.BadGateway, "", "stream_ended");
    }

    private static string? ErrorCode(string body)
    {
        try { var n = JsonNode.Parse(body); return (string?)n?["error"]?["code"] ?? (string?)n?["detail"]; } catch (JsonException) { return null; }
    }

    private static string ExtractJson(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```")) t = t.Trim('`').Replace("json", "", StringComparison.OrdinalIgnoreCase).Trim();
        var a = t.IndexOf('{'); var b = t.LastIndexOf('}');
        return a >= 0 && b > a ? t[a..(b + 1)] : t;
    }

    public static string ErrorText(HttpStatusCode status, string? code) => code switch
    {
        "subscription_sharing_user_not_eligible" => "ChatGPT-kontoen din kan ikke brukes i andre apper. Det krever ChatGPT Plus eller Pro.",
        "subscription_sharing_usage_limit_exceeded" => "Grensen for bruk i ChatGPT-abonnementet er nådd. Se «Administrer bruk» på chatgpt.com/settings/usage, eller prøv igjen senere.",
        "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" => "ChatGPT er midlertidig utilgjengelig. Prøv igjen senere.",
        "subscription_sharing_invalid_user" => "Innloggingen til ChatGPT er ikke lenger gyldig. Koble til kontoen på nytt i Innstillinger.",
        "subscription_sharing_route_not_supported" => "ChatGPT-abonnementet kan ikke brukes fra denne regionen eller tjenesten.",
        _ when status == HttpStatusCode.Unauthorized => "Innloggingen til ChatGPT er ikke lenger gyldig. Koble til kontoen på nytt i Innstillinger.",
        _ when status == HttpStatusCode.TooManyRequests => "Grensen for bruk i ChatGPT-abonnementet er nådd. Prøv igjen senere.",
        _ => $"ChatGPT svarte med feil ({(int)status}{(code != null ? ", " + code : "")}).",
    };
}

public sealed class ChatGptAuthException(string message) : Exception(message);

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using UuDok.Ai;
using UuDok.Web.Security;

namespace UuDok.Web;

/// <summary>
/// «Sign in with ChatGPT» (developers.openai.com/siwc), åpen kildekode-flyten:
/// OAuth 2.0 + PKCE (S256), retur til http://127.0.0.1:1455/auth/callback, første innlogging registrerer appen
/// (client_id=dynamic_agent_client) og får utstedt en egen client_id. ID-tokenet kontrolleres mot OpenAIs JWKS.
/// Access- og refresh-token lagres bare i Windows Legitimasjonsbehandling og fornyes automatisk (refresh-token roterer).
/// </summary>
public sealed class ChatGptAuth(SettingsService settings, ISecretStore secrets, ILogger<ChatGptAuth> log, HttpMessageHandler? handler = null, string? diagnosePath = null)
{
    public const string Issuer = "https://auth.openai.com";
    public const string AuthorizeUrl = Issuer + "/api/accounts/authorize";
    public const string TokenUrl = Issuer + "/api/accounts/oauth/token";
    public const string JwksUrl = Issuer + "/.well-known/jwks.json";
    public const string DiscoveryUrl = Issuer + "/.well-known/openid-configuration";
    public const string Resource = "https://api.openai.com/v1";
    public const string Scopes = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    public const int CallbackPort = 1455;
    public static string RedirectUri => $"http://127.0.0.1:{CallbackPort}/auth/callback";
    private const string SecretName = "chatgpt";

    private readonly HttpClient _http = new(handler ?? new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private PendingLogin? _pending;
    private string? _lastError;
    private string _step = "";

    /// <summary>Diagnose for innlogging: trinn, statuskoder og feltnavn. Aldri tokens, koder eller hemmeligheter.</summary>
    private void Diag(string line)
    {
        if (diagnosePath == null) return;
        try { File.AppendAllText(diagnosePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); } catch { }
    }

    private sealed record PendingLogin(string State, string Nonce, string Verifier, string ClientIdUsed, DateTimeOffset Expires, TcpListener Listener);
    private sealed record Tokens(string Access, string Refresh, DateTimeOffset ExpiresAt);

    public bool Connected => secrets.Get(SecretName) is { Length: > 0 };
    public bool LoginInProgress => _pending != null && _pending.Expires > DateTimeOffset.Now;
    public string? LastError => _lastError;

    /// <summary>Starter innlogging: lytter på callback-porten og returnerer adressen brukeren skal åpne.</summary>
    public string BeginLogin()
    {
        if (!secrets.IsAvailable) throw new InvalidOperationException("Sikker lagring av innlogging (Windows Legitimasjonsbehandling) er ikke tilgjengelig.");
        CancelLogin();
        var s = settings.Get();
        // Uten lagret app-ID er en tidligere installasjons-ID ubrukelig (den kan allerede være registrert hos OpenAI
        // med en app-ID vi ikke kjenner). Ny registrering bruker derfor alltid en ny installasjons-ID.
        if (s.ChatGptHostId == null || s.ChatGptClientId == null) { s = s with { ChatGptHostId = "urn:uuid:" + Guid.NewGuid() }; settings.Save(s); }
        var listener = new TcpListener(IPAddress.Loopback, CallbackPort);
        try { listener.Start(); }
        catch (SocketException) { throw new InvalidOperationException($"Port {CallbackPort} på denne maskinen er opptatt (for eksempel av et annet program som logger inn i ChatGPT). Lukk det og prøv igjen."); }

        var verifier = B64(RandomNumberGenerator.GetBytes(32));
        var challenge = B64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64(RandomNumberGenerator.GetBytes(16));
        var nonce = B64(RandomNumberGenerator.GetBytes(16));
        var firstTime = s.ChatGptClientId == null;
        var clientId = firstTime ? "dynamic_agent_client" : s.ChatGptClientId!;
        var q = HttpUtility.ParseQueryString("");
        q["client_id"] = clientId;
        if (firstTime) q["agent_name_hint"] = "UU-dokumenter";
        q["ext_agent_host_id"] = s.ChatGptHostId;
        q["response_type"] = "code";
        q["redirect_uri"] = RedirectUri;
        q["scope"] = Scopes;
        q["resource"] = Resource;
        q["state"] = state;
        q["nonce"] = nonce;
        q["code_challenge"] = challenge;
        q["code_challenge_method"] = "S256";
        _pending = new PendingLogin(state, nonce, verifier, clientId, DateTimeOffset.Now.AddMinutes(10), listener);
        Diag($"Innlogging startet ({(firstTime ? "ny registrering" : "kjent app-ID")}). Venter på tilbakekall på {RedirectUri}.");
        _lastError = null;
        _ = Task.Run(() => AwaitCallback(_pending));
        return AuthorizeUrl + "?" + q;
    }

    public void CancelLogin()
    {
        try { _pending?.Listener.Stop(); } catch { }
        _pending = null;
    }

    private async Task AwaitCallback(PendingLogin p)
    {
        using var cts = new CancellationTokenSource(p.Expires - DateTimeOffset.Now);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                using var client = await p.Listener.AcceptTcpClientAsync(cts.Token);
                using var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(cts.Token) ?? "";
                string? l; while (!string.IsNullOrEmpty(l = await reader.ReadLineAsync(cts.Token))) { }   // les og forkast headere
                var target = requestLine.Split(' ').ElementAtOrDefault(1) ?? "";
                if (!target.StartsWith("/auth/callback")) { await Respond(stream, 404, "Ikke funnet."); continue; }
                var query = HttpUtility.ParseQueryString(target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "");
                string message;
                bool ok;
                try { (ok, message) = await CompleteLogin(p, query, cts.Token); }
                catch (Exception ex)
                {
                    ok = false;
                    message = $"Innloggingen mislyktes under «{_step}»: {ex.Message}";
                    Diag($"FEIL i «{_step}»: {ex.GetType().Name}: {ex.Message}");
                    log.LogWarning("ChatGPT-innlogging feilet i {Step}: {Type}", _step, ex.GetType().Name);
                }
                if (!ok) Diag("Resultat: " + message);
                _lastError = ok ? null : message;
                await Respond(stream, 200, message, ok);
                break;
            }
        }
        catch (OperationCanceledException) { _lastError ??= "Innloggingen ble ikke fullført i tide."; Diag("Ingen tilbakekall innen tidsfristen (eller innloggingen ble avbrutt/startet på nytt)."); }
        finally { try { p.Listener.Stop(); } catch { } if (ReferenceEquals(_pending, p)) _pending = null; }
    }

    private async Task<(bool, string)> CompleteLogin(PendingLogin p, System.Collections.Specialized.NameValueCollection q, CancellationToken ct)
    {
        _step = "tilbakekall";
        Diag($"Tilbakekall mottatt. Parametere: {string.Join(", ", q.AllKeys)}{(q["error"] != null ? $", error={q["error"]}, beskrivelse={q["error_description"]}" : "")}");
        if (q["error"] == "access_denied") return (false, "Du avbrøt innloggingen. Ingenting er koblet til.");
        if (q["error"] is { } e) return (false, "ChatGPT avviste innloggingen (" + e + ").");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(q["state"] ?? ""), Encoding.UTF8.GetBytes(p.State)))
            return (false, "Ugyldig svar på innloggingen (state). Prøv igjen.");
        var code = q["code"] ?? throw new InvalidOperationException("Mangler kode.");
        var clientId = q["client_id"] ?? (p.ClientIdUsed == "dynamic_agent_client" ? null : p.ClientIdUsed);
        if (clientId == null) return (false, "ChatGPT returnerte ingen app-ID. Prøv igjen.");
        // Lagre utstedt app-ID med en gang, slik at et nytt forsøk ikke registrerer appen på nytt.
        if (settings.Get().ChatGptClientId != clientId) settings.Save(settings.Get() with { ChatGptClientId = clientId });

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code,
            ["code_verifier"] = p.Verifier, ["redirect_uri"] = RedirectUri, ["resource"] = Resource,
        };
        _step = "utveksling av kode";
        using var resp = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        JsonNode? body = null;
        try { body = JsonNode.Parse(raw); } catch (System.Text.Json.JsonException) { }
        Diag($"Token-endepunkt: HTTP {(int)resp.StatusCode}, innholdstype {resp.Content.Headers.ContentType}, felter: {string.Join(", ", (body as JsonObject)?.Select(kv => kv.Key) ?? ["(ikke JSON)"])}" +
             $"{(body?["error"] != null ? $", error={(string?)body["error"]}, beskrivelse={(string?)body["error_description"]}" : "")}");
        if (!resp.IsSuccessStatusCode || body == null)
            return (false, "Innloggingen kunne ikke fullføres (" + ((string?)body?["error_description"] ?? (string?)body?["error"] ?? "HTTP " + (int)resp.StatusCode) + "). Prøv igjen.");
        var scope = (string?)body["scope"] ?? "";
        if (!scope.Split(' ').Contains("chatgpt.tokens.use.direct"))
            return (false, "ChatGPT ga ikke tilgang til å bruke abonnementet i appen. Det krever ChatGPT Plus eller Pro, og at du godtar tilgangen.");
        _step = "kontroll av ID-token";
        var claims = await ValidateIdToken((string?)body["id_token"] ?? throw new InvalidOperationException("Svaret manglet ID-token."), clientId, p.Nonce, ct);
        _step = "lagring";
        var access = (string?)body["access_token"] ?? throw new InvalidOperationException("Svaret manglet access-token.");
        var refresh = (string?)body["refresh_token"] ?? throw new InvalidOperationException("Svaret manglet refresh-token (offline_access).");
        var expires = body["expires_in"] is JsonValue ev && ev.TryGetValue<double>(out var secs) ? secs : 3600;
        Save(new Tokens(access, refresh, DateTimeOffset.Now.AddSeconds(expires)));
        Diag("Innlogging fullført.");
        var s = settings.Get();
        var account = (string?)claims["email"] ?? (string?)claims["name"] ?? "ChatGPT-konto";
        settings.Save(s with { ChatGptClientId = clientId, ChatGptAccount = account, ChatGptConsentAt = s.ChatGptConsentAt ?? DateTimeOffset.Now });
        return (true, "Du er koblet til ChatGPT som " + account + ". Du kan lukke denne fanen og gå tilbake til UU-dokumenter.");
    }

    /// <summary>Kontroll av ID-tokenet: RS256-signatur mot JWKS, iss, aud, exp og nonce.</summary>
    private async Task<JsonObject> ValidateIdToken(string jwt, string clientId, string nonce, CancellationToken ct)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) throw new InvalidOperationException("Ugyldig ID-token.");
        var header = JsonNode.Parse(FromB64(parts[0]))!;
        var claims = JsonNode.Parse(FromB64(parts[1]))!.AsObject();
        Diag($"ID-token: alg={(string?)header["alg"]}, kid={(string?)header["kid"]}, felter={string.Join(", ", claims.Select(kv => kv.Key))}, iss={(string?)claims["iss"]}, " +
             $"aud={claims["aud"]?.ToJsonString()}, forventet aud={clientId}, nonce lik={(string?)claims["nonce"] == nonce}");
        if ((string?)header["alg"] != "RS256") throw new InvalidOperationException("Uventet signaturalgoritme.");
        var jwks = JsonNode.Parse(await _http.GetStringAsync(JwksUrl, ct))!;
        var keys = jwks["keys"]!.AsArray();
        Diag($"JWKS: {keys.Count} nøkler, kid: {string.Join(", ", keys.Select(k => (string?)k!["kid"]))}");
        var key = keys.FirstOrDefault(k => (string?)k!["kid"] == (string?)header["kid"]) ?? (keys.Count == 1 && header["kid"] == null ? keys[0] : null)
            ?? throw new InvalidOperationException("Ukjent signaturnøkkel.");
        using var rsa = RSA.Create(new RSAParameters { Modulus = FromB64((string)key["n"]!), Exponent = FromB64((string)key["e"]!) });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), FromB64(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidOperationException("Ugyldig signatur på ID-token.");
        if ((string?)claims["iss"] != Issuer) throw new InvalidOperationException("Feil utsteder.");
        var aud = claims["aud"];
        var audOk = aud is JsonArray arr ? arr.Any(a => (string?)a == clientId) : (string?)aud == clientId;
        if (!audOk) throw new InvalidOperationException("Feil mottaker.");
        if (claims["exp"] is not JsonValue expV || !expV.TryGetValue<double>(out var exp) || DateTimeOffset.FromUnixTimeSeconds((long)exp) < DateTimeOffset.UtcNow.AddMinutes(-2))
            throw new InvalidOperationException("Utløpt eller manglende utløpstid i ID-token.");
        if ((string?)claims["nonce"] != nonce) throw new InvalidOperationException("Feil nonce.");
        return claims;
    }

    /// <summary>Gyldig access-token; fornyer ved behov (serialisert, fordi refresh-token roterer).</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var t = Load() ?? throw new ChatGptAuthException("Du er ikke koblet til ChatGPT. Koble til kontoen i Innstillinger.");
        if (t.ExpiresAt > DateTimeOffset.Now.AddMinutes(2)) return t.Access;
        await _refreshLock.WaitAsync(ct);
        try
        {
            t = Load() ?? throw new ChatGptAuthException("Du er ikke koblet til ChatGPT.");
            if (t.ExpiresAt > DateTimeOffset.Now.AddMinutes(2)) return t.Access;
            var clientId = settings.Get().ChatGptClientId ?? throw new ChatGptAuthException("Mangler app-ID for ChatGPT. Koble til på nytt.");
            var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = t.Refresh, ["resource"] = Resource };
            using var resp = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
            var body = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!resp.IsSuccessStatusCode)
            {
                var err = (string?)body?["error"];
                if (err is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused")
                    secrets.Delete(SecretName);
                throw new ChatGptAuthException("Innloggingen til ChatGPT er utløpt. Koble til kontoen på nytt i Innstillinger.");
            }
            var nt = new Tokens((string)body!["access_token"]!, (string?)body["refresh_token"] ?? t.Refresh, DateTimeOffset.Now.AddSeconds(body["expires_in"] is JsonValue rv && rv.TryGetValue<double>(out var rs) ? rs : 3600));
            Save(nt);
            return nt.Access;
        }
        finally { _refreshLock.Release(); }
    }

    /// <summary>Modeller brukeren kan bruke (synlige i katalogen for kontoen).</summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, ChatGptProvider.ApiBase + "models");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];
        var n = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        var items = (n?["data"] as JsonArray ?? n?["models"] as JsonArray ?? []).OfType<JsonNode>();
        return items.Where(m => (string?)m["visibility"] is null or "list")
            .Select(m => (string?)m["slug"] ?? (string?)m["id"]).OfType<string>().Distinct().ToList();
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        var t = Load();
        var clientId = settings.Get().ChatGptClientId;
        if (t != null && clientId != null)
        {
            try
            {
                var disc = JsonNode.Parse(await _http.GetStringAsync(DiscoveryUrl, ct));
                if ((string?)disc?["revocation_endpoint"] is { } rev)
                    await _http.PostAsync(rev, new FormUrlEncodedContent(new Dictionary<string, string>
                        { ["token"] = t.Refresh, ["token_type_hint"] = "refresh_token", ["client_id"] = clientId }), ct);
            }
            catch (Exception ex) { log.LogWarning("Tilbakekalling av ChatGPT-token feilet: {Type}", ex.GetType().Name); }
        }
        secrets.Delete(SecretName);
        settings.Save(settings.Get() with { ChatGptAccount = null });
    }

    private Tokens? Load()
    {
        var raw = secrets.Get(SecretName);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonSerializer.Deserialize<Tokens>(raw); } catch (JsonException) { return null; }
    }

    private void Save(Tokens t) => secrets.Set(SecretName, JsonSerializer.Serialize(t));

    private static async Task Respond(NetworkStream s, int status, string message, bool ok = false)
    {
        var html = "<!doctype html><html lang=\"nb\"><head><meta charset=\"utf-8\"><title>UU-dokumenter – ChatGPT</title>" +
                   "<style>body{font:1.1rem/1.5 system-ui,sans-serif;max-width:40rem;margin:3rem auto;padding:0 1rem}</style></head><body>" +
                   $"<h1>{(ok ? "Koblet til ChatGPT" : "Ikke koblet til")}</h1><p>{WebUtility.HtmlEncode(message)}</p>" +
                   "<p><a href=\"http://127.0.0.1:5317/#/innstillinger\">Tilbake til UU-dokumenter</a></p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\n" +
                   "Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head));
        await s.WriteAsync(bytes);
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Extensions.Logging.Abstractions;
using UuDok.Ai;
using UuDok.Web;
using UuDok.Web.Security;

namespace UuDok.Tests;

/// <summary>
/// «Sign in with ChatGPT» og Responses-kall, testet mot en falsk OpenAI (ingen nettverk).
/// Dokumenterer integrasjonen, ikke at OpenAIs tjeneste svarer slik i praksis.
/// </summary>
[Collection("chatgpt")]
public class ChatGptTests
{
    private static string Sse(params object[] events) => string.Concat(events.Select(e => "data: " + JsonSerializer.Serialize(e) + "\n\n"));

    private static AltTextRequest Req() => new()
    {
        ObjectId = "obj-1", DisplayedImagePng = [1, 2, 3], DocumentKind = "presentasjon", Language = "nb-NO", NearbyText = "Cellens deler", ImageWidth = 800, ImageHeight = 600,
    };

    private const string Answer = """{"objectId":"obj-1","functionalRole":"informative","proposedAltText":"Skisse av en planteselle med cellevegg og cellekjerne.","proposedLongDescription":null,"language":"nb-NO","evidence":["synlig i bildet"],"uncertaintyReasons":[],"requiresReview":false,"suggestedAction":"setAltText"}""";

    [Fact]
    public async Task Responses_kall_folger_kravene_og_strommet_svar_valideres()
    {
        string? sent = null;
        var handler = new FakeHandler(r =>
        {
            sent = r.Content!.ReadAsStringAsync().Result;
            Assert.Equal("Bearer test-token", r.Headers.Authorization!.ToString());
            Assert.Equal("https://api.openai.com/v1/responses", r.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Sse(new { type = "response.output_text.delta", delta = Answer[..40] }, new { type = "response.output_text.delta", delta = Answer[40..] },
                    new { type = "response.completed" }), Encoding.UTF8, "text/event-stream"),
            };
        });
        var p = new ChatGptProvider(_ => Task.FromResult("test-token"), "gpt-test", handler);
        var res = await p.SuggestAsync(Req(), default);
        Assert.NotNull(res.Suggestion);
        Assert.Equal("Skisse av en planteselle med cellevegg og cellekjerne.", res.Suggestion!.ProposedAltText);
        var body = JsonNode.Parse(sent!)!;
        Assert.False((bool)body["store"]!);
        Assert.True((bool)body["stream"]!);
        Assert.Null(body["temperature"]);
        Assert.Null(body["max_output_tokens"]);
        Assert.StartsWith("data:image/png;base64,", (string)body["input"]![0]!["content"]![1]!["image_url"]!);
        Assert.DoesNotContain(".pptx", sent);
    }

    [Fact]
    public async Task Ustottet_strukturert_utdata_gir_nytt_forsok_uten_format()
    {
        int calls = 0; bool secondHadFormat = true;
        var handler = new FakeHandler(r =>
        {
            calls++;
            var body = JsonNode.Parse(r.Content!.ReadAsStringAsync().Result)!;
            if (calls == 1)
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("""{"error":{"code":"subscription_sharing_unsupported_capability","param":"text.format"}}""") };
            secondHadFormat = body["text"] != null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sse(new { type = "response.output_text.delta", delta = "```json\n" + Answer + "\n```" }, new { type = "response.completed" })) };
        });
        var res = await new ChatGptProvider(_ => Task.FromResult("t"), "gpt-test", handler).SuggestAsync(Req(), default);
        Assert.Equal(2, calls);
        Assert.False(secondHadFormat);
        Assert.NotNull(res.Suggestion);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "subscription_sharing_user_not_eligible", "Plus eller Pro")]
    [InlineData(HttpStatusCode.TooManyRequests, "subscription_sharing_usage_limit_exceeded", "Administrer bruk")]
    public async Task Feil_fra_abonnementet_gir_forstaelig_norsk_melding(HttpStatusCode status, string code, string expected)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(new { error = new { code } })) });
        var res = await new ChatGptProvider(_ => Task.FromResult("t"), "gpt-test", handler).SuggestAsync(Req(), default);
        Assert.Null(res.Suggestion);
        Assert.Contains(expected, res.Error);
    }

    [Fact]
    public async Task Innlogging_med_PKCE_tilbakekall_til_loopback_og_kontroll_av_ID_token()
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(false);
        string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string? nonce = null;
        var tokenCalls = new List<Dictionary<string, string>>();
        var handler = new FakeHandler(r =>
        {
            var url = r.RequestUri!.ToString();
            if (url == ChatGptAuth.JwksUrl)
                return Json(new { keys = new[] { new { kty = "RSA", kid = "k1", n = B64(p.Modulus!), e = B64(p.Exponent!), alg = "RS256" } } });
            if (url == ChatGptAuth.TokenUrl)
            {
                var form = HttpUtility.ParseQueryString(r.Content!.ReadAsStringAsync().Result);
                tokenCalls.Add(form.AllKeys.ToDictionary(k => k!, k => form[k]!));
                if (form["grant_type"] == "refresh_token")
                    return Json(new { access_token = "access-2", refresh_token = "refresh-2", expires_in = 3600, token_type = "Bearer" });
                var head = B64(Encoding.UTF8.GetBytes("""{"alg":"RS256","kid":"k1"}"""));
                var claims = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { iss = ChatGptAuth.Issuer, aud = "oaiapp_test", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), nonce, sub = "u1", email = "laerer@example.org" })));
                var sig = B64(rsa.SignData(Encoding.ASCII.GetBytes(head + "." + claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                return Json(new { access_token = "access-1", refresh_token = "refresh-1", id_token = $"{head}.{claims}.{sig}", expires_in = 1, token_type = "Bearer", scope = ChatGptAuth.Scopes });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var dir = Corpus.TempDir();
        var settings = new SettingsService(Path.Combine(dir, "settings.json"));
        settings.Save(new AppSettings { ChatGptConsentAt = DateTimeOffset.Now });
        var secrets = new InMemorySecretStore();
        var auth = new ChatGptAuth(settings, secrets, NullLogger<ChatGptAuth>.Instance, handler);

        var url = auth.BeginLogin();
        var q = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.StartsWith(ChatGptAuth.AuthorizeUrl, url);
        Assert.Equal("dynamic_agent_client", q["client_id"]);
        Assert.Equal("http://127.0.0.1:1455/auth/callback", q["redirect_uri"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Contains("chatgpt.tokens.use.direct", q["scope"]);
        Assert.Equal("https://api.openai.com/v1", q["resource"]);
        Assert.StartsWith("urn:uuid:", q["ext_agent_host_id"]);
        nonce = q["nonce"];

        // Feil «state» avvises
        using var http = new HttpClient();
        // Riktig tilbakekall fra OpenAI (nettleseren omdirigeres hit)
        var cb = await http.GetStringAsync($"http://127.0.0.1:1455/auth/callback?code=abc&state={Uri.EscapeDataString(q["state"]!)}&client_id=oaiapp_test&scope={Uri.EscapeDataString(ChatGptAuth.Scopes)}");
        Assert.Contains("Koblet til ChatGPT", cb);
        Assert.True(auth.Connected);
        Assert.Equal("oaiapp_test", settings.Get().ChatGptClientId);
        Assert.Equal("laerer@example.org", settings.Get().ChatGptAccount);
        var exchange = tokenCalls.Single();
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal("oaiapp_test", exchange["client_id"]);
        Assert.True(exchange["code_verifier"].Length >= 43);
        Assert.DoesNotContain("access-1", File.ReadAllText(Path.Combine(dir, "settings.json")));   // tokens aldri i innstillingsfilen

        // Tokenet utløp straks (expires_in = 1): neste bruk fornyer med utstedt client_id, og refresh-token roteres.
        Assert.Equal("access-2", await auth.GetAccessTokenAsync(default));
        Assert.Equal("refresh_token", tokenCalls[1]["grant_type"]);
        Assert.Equal("refresh-1", tokenCalls[1]["refresh_token"]);
        Assert.Equal("oaiapp_test", tokenCalls[1]["client_id"]);
        Assert.Contains("refresh-2", secrets.Get("chatgpt"));
    }

    [Fact]
    public void Store_hemmeligheter_lagres_i_flere_deler_i_Legitimasjonsbehandling()
    {
        // Observert ved første ekte innlogging: tokens fra ChatGPT er større enn 2 560 byte (grensen per oppføring).
        if (!OperatingSystem.IsWindows()) Assert.Skip("Bare Windows.");
        var store = new WindowsCredentialStore();
        var name = "test-" + Guid.NewGuid().ToString("N");
        var big = string.Concat(Enumerable.Repeat("eyJhbGciOiJSUzI1NiJ9.", 300));     // ca. 6 300 tegn
        try
        {
            store.Set(name, big);
            Assert.Equal(big, store.Get(name));
            store.Set(name, "kort");                                                   // overskriving rydder bort gamle deler
            Assert.Equal("kort", store.Get(name));
        }
        finally { store.Delete(name); }
        Assert.Null(store.Get(name));
    }

    private static HttpResponseMessage Json(object o) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };
}

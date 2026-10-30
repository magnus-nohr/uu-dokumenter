using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UuDok.Web.Jobs;

namespace UuDok.Tests;

/// <summary>Webkontrakt og lokal sikkerhet. Kjøres uten Office (ingen hjelper) og med nøkkellager i minnet.</summary>
[Collection("web")]
public sealed class WebApiTests : IDisposable
{
    private readonly string _data = Corpus.TempDir();
    private readonly WebApplicationFactory<Program> _factory;

    public WebApiTests()
    {
        Environment.SetEnvironmentVariable("UuDok__DataDir", _data);
        Environment.SetEnvironmentVariable("UuDok__Helper", Path.Combine(_data, "finnes-ikke.exe"));
        Environment.SetEnvironmentVariable("UuDok__InMemorySecrets", "true");
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost:5317"), HandleCookies = true });

    private static async Task<string> Token(HttpClient c) =>
        (await c.GetFromJsonAsync<JsonElement>("/api/session")).GetProperty("csrfToken").GetString()!;

    private static MultipartFormDataContent Form(params string[] files)
    {
        var f = new MultipartFormDataContent();
        foreach (var name in files)
        {
            var b = new ByteArrayContent(Corpus.Bytes(name));
            b.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            f.Add(b, "files", Path.GetFileName(name));
        }
        f.Add(new StringContent("PreserveAppearance"), "mode");
        f.Add(new StringContent("none"), "ai");
        return f;
    }

    private static async Task<JsonElement> WaitDone(HttpClient c, string id)
    {
        for (int i = 0; i < 300; i++)
        {
            var j = await c.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}");
            var s = j.GetProperty("job").GetProperty("status").GetString();
            if (s is "done" or "failed" or "rejected") return j;
            await Task.Delay(200);
        }
        throw new TimeoutException();
    }

    [Fact]
    public async Task Endringsforesporsler_krever_CSRF_token()
    {
        var c = Client();
        await Token(c);
        var r = await c.PostAsync("/api/jobs", Form("office-presentasjon.pptx"));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Fremmed_origin_og_host_avvises()
    {
        var c = Client();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        req.Headers.Add("Origin", "http://ondsinnet.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
        var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        req2.Headers.Host = "ondsinnet.example";
        Assert.Equal(HttpStatusCode.MisdirectedRequest, (await c.SendAsync(req2)).StatusCode);
    }

    [Fact]
    public async Task Sikkerhetsheadere_settes()
    {
        var r = await Client().GetAsync("/api/session");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains(r.Headers.GetValues("Set-Cookie"), v => v.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && v.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Hele_flyten_opplasting_behandling_nedlasting_rapport_gjennomgang_sletting()
    {
        var c = Client();
        var token = await Token(c);
        c.DefaultRequestHeaders.Add("X-UuDok-Csrf", token);
        var up = await (await c.PostAsync("/api/jobs", Form("office-presentasjon.pptx", "ondsinnet/tekst-forkledd.docx"))).Content.ReadFromJsonAsync<JsonElement>();
        var jobs = up.GetProperty("jobs").EnumerateArray().ToList();
        Assert.True(jobs[1].TryGetProperty("error", out var err) && err.GetString()!.Length > 0);   // innhold, ikke filendelse
        var id = jobs[0].GetProperty("id").GetString()!;

        var done = await WaitDone(c, id);
        Assert.Equal("done", done.GetProperty("job").GetProperty("status").GetString());
        var sentences = done.GetProperty("summary").GetProperty("sentences").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("Visuell bevaring er ikke kontrollert i Office.", sentences);

        var dl = await c.GetAsync($"/api/jobs/{id}/download");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        Assert.Equal("office-presentasjon-forbedret.pptx", dl.Content.Headers.ContentDisposition!.FileNameStar ?? dl.Content.Headers.ContentDisposition.FileName!.Trim('"'));
        var bytes = await dl.Content.ReadAsByteArrayAsync();
        Assert.True(UuDok.Intake.FileInspector.Inspect(bytes).Accepted);

        var rep = await c.GetAsync($"/api/jobs/{id}/report.html");
        Assert.Contains("sandbox", rep.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("Tilgjengelighetsrapport", await rep.Content.ReadAsStringAsync());

        var review = await c.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}/review");
        var item = review.GetProperty("items").EnumerateArray().First(i => i.GetProperty("kind").GetString() == "SetDocumentTitle");
        var post = await c.PostAsJsonAsync($"/api/jobs/{id}/review", new { decisions = new[] { new { changeId = item.GetProperty("id").GetString(), action = "edit", value = "Naturfag – planter" } } });
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var after = await WaitDone(c, id);
        Assert.Contains(after.GetProperty("summary").GetProperty("sentences").EnumerateArray(), s => s.GetString()!.Contains("Naturfag – planter"));

        // «Bruk alle forslag»: alle ventende forslag med innhold brukes, og usikkerheten beholdes i rapporten.
        var pendingBefore = (await c.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}/review")).GetProperty("items").EnumerateArray()
            .Count(i => i.GetProperty("pending").GetBoolean() && i.GetProperty("value").GetString()!.Length > 0);
        if (pendingBefore > 0)
        {
            var all = await (await c.PostAsync($"/api/jobs/{id}/accept-all", null)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(pendingBefore, all.GetProperty("count").GetInt32());
            await WaitDone(c, id);
            var left = (await c.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}/review")).GetProperty("items").EnumerateArray()
                .Count(i => i.GetProperty("pending").GetBoolean() && i.GetProperty("value").GetString()!.Length > 0);
            Assert.Equal(0, left);
        }

        // Ugyldig handling avvises med forklaring
        var bad = await c.PostAsJsonAsync($"/api/jobs/{id}/review", new { decisions = new[] { new { changeId = "C9999", action = "accept" } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync($"/api/jobs/{id}")).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_data, "jobs", id)));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/jobs/{id}")).StatusCode);
    }

    [Fact]
    public async Task Stiangrep_i_jobb_og_bildeadresser_avvises()
    {
        var c = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/jobs/..%2F..%2Fwindows/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/jobs/{Guid.NewGuid():N}/image/..%2F..%2Fstate")).StatusCode);
    }

    [Fact]
    public async Task Innstillinger_avviser_lokal_KI_utenfor_maskinen_og_nokkel_lekker_ikke()
    {
        var c = Client();
        c.DefaultRequestHeaders.Add("X-UuDok-Csrf", await Token(c));
        var r = await c.PutAsJsonAsync("/api/settings", new { localAiUrl = "http://192.168.1.10:11434", retentionDays = 7 });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/settings/cloud-key", new { key = "sk-test-0123456789abcdefghijklmnop" })).StatusCode);
        var s = await c.GetStringAsync("/api/settings");
        Assert.DoesNotContain("sk-test", s);
        Assert.Contains("\"cloudKeyStored\":true", s);
        foreach (var file in Directory.EnumerateFiles(_data, "*", SearchOption.AllDirectories))
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assert.DoesNotContain("sk-test", new StreamReader(fs).ReadToEnd());          // nøkkelen finnes ikke i noen fil (også SQLite)
        }
    }

    [Fact]
    public async Task Skyjobb_uten_godkjenning_stoppes_og_faller_ikke_tilbake()
    {
        var c = Client();
        c.DefaultRequestHeaders.Add("X-UuDok-Csrf", await Token(c));
        var f = Form("office-presentasjon.pptx");
        f.Add(new StringContent("cloud"), "ai");   // siste verdi
        var form = new MultipartFormDataContent();
        foreach (var part in f.Where(p => p.Headers.ContentDisposition?.Name?.Trim('"') != "ai")) form.Add(part);
        form.Add(new StringContent("cloud"), "ai");
        var up = await (await c.PostAsync("/api/jobs", form)).Content.ReadFromJsonAsync<JsonElement>();
        var id = up.GetProperty("jobs")[0].GetProperty("id").GetString()!;
        var done = await WaitDone(c, id);
        Assert.Equal("failed", done.GetProperty("job").GetProperty("status").GetString());
        Assert.Equal("ai_config", done.GetProperty("job").GetProperty("errorCode").GetString());
    }
}

[Collection("web")]
public sealed class RestartTests
{
    [Fact]
    public async Task Jobb_som_var_i_gang_ved_stopp_gjenopptas_ved_oppstart()
    {
        var data = Corpus.TempDir();
        Environment.SetEnvironmentVariable("UuDok__DataDir", data);
        Environment.SetEnvironmentVariable("UuDok__Helper", Path.Combine(data, "x.exe"));
        Environment.SetEnvironmentVariable("UuDok__InMemorySecrets", "true");
        Directory.CreateDirectory(Path.Combine(data, "jobs"));
        var store = new JobStore(Path.Combine(data, "jobs.db"));
        var paths = new Paths(data);
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(paths.Work(id));
        File.WriteAllBytes(paths.Original(id), Corpus.Bytes("office-dokument.docx"));
        store.Insert(new JobRow(id, DateTimeOffset.Now, DateTimeOffset.Now, JobStatus.Running, "Analyserer", 40, null, "dok.docx", "docx",
            JsonSerializer.Serialize(new JobRequest(UuDok.Core.ProcessingMode.PreserveAppearance, UuDok.Core.AiMode.None, AiChoice.none, false), JobService.Json), null, null));

        using var f = new WebApplicationFactory<Program>();
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost:5317") });
        string? status = null;
        for (int i = 0; i < 300 && status != "done"; i++)
        {
            status = (await c.GetFromJsonAsync<JsonElement>($"/api/jobs/{id}")).GetProperty("job").GetProperty("status").GetString();
            await Task.Delay(200);
        }
        Assert.Equal("done", status);
    }
}

[CollectionDefinition("web", DisableParallelization = true)]
public sealed class WebCollection;

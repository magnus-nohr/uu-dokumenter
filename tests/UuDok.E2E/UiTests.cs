using System.Diagnostics;
using System.Net.Http;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace UuDok.E2E;

/// <summary>
/// Starter den bygde webappen på en egen port med midlertidig datamappe (uten Office-hjelper) og
/// tester grensesnittet i Microsoft Edge (installert nettleser, kanal «msedge») med axe-core.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    public const int Port = 5399;
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public string Root { get; } = FindRoot();
    private Process? _server;
    private IPlaywright? _pw;
    public IBrowser Browser { get; private set; } = null!;
    private readonly string _data = Path.Combine(Path.GetTempPath(), "uudok-e2e-" + Guid.NewGuid().ToString("N"));

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "UuDok.slnx"))) return d.FullName;
        throw new InvalidOperationException("Fant ikke repo-roten.");
    }

    public async ValueTask InitializeAsync()
    {
        var web = Path.Combine(Root, "src", "UuDok.Web");
        var dll = Path.Combine(web, "bin", "Debug", "net10.0", "UuDok.Web.dll");
        var psi = new ProcessStartInfo("dotnet", [dll, $"--UuDok:Port={Port}", $"--UuDok:DataDir={_data}", "--UuDok:Helper=ingen.exe", "--UuDok:InMemorySecrets=true"])
        { WorkingDirectory = web, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        _server = Process.Start(psi)!;
        using var http = new HttpClient();
        for (int i = 0; i < 100; i++)
        {
            try { if ((await http.GetAsync(BaseUrl + "/api/session")).IsSuccessStatusCode) break; } catch { }
            await Task.Delay(200);
        }
        _pw = await Playwright.CreateAsync();
        Browser = await _pw.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.CloseAsync();
        _pw?.Dispose();
        try { _server?.Kill(true); } catch { }
        try { Directory.Delete(_data, true); } catch { }
    }
}

[CollectionDefinition("ui")] public sealed class UiCollection : ICollectionFixture<AppFixture>;

[Collection("ui")]
public sealed class UiTests(AppFixture app)
{
    private static readonly AxeRunOptions Wcag = new()
    {
        RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
    };

    private async Task<IPage> Open(string hash = "", int width = 1280, int height = 900)
    {
        var ctx = await app.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height }, Locale = "nb-NO", AcceptDownloads = true });
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(app.BaseUrl + "/" + hash);
        await page.Locator("h1").First.WaitForAsync();
        return page;
    }

    private static async Task NoViolations(IPage page, string where)
    {
        var res = await page.RunAxe(Wcag);
        Assert.True(res.Violations.Length == 0,
            $"{where}: " + string.Join("; ", res.Violations.Select(v => $"{v.Id} ({v.Impact}): {v.Help} – {string.Join(", ", v.Nodes.Take(3).Select(n => n.Html))}")));
    }

    [Fact]
    public async Task Startsiden_har_ingen_axe_brudd_og_riktig_sprak()
    {
        var page = await Open();
        Assert.Equal("nb", await page.GetAttributeAsync("html", "lang"));
        await NoViolations(page, "Startside");
    }

    [Fact]
    public async Task Tastatur_hopplenke_og_feilforklaring_uten_filer()
    {
        var page = await Open();
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("Hopp til hovedinnhold", (await page.EvaluateAsync<string>("document.activeElement.textContent")).Trim());
        await page.GetByRole(AriaRole.Button, new() { Name = "Forbedre filene" }).PressAsync("Enter");
        var alert = page.GetByRole(AriaRole.Alert);
        await alert.WaitForAsync();
        Assert.Contains("Velg minst én", await alert.InnerTextAsync());
        Assert.Equal("filer", await page.EvaluateAsync<string>("document.activeElement.id"));   // fokus til filvelgeren
        await NoViolations(page, "Startside med feil");
    }

    [Fact]
    public async Task Synlig_fokus_paa_knapper()
    {
        var page = await Open();
        await page.GetByRole(AriaRole.Radio, new() { Name = "Tillat synlige forbedringer" }).FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        var outline = await page.EvaluateAsync<string>("getComputedStyle(document.activeElement).outlineStyle");
        Assert.NotEqual("none", outline);
    }

    [Fact]
    public async Task Hele_flyten_med_filvelger_fremdrift_resultat_og_gjennomgang()
    {
        var page = await Open();
        await page.Locator("#filer").SetInputFilesAsync(Path.Combine(app.Root, "testkorpus", "office-presentasjon.pptx"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Forbedre filene" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Behandling" }).WaitForAsync();
        await page.GetByRole(AriaRole.Link, new() { NameRegex = new System.Text.RegularExpressions.Regex("Last ned forbedret fil") }).WaitForAsync(new() { Timeout = 120_000 });
        Assert.Contains("Ferdig", await page.Locator(".badge").First.InnerTextAsync());
        // Fremdriften kunngjøres i en høflig levende region (ikke hver prosent).
        Assert.Equal("polite", await page.Locator("[role=status]").First.GetAttributeAsync("aria-live"));
        await NoViolations(page, "Resultat");

        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Link, new() { NameRegex = new System.Text.RegularExpressions.Regex("Last ned forbedret fil") }).ClickAsync());
        Assert.EndsWith("-forbedret.pptx", download.SuggestedFilename);

        await page.GetByRole(AriaRole.Link, new() { NameRegex = new System.Text.RegularExpressions.Regex("Gjennomgå|Se og juster") }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Gjennomgå forslag", Level = 1 }).WaitForAsync();
        await NoViolations(page, "Gjennomgang");
        // Knappene har unike tilgjengelige navn med kontekst.
        var names = await page.GetByRole(AriaRole.Button).AllInnerTextsAsync();
        var keep = names.Where(n => n.StartsWith("Behold original")).ToList();
        Assert.Equal(keep.Count, keep.Distinct().Count());
    }

    [Theory]
    [InlineData("#/innstillinger", "Innstillinger")]
    [InlineData("#/hjelp", "Hjelp")]
    [InlineData("#/jobber", "Mine jobber")]
    public async Task Andre_sider_har_ingen_axe_brudd(string hash, string heading)
    {
        var page = await Open(hash);
        await page.GetByRole(AriaRole.Heading, new() { Name = heading, Level = 1 }).WaitForAsync();
        await NoViolations(page, heading);
    }

    [Fact]
    public async Task Smal_skjerm_og_zoom_gir_ingen_horisontal_rulling()
    {
        // 320 CSS-piksler tilsvarer 400 % zoom på 1280 (WCAG 1.4.10).
        var page = await Open("", 320, 640);
        var overflow = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.False(overflow);
        await NoViolations(page, "Smal skjerm");
    }
}

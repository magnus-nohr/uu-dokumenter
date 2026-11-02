using Microsoft.AspNetCore.Server.Kestrel.Core;
using UuDok.Engine;
using UuDok.Rendering;
using UuDok.Web;
using UuDok.Web.Jobs;
using UuDok.Web.Security;

Console.OutputEncoding = System.Text.Encoding.UTF8;
// Installert app: innholdsroten er programmappen (publish\), uavhengig av hvor den startes fra.
var installedRoot = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")) ? AppContext.BaseDirectory : null;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = "wwwroot", ContentRootPath = installedRoot });

var port = builder.Configuration.GetValue("UuDok:Port", 5317);
var dataDir = builder.Configuration["UuDok:DataDir"] is { Length: > 0 } d ? d
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UuDok");
Directory.CreateDirectory(Path.Combine(dataDir, "jobs"));
var helper = builder.Configuration["UuDok:Helper"] is { Length: > 0 } h ? h : Path.Combine(AppContext.BaseDirectory, "helper", "UuDok.WinHelper.exe");

// Bare loopback: tjenesten er aldri tilgjengelig fra nettverket.
builder.WebHost.ConfigureKestrel(k =>
{
    k.Listen(System.Net.IPAddress.Loopback, port);
    k.Limits.MaxRequestBodySize = Api.MaxUpload * 2 + 1_000_000;
    k.AddServerHeader = false;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = Api.MaxUpload * 2 + 1_000_000);

// Ingen logging av dokumentinnhold: bare advarsler og feiltyper.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

builder.Services.AddSingleton(new Paths(dataDir));
builder.Services.AddSingleton(new JobStore(Path.Combine(dataDir, "jobs.db")));
builder.Services.AddSingleton(new SettingsService(Path.Combine(dataDir, "settings.json")));
builder.Services.AddSingleton<ISecretStore>(builder.Configuration.GetValue("UuDok:InMemorySecrets", false) ? new InMemorySecretStore() : new WindowsCredentialStore());
builder.Services.AddSingleton<Func<IRenderer>>(_ => () => File.Exists(helper) ? new OfficeRenderer(helper, TimeSpan.FromMinutes(10)) : new NoRenderer());
builder.Services.AddSingleton<Func<IOcrService>>(_ => () => File.Exists(helper) ? new WindowsOcr(helper, TimeSpan.FromMinutes(15)) : new NoOcr());
builder.Services.AddSingleton(sp => new ChatGptAuth(sp.GetRequiredService<SettingsService>(), sp.GetRequiredService<ISecretStore>(),
    sp.GetRequiredService<ILogger<ChatGptAuth>>(), diagnosePath: Path.Combine(dataDir, "chatgpt-diagnose.log")));
builder.Services.AddSingleton<JobService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobService>());
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();
app.UseMiddleware<LocalSecurityMiddleware>(LocalSecurityOptions.ForPort(port));
app.UseDefaultFiles();
app.UseStaticFiles();
Api.Map(app);
app.MapFallback(async ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 404; return; }
    var index = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (File.Exists(index)) { ctx.Response.ContentType = "text/html; charset=utf-8"; await ctx.Response.SendFileAsync(index); }
    else
    {
        ctx.Response.StatusCode = 503;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync("Grensesnittet mangler. Stopp appen med stopp.bat, kjør installer.bat og start på nytt med start.bat.");
    }
});

app.Lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine($"UU-dokumenter kjører på http://127.0.0.1:{port}  (data: {dataDir})"));
app.Run();

public partial class Program;

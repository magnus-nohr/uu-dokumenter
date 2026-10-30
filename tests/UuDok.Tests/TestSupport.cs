using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using UuDok.Core;
using UuDok.Core.Opc;
using UuDok.Engine;
using UuDok.Rendering;

namespace UuDok.Tests;

public static class Corpus
{
    public static string Root { get; } = FindRoot();
    public static string Dir => Path.Combine(Root, "testkorpus");
    public static string File(string name) => Path.Combine(Dir, name);
    public static byte[] Bytes(string name) => System.IO.File.ReadAllBytes(File(name));
    public static OpcPackage Package(string name) => OpcPackage.Load(new MemoryStream(Bytes(name)));

    private static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (System.IO.File.Exists(Path.Combine(d.FullName, "UuDok.slnx"))) return d.FullName;
        throw new InvalidOperationException("Fant ikke repo-roten.");
    }

    public static string Helper => Path.Combine(Root, "src", "UuDok.WinHelper", "bin", "Debug", "net10.0-windows10.0.19041.0", "UuDok.WinHelper.exe");

    public static bool OfficeAvailable =>
        OperatingSystem.IsWindows() && System.IO.File.Exists(Helper) && Type.GetTypeFromProgID("PowerPoint.Application") != null && Type.GetTypeFromProgID("Word.Application") != null
        && Environment.GetEnvironmentVariable("UUDOK_SKIP_OFFICE") != "1";

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "uudok-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Bygger en zip fra (navn, innhold)-par. Brukes til ondsinnede kanttilfeller.</summary>
    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (n, c) in entries)
            {
                using var s = z.CreateEntry(n).Open();
                s.Write(Encoding.UTF8.GetBytes(c));
            }
        return ms.ToArray();
    }

    public static Pipeline LocalPipeline(IRenderer? renderer = null) => new(renderer ?? new NoRenderer(), new NoOcr(), () => null);
}

/// <summary>Registrerer alle utgående HTTP-forespørsler i prosessen (DiagnosticListener fra System.Net.Http).</summary>
public sealed class NetworkSpy : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly IDisposable _sub;
    private readonly List<IDisposable> _inner = [];
    public List<Uri> Requests { get; } = [];

    public NetworkSpy() { _sub = DiagnosticListener.AllListeners.Subscribe(this); }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == "HttpHandlerDiagnosticListener") lock (_inner) _inner.Add(value.Subscribe(this));
    }

    public void OnNext(KeyValuePair<string, object?> kv)
    {
        if (!kv.Key.EndsWith(".Start") && kv.Key != "System.Net.Http.Request") return;
        var req = kv.Value?.GetType().GetProperty("Request")?.GetValue(kv.Value) as HttpRequestMessage;
        if (req?.RequestUri != null) lock (Requests) Requests.Add(req.RequestUri);
    }

    public void OnError(Exception error) { }
    public void OnCompleted() { }
    public void Dispose() { _sub.Dispose(); lock (_inner) foreach (var i in _inner) i.Dispose(); }
}

/// <summary>Testhåndterer for HttpClient: svarer uten nettverk og husker forespørsler.</summary>
public sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Seen { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Seen.Add(request);
        return Task.FromResult(respond(request));
    }
}

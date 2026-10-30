using System.Text.Json;
using UuDok.WinHelper;

// Avgrenset hjelpeprosess. Kalles av backend med tidsgrense; skriver JSON til --json-filen.
// Den åpner bare filer i jobbmappen, skrivebeskyttet, med makroer tvunget av.
var a = Args.Parse(args);
var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
try
{
    object result = a.Command switch
    {
        "render-pptx" => OfficeRender.RenderPptx(a.Req("in"), a.Req("out"), a.Int("width", 1280)),
        "render-docx" => OfficeRender.RenderDocx(a.Req("in"), a.Req("out"), a.Int("dpi", 96)),
        "ocr" => await WinOcr.Recognize(a.Req("in"), a.Opt("lang")),
        "ocr-batch" => await WinOcr.Batch(a.Req("manifest"), a.Opt("lang")),
        "office-info" => OfficeRender.Info(),
        "create-corpus" => OfficeCorpus.Create(a.Req("out")),
        "create-big" => OfficeCorpus.CreateBig(a.Req("in"), a.Req("out"), a.Int("n", 400)),
        _ => throw new ArgumentException($"Ukjent kommando: {a.Command}")
    };
    var text = JsonSerializer.Serialize(result, json);
    if (a.Opt("json") is { } jp) File.WriteAllText(jp, text); else Console.WriteLine(text);
    return 0;
}
catch (Exception ex)
{
    var err = JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message }, json);
    if (a.Opt("json") is { } jp) File.WriteAllText(jp, err); else Console.Error.WriteLine(err);
    return 2;
}

namespace UuDok.WinHelper
{
    sealed class Args
    {
        public string Command = "";
        public Dictionary<string, string> Opts = new();
        public static Args Parse(string[] args)
        {
            var a = new Args { Command = args.FirstOrDefault() ?? "" };
            for (int i = 1; i < args.Length - 1; i++)
                if (args[i].StartsWith("--")) { a.Opts[args[i][2..]] = args[i + 1]; i++; }
            return a;
        }
        public string Req(string k) => Opts.TryGetValue(k, out var v) ? v : throw new ArgumentException($"Mangler --{k}");
        public string? Opt(string k) => Opts.GetValueOrDefault(k);
        public int Int(string k, int d) => Opts.TryGetValue(k, out var v) ? int.Parse(v) : d;
    }
}

using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using UuDok.Core;
using UuDok.Core.Opc;

namespace UuDok.Intake;

public sealed class IntakeResult
{
    public bool Accepted => RejectCode == null;
    public string? RejectCode { get; init; }
    public string? RejectMessage { get; init; }
    public DocumentKind Kind { get; init; }
    public OpcPackage? Package { get; init; }
    public byte[]? Bytes { get; init; }
    public bool IsSigned { get; init; }
    public bool IsProtected { get; init; }
    public List<string> ExternalTargets { get; init; } = [];
    public List<string> Notes { get; init; } = [];

    public static IntakeResult Reject(string code, string message) => new() { RejectCode = code, RejectMessage = message };
}

/// <summary>
/// Kontrollerer filinnhold (ikke bare filendelse) før noe annet skjer. Avviser gamle binærformater,
/// krypterte filer, makroformater, maler/fremvisningsfiler, ZIP-angrep og XML med DTD.
/// Signerte og beskyttede filer godtas for analyse, men flagges slik at reparasjon håndteres eksplisitt.
/// </summary>
public static class FileInspector
{
    private const string CtDocx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string CtPptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";

    private static readonly Dictionary<string, string> KnownUnsupported = new()
    {
        ["application/vnd.ms-word.document.macroEnabled.main+xml"] = "Filen er et Word-dokument med makroer (.docm). Makroformater støttes ikke. Lagre som .docx uten makroer og prøv igjen.",
        ["application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml"] = "Filen er en presentasjon med makroer (.pptm). Makroformater støttes ikke. Lagre som .pptx uten makroer og prøv igjen.",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml"] = "Filen er en Word-mal (.dotx). Bare vanlige dokumenter (.docx) støttes.",
        ["application/vnd.ms-word.template.macroEnabledTemplate.main+xml"] = "Filen er en Word-mal med makroer (.dotm). Støttes ikke.",
        ["application/vnd.openxmlformats-officedocument.presentationml.template.main+xml"] = "Filen er en PowerPoint-mal (.potx). Bare vanlige presentasjoner (.pptx) støttes.",
        ["application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml"] = "Filen er en PowerPoint-fremvisning (.ppsx). Lagre den som .pptx og prøv igjen.",
        ["application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml"] = "Filen er en fremvisning med makroer (.ppsm). Støttes ikke.",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"] = "Filen er et Excel-regneark. Bare Word (.docx) og PowerPoint (.pptx) støttes.",
    };

    public static IntakeResult Inspect(byte[] data, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;
        if (data.Length == 0) return IntakeResult.Reject("empty", "Filen er tom.");
        if (data.Length > limits.MaxInputBytes) return IntakeResult.Reject("too_large", $"Filen er større enn grensen på {limits.MaxInputBytes / (1024 * 1024)} MB.");

        if (data.Length >= 8 && data[0] == 0xD0 && data[1] == 0xCF && data[2] == 0x11 && data[3] == 0xE0)
            return IntakeResult.Reject("cfb",
                "Filen er enten passordbeskyttet (kryptert) eller i et gammelt binærformat (.doc/.ppt). " +
                "Fjern passordet i Office, eller lagre som .docx/.pptx, og prøv igjen. Appen omgår aldri passord.");
        if (data.Length < 4 || data[0] != 'P' || data[1] != 'K')
            return IntakeResult.Reject("not_ooxml", "Filen er ikke et Word- eller PowerPoint-dokument (innholdet er ikke en Office-pakke), uansett filendelse.");

        OpcPackage pkg;
        try { pkg = OpcPackage.Load(new MemoryStream(data, writable: false), limits); }
        catch (PackageRejectedException ex) { return IntakeResult.Reject(ex.Code, ex.Message); }

        var ctEntry = pkg.Get("/[Content_Types].xml");
        if (ctEntry == null) return IntakeResult.Reject("not_ooxml", "Filen mangler innholdstyper og er ikke en gyldig Office-pakke.");

        // All XML i pakken lastes med DTD forbudt (XXE) før vi går videre.
        foreach (var e in pkg.Entries.Where(e => !e.IsDirectory && (e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))))
        {
            try { SafeXml.Load(e.Data); }
            catch (PackageRejectedException ex) { return IntakeResult.Reject(ex.Code, ex.Message); }
        }

        var ct = SafeXml.Load(ctEntry.Data);
        var overrides = ct.Root!.Elements(Ns.Ct + "Override")
            .ToDictionary(o => ((string)o.Attribute("PartName")!).ToLowerInvariant(), o => (string)o.Attribute("ContentType")!);
        var rels = pkg.Get("/_rels/.rels") is { } re ? SafeXml.Load(re.Data) : null;
        var officeDoc = rels?.Root!.Elements(Ns.Rel + "Relationship")
            .FirstOrDefault(r => ((string?)r.Attribute("Type"))?.EndsWith("/officeDocument") == true);
        if (officeDoc == null) return IntakeResult.Reject("not_ooxml", "Filen mangler hoveddokument.");
        var mainUri = "/" + ((string)officeDoc.Attribute("Target")!).TrimStart('/');
        var mainCt = overrides.GetValueOrDefault(mainUri.ToLowerInvariant());
        if (mainCt == null) return IntakeResult.Reject("not_ooxml", "Hoveddokumentet har ingen innholdstype.");
        if (KnownUnsupported.TryGetValue(mainCt, out var msg)) return IntakeResult.Reject("unsupported_variant", msg);
        DocumentKind kind = mainCt switch
        {
            CtDocx => DocumentKind.Docx,
            CtPptx => DocumentKind.Pptx,
            _ => (DocumentKind)(-1)
        };
        if ((int)kind == -1) return IntakeResult.Reject("unsupported_variant", "Filtypen støttes ikke. Bare Word (.docx) og PowerPoint (.pptx) støttes.");

        if (pkg.Entries.Any(e => e.Name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)) ||
            overrides.Values.Any(v => v.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)))
            return IntakeResult.Reject("macro", "Filen inneholder makrokode. Makroer kjøres aldri, og filer med makroer behandles ikke. Fjern makroene og lagre som .docx/.pptx.");

        bool signed = rels!.Root!.Elements(Ns.Rel + "Relationship").Any(r => (string?)r.Attribute("Type") == Ns.RelSignatureOrigin)
                      || pkg.Entries.Any(e => e.Name.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase));

        // Eksterne relasjoner registreres, men hentes aldri.
        var external = new List<string>();
        foreach (var e in pkg.Entries.Where(e => e.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var x = SafeXml.Load(e.Data);
            foreach (var r in x.Root?.Elements(Ns.Rel + "Relationship") ?? [])
                if ((string?)r.Attribute("TargetMode") == "External" && ((string?)r.Attribute("Type"))?.EndsWith("/hyperlink") != true)
                    external.Add($"{e.Name}: {((string?)r.Attribute("Type"))?.Split('/').Last()}");
        }

        bool isProtected = false;
        var notes = new List<string>();
        if (kind == DocumentKind.Docx && pkg.Get("/word/settings.xml") is { } st)
        {
            var s = SafeXml.Load(st.Data);
            var dp = s.Root?.Element(Ns.W + "documentProtection");
            if (dp != null && ((string?)dp.Attribute(Ns.W + "enforcement")) is "1" or "true" or "on") isProtected = true;
            if (s.Root?.Element(Ns.W + "writeProtection") != null) isProtected = true;
        }
        if (kind == DocumentKind.Pptx && pkg.Get("/ppt/presentation.xml") is { } pr)
        {
            var p = SafeXml.Load(pr.Data);
            if (p.Root?.Element(Ns.P + "modifyVerifier") != null) isProtected = true;
        }
        if (kind == DocumentKind.Pptx && pkg.Get("/ppt/presProps.xml") is { } pp &&
            SafeXml.Load(pp.Data).Descendants().Any(e => e.Name.LocalName == "modifyVerifier")) isProtected = true;

        // Endelig kontroll: Open XML SDK må kunne åpne pakken.
        try
        {
            using var ms = new MemoryStream(data, writable: false);
            var settings = new OpenSettings { AutoSave = false, MarkupCompatibilityProcessSettings = new MarkupCompatibilityProcessSettings(MarkupCompatibilityProcessMode.NoProcess, FileFormatVersions.Microsoft365) };
            using OpenXmlPackage doc = kind == DocumentKind.Docx ? WordprocessingDocument.Open(ms, false, settings) : PresentationDocument.Open(ms, false, settings);
            _ = doc.Parts.Count();
        }
        catch (Exception)
        {
            return IntakeResult.Reject("corrupt", "Filen er skadet eller ufullstendig og kan ikke behandles. Prøv å åpne og lagre den på nytt i Office.");
        }

        if (signed) notes.Add("Dokumentet er digitalt signert. En endring vil gjøre signaturen ugyldig.");
        if (isProtected) notes.Add("Dokumentet er beskyttet mot redigering. Appen analyserer det, men endrer det ikke.");
        if (external.Count > 0) notes.Add($"Dokumentet har {external.Count} eksterne koblinger. De er ikke hentet.");

        return new IntakeResult { Kind = kind, Package = pkg, Bytes = data, IsSigned = signed, IsProtected = isProtected, ExternalTargets = external, Notes = notes };
    }
}

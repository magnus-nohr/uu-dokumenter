using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UuDok.Core;

namespace UuDok.Patching;

public sealed record ValidationIssue(string Part, string Path, string Id, string Description)
{
    public string Key => $"{Part}|{Path}|{Id}";
}

/// <summary>Open XML SDK-validering. Brukes til å sikre at en endring ikke innfører nye valideringsfeil.</summary>
public static class OoxmlValidation
{
    public static OpenSettings ReadOnlySettings => new()
    {
        AutoSave = false,
        MarkupCompatibilityProcessSettings = new MarkupCompatibilityProcessSettings(MarkupCompatibilityProcessMode.NoProcess, FileFormatVersions.Microsoft365),
    };

    public static List<ValidationIssue> Validate(byte[] package, DocumentKind kind, int max = 2000)
    {
        using var ms = new MemoryStream(package, writable: false);
        OpenXmlPackage doc = kind == DocumentKind.Docx
            ? WordprocessingDocument.Open(ms, false, ReadOnlySettings)
            : PresentationDocument.Open(ms, false, ReadOnlySettings);
        using (doc)
        {
            var v = new OpenXmlValidator(FileFormatVersions.Microsoft365) { MaxNumberOfErrors = max };
            return v.Validate(doc)
                .Select(e => new ValidationIssue(e.Part?.Uri.OriginalString ?? "", e.Path?.XPath ?? "", e.Id ?? "", e.Description ?? ""))
                .ToList();
        }
    }

    /// <summary>Feil i <paramref name="after"/> som ikke fantes i <paramref name="before"/>.</summary>
    public static List<ValidationIssue> NewIssues(List<ValidationIssue> before, List<ValidationIssue> after)
    {
        // XPath kan forskyves når et element settes inn (f.eks. ny tittel først i spTree);
        // sammenlign derfor på del + feil-ID + beskrivelse med antall.
        var counts = before.GroupBy(i => (i.Part, i.Id, i.Description)).ToDictionary(g => g.Key, g => g.Count());
        var res = new List<ValidationIssue>();
        foreach (var g in after.GroupBy(i => (i.Part, i.Id, i.Description)))
        {
            var had = counts.GetValueOrDefault(g.Key);
            if (g.Count() > had) res.AddRange(g.Skip(had));
        }
        return res;
    }
}

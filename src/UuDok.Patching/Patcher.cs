using System.Xml.Linq;
using UuDok.Core;
using UuDok.Core.Opc;

namespace UuDok.Patching;

public sealed class PartReport
{
    public required string PartUri { get; init; }
    public List<string> Diffs { get; } = [];
    public bool Created { get; init; }
}

public sealed class PatchResult
{
    public required byte[] Output { get; init; }
    public List<PartReport> ChangedParts { get; } = [];
    public int UntouchedParts { get; set; }
    public bool UntouchedVerified { get; set; }
    public List<string> Problems { get; } = [];
}

/// <summary>
/// Kopi-ved-skriving av originalpakken. Endringer gjøres én og én på XML-treet; etter hver endring
/// sammenlignes treet semantisk med tilstanden før, og alt utenfor endringens tillatelse ruller den tilbake.
/// </summary>
public sealed class Patcher
{
    public PatchResult Apply(OpcPackage pkg, IReadOnlyList<PlannedChange> changes, PatchEnv env)
    {
        var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var additions = new List<(string, byte[])>();
        var reports = new List<PartReport>();

        // Kjerneegenskaper som mangler må opprettes som ny del med relasjon og innholdstype.
        var coreChanges = changes.Where(c => c.Kind == ChangeKind.SetDocumentTitle && c.Status == ChangeStatus.Planned).ToList();
        if (coreChanges.Count > 0 && !pkg.Contains(coreChanges[0].Target.PartUri))
            CreateCorePart(pkg, coreChanges, replacements, additions, reports);

        foreach (var group in changes.Where(c => c.Status == ChangeStatus.Planned).GroupBy(c => c.Target.PartUri, StringComparer.OrdinalIgnoreCase))
        {
            var entry = pkg.Get(group.Key);
            if (entry == null)
            {
                foreach (var c in group) Discard(c, "Pakkedelen finnes ikke.");
                continue;
            }
            var original = SafeXml.Load(entry.Data);
            var work = new XDocument(original);
            var total = new Allowance();
            var succeeded = new List<PlannedChange>();
            // Tillatelser peker på elementer i «work». Ved forkastet endring bygges «work» på nytt fra
            // originalen med de vellykkede endringene, slik at referansene alltid gjelder gjeldende tre.
            void Rebuild()
            {
                work = new XDocument(original);
                total = new Allowance();
                foreach (var s in succeeded) total.Merge(PatchOps.Apply(s, work, original, env));
            }
            foreach (var c in group)
            {
                var before = new XDocument(work);
                try
                {
                    var al = PatchOps.Apply(c, work, original, env);
                    var diffs = SemanticDiff.Compare(before, work);
                    var bad = diffs.Where(d => !al.Covers(d)).ToList();
                    if (diffs.Count == 0) { Skip(c, "Ingen endring var nødvendig."); Rebuild(); continue; }
                    if (bad.Count > 0)
                    {
                        Discard(c, "Endringen ville berørt mer enn tillatt: " + string.Join("; ", bad.Take(3)));
                        Rebuild();
                        continue;
                    }
                    total.Merge(al);
                    c.Status = ChangeStatus.Applied;
                    succeeded.Add(c);
                }
                catch (PreconditionFailedException ex) { Discard(c, ex.Message); Rebuild(); }
                catch (NotSupportedException ex) { Discard(c, ex.Message); Rebuild(); }
            }
            if (succeeded.Count == 0) continue;

            // Kontroll av hele delen mot original: bare tillatte forskjeller.
            var all = SemanticDiff.Compare(original, work);
            var stray = all.Where(d => !total.Covers(d)).ToList();
            if (stray.Count > 0)
            {
                foreach (var c in group.Where(c => c.Status == ChangeStatus.Applied))
                    { c.Status = ChangeStatus.RolledBack; c.StatusNote = "Delen fikk uventede forskjeller og ble rullet tilbake."; }
                continue;
            }
            byte[] bytes;
            try { bytes = SafeXml.Save(work, entry.Data); }
            catch (NotSupportedException ex)
            {
                foreach (var c in group.Where(c => c.Status == ChangeStatus.Applied)) Discard(c, ex.Message);
                continue;
            }
            // Rundtur: serialisert resultat skal gi samme semantiske tre.
            if (SemanticDiff.Compare(work, SafeXml.Load(bytes)).Count != 0)
            {
                foreach (var c in group.Where(c => c.Status == ChangeStatus.Applied)) { c.Status = ChangeStatus.RolledBack; c.StatusNote = "Serialisering ga avvik."; }
                continue;
            }
            replacements[entry.PartUri] = bytes;
            var rep = new PartReport { PartUri = entry.PartUri };
            rep.Diffs.AddRange(all.Select(d => d.ToString()));
            reports.Add(rep);
        }

        using var ms = new MemoryStream();
        pkg.WriteTo(ms, replacements, additions);
        var output = ms.ToArray();

        var result = new PatchResult { Output = output };
        result.ChangedParts.AddRange(reports);
        VerifyUntouched(pkg, output, replacements.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), additions.Select(a => "/" + a.Item1).ToHashSet(), result);
        return result;
    }

    private static void VerifyUntouched(OpcPackage orig, byte[] output, HashSet<string> replaced, HashSet<string> added, PatchResult result)
    {
        var after = OpcPackage.Load(new MemoryStream(output));
        int same = 0; bool ok = true;
        foreach (var e in orig.Entries)
        {
            var a = after.Get(e.PartUri);
            if (a == null) { ok = false; result.Problems.Add($"Del mangler etter skriving: {e.PartUri}"); continue; }
            if (replaced.Contains(e.PartUri)) continue;
            if (!a.Data.AsSpan().SequenceEqual(e.Data)) { ok = false; result.Problems.Add($"Urørt del endret: {e.PartUri}"); }
            else same++;
        }
        foreach (var e in after.Entries)
            if (!orig.Contains(e.PartUri) && !added.Contains(e.PartUri)) { ok = false; result.Problems.Add($"Ukjent ny del: {e.PartUri}"); }
        result.UntouchedParts = same;
        result.UntouchedVerified = ok;
    }

    private static void CreateCorePart(OpcPackage pkg, List<PlannedChange> coreChanges, Dictionary<string, byte[]> replacements,
        List<(string, byte[])> additions, List<PartReport> reports)
    {
        var c = coreChanges[0];
        var name = c.Target.PartUri.TrimStart('/');
        try
        {
            var core = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Ns.Cp + "coreProperties",
                    new XAttribute(XNamespace.Xmlns + "cp", Ns.Cp.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "dc", Ns.Dc.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "dcterms", Ns.DcTerms.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "xsi", Ns.Xsi.NamespaceName),
                    new XElement(Ns.Dc + "title", c.Value)));
            var coreBytes = System.Text.Encoding.UTF8.GetBytes(core.Declaration + "\r\n" + core.Root!.ToString(SaveOptions.DisableFormatting));

            var relsEntry = pkg.Get("/_rels/.rels") ?? throw new PreconditionFailedException("Pakken mangler /_rels/.rels.");
            var rels = SafeXml.Load(relsEntry.Data);
            var relsOrig = new XDocument(rels);
            var ids = rels.Root!.Elements(Ns.Rel + "Relationship").Select(r => (string?)r.Attribute("Id")).ToHashSet();
            var rid = Enumerable.Range(1, 1000).Select(i => $"rIdUu{i}").First(x => !ids.Contains(x));
            var rel = new XElement(Ns.Rel + "Relationship", new XAttribute("Id", rid), new XAttribute("Type", Ns.RelCoreProps), new XAttribute("Target", name));
            rels.Root.Add(rel);

            var ctEntry = pkg.Get("/[Content_Types].xml") ?? throw new PreconditionFailedException("Pakken mangler [Content_Types].xml.");
            var ct = SafeXml.Load(ctEntry.Data);
            var ctOrig = new XDocument(ct);
            var ov = new XElement(Ns.Ct + "Override", new XAttribute("PartName", "/" + name),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml"));
            ct.Root!.Add(ov);

            var a1 = new Allowance(); a1.AddedElements.Add(rel);
            var a2 = new Allowance(); a2.AddedElements.Add(ov);
            if (SemanticDiff.Compare(relsOrig, rels).Any(d => !a1.Covers(d)) || SemanticDiff.Compare(ctOrig, ct).Any(d => !a2.Covers(d)))
                throw new PreconditionFailedException("Uventet avvik ved oppretting av dokumentegenskaper.");

            replacements[relsEntry.PartUri] = SafeXml.Save(rels, relsEntry.Data);
            replacements[ctEntry.PartUri] = SafeXml.Save(ct, ctEntry.Data);
            additions.Add((name, coreBytes));
            reports.Add(new PartReport { PartUri = "/" + name, Created = true });
            var r1 = new PartReport { PartUri = relsEntry.PartUri }; r1.Diffs.Add("ElementAdded Relationship (core-properties)"); reports.Add(r1);
            var r2 = new PartReport { PartUri = ctEntry.PartUri }; r2.Diffs.Add("ElementAdded Override (core-properties)"); reports.Add(r2);
            foreach (var cc in coreChanges) { cc.Status = ChangeStatus.Applied; }
        }
        catch (PreconditionFailedException ex)
        {
            foreach (var cc in coreChanges) Discard(cc, ex.Message);
        }
    }

    private static void Discard(PlannedChange c, string why) { c.Status = ChangeStatus.Discarded; c.StatusNote = why; }
    private static void Skip(PlannedChange c, string why) { c.Status = ChangeStatus.Skipped; c.StatusNote = why; }
}

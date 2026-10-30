using System.Text;
using UuDok.Core.Rules;

namespace UuDok.Tools;

/// <summary>Genererer docs/REGELREGISTER.md fra det innebygde regelregisteret (én kilde til sannhet).</summary>
public static class RulesDoc
{
    public static void Write(string path)
    {
        var r = RuleRegistry.Default;
        var sb = new StringBuilder();
        sb.AppendLine("# Regelregister");
        sb.AppendLine();
        sb.AppendLine($"Generert fra `src/UuDok.Core/Rules/regelregister.json` (versjon **{r.Version}**, kilder kontrollert **{r.SourcesChecked}**). Ikke rediger for hånd: `UuDok.Tools regler docs/REGELREGISTER.md`.");
        sb.AppendLine();
        sb.AppendLine("> " + r.Disclaimer);
        sb.AppendLine();
        sb.AppendLine("Resultatverdier: **bestått**, **ikke bestått**, **krever vurdering**, **ikke relevant**, **ikke testet**. Manuelle og heuristiske regler kan aldri gi «bestått».");
        sb.AppendLine();
        sb.AppendLine("## Profiler");
        sb.AppendLine();
        sb.AppendLine("| ID | Navn | Normativ | Grunnlag | Kilder |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var p in r.Profiles) sb.AppendLine($"| `{p.Id}` | {p.Name} | {(p.Normative ? "ja" : "nei")} | {p.Basis}{(p.Note != null ? " " + p.Note : "")} | {string.Join(", ", p.Sources)} |");
        sb.AppendLine();
        sb.AppendLine("## Regler");
        foreach (var x in r.Rules)
        {
            sb.AppendLine();
            sb.AppendLine($"### {x.Id} – {x.Title}");
            sb.AppendLine();
            sb.AppendLine($"- **Type:** {(x.Kind == "requirement" ? "Krav (normativ kilde)" : "Beste praksis / informasjon")}; gjelder {string.Join(" og ", x.AppliesTo.Select(a => a.ToUpperInvariant()))}");
            if (x.Wcag != null) sb.AppendLine($"- **WCAG-kriterium:** {x.Wcag} ({x.Level})");
            sb.AppendLine($"- **Kontroll:** {x.Check switch { "auto" => "automatisk", "heuristic" => "heuristisk (kan ikke gi bestått)", _ => "manuell (ikke testet av appen)" }}");
            sb.AppendLine($"- **Reparasjon:** {x.Repair switch { "auto" => "automatisk når forutsetningene holder", "suggestion" => "forslag (automatisk bare når sikkert)", "visible" => "krever synlig endring (egen variant)", _ => "ingen" }}");
            foreach (var (k, v) in x.Refs) sb.AppendLine($"- **{r.Profiles.First(p => p.Id == k).Name}:** {string.Join("; ", v)}");
            sb.AppendLine($"- **Testmetode:** {x.TestMethod}");
            sb.AppendLine($"- **Begrensninger:** {x.Limitations}");
        }
        sb.AppendLine();
        sb.AppendLine("## Kilder");
        sb.AppendLine();
        foreach (var s in r.Sources) sb.AppendLine($"- **{s.Title}** – <{s.Url}> (kontrollert {s.Checked}). {s.Note}");
        sb.AppendLine();
        sb.AppendLine("## Forfatterverktøy (EN 301 549, kapittel 11.8)");
        sb.AppendLine();
        sb.AppendLine("Appen endrer dokumenter og må derfor vurderes mot kravene til forfatterverktøy: bevaring av tilgjengelighetsinformasjon ved transformasjon (11.8.3), støtte for reparasjon (11.8.4) og tilgjengelig innhold fra eget arbeid (11.8.2). Designet følger dette: originalinformasjon bevares (urørte deler er byte-like, eksisterende alternativ tekst overskrives ikke uten grunn), funn har reparasjonsforslag, og appen lager ikke utilgjengelig innhold selv. Dette er en designvurdering, ikke en dokumentert samsvarsvurdering. Klausulnumre er hentet fra V3.2.1 og må kontrolleres mot V4.1.1-teksten.");
        File.WriteAllText(path, sb.ToString());
    }
}

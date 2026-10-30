# Arkitektur

## Oversikt

```
Nettleser (React/TS, servert av backend)
        │  HTTP på 127.0.0.1 (Host/Origin-kontroll, CSRF, CSP)
        ▼
UuDok.Web (ASP.NET Core, .NET 10)
  ├─ API (Api.cs) og innstillinger (AppSettings, nøkler i Windows Legitimasjonsbehandling)
  ├─ JobService: lokal kø i bakgrunnstjeneste, SQLite (jobs.db), isolerte jobbmapper, opprydding
  └─ UuDok.Engine.Pipeline
        1 Intake ── FileInspector: innhold, ZIP-grenser, XXE, makro/kryptert/signert/beskyttet
        2 Analyse ─ DocxAnalyzer / PptxAnalyzer → felles DocumentModel (lesende)
        3 Bilder ── ImagePrep (beskjæring som vist) → lokal OCR (WinHelper) og QR (ZXing)
        4 Regler ── RuleEngine + versjonert regelregister → Findings og resultat per regel
        5 KI ────── IAltTextProvider (ingen / lokal Ollama / Anthropic) → validert skjema + usikkerhet
        6 Plan ──── Planner: endringer med forutsetning, risiko, opprinnelse, gjennomgangsbehov
        7 Patch ─── Patcher: kopi av originalpakken, målrettede XML-endringer, semantisk diff mot tillatelser
        8 Kontroll  Open XML-validering (ingen nye feil) + Office-rendering før/etter (WinHelper)
        9 Rapport ─ ReportBuilder: HTML (tilgjengelig) og JSON
UuDok.WinHelper (egen prosess, tidsgrense): Word/PowerPoint via COM, Windows.Data.Pdf, Windows.Media.Ocr
```

## Moduler

| Prosjekt | Ansvar |
|---|---|
| `UuDok.Core` | Felles dokumentmodell, funn, endringsplan, lokatorer, sikker XML/OPC, regelregister og regelmotor, farge/kontrast, språkgjenkjenning |
| `UuDok.Intake` | Filinntak og sikkerhetskontroll |
| `UuDok.Docx` | DOCX-analyse |
| `UuDok.Pptx` | PPTX-analyse |
| `UuDok.Patching` | Dokumentpatching, semantisk diff, tillatte endringer, Open XML-validering |
| `UuDok.Ai` | KI-kontrakt (skjema), prompt, validering, usikkerhet, adaptere, bildeforberedelse, QR |
| `UuDok.Rendering` | Grensesnitt for renderingsmotorer, Office-renderer, bildesammenligning og toleranser |
| `UuDok.Engine` | Pipeline, OCR-tjeneste, planlegger (endringsplan og risikovurdering), verifisering |
| `UuDok.Reporting` | Eksport av rapport (HTML/JSON) og oppsummering |
| `UuDok.Web` | Backend, jobbkø, SQLite, sikkerhet, servering av frontend |
| `UuDok.WinHelper` | Windows-spesifikk hjelpeprosess (Office COM, OCR, PDF-rasterisering) |
| `web/` | React + TypeScript + Vite, kontrakter validert med zod |
| `tools/UuDok.Tools` | Testkorpus, kommandolinje, KI-evaluering, generering av regeldokumentasjon |
| `probes/UuDok.Probes` | Tekniske bevis (fase A) |

## Begrunnede valg

- **Ingen ombygging av dokumentet.** `OpcPackage` leser alle deler som dekomprimerte bytes. `Patcher` skriver en ny pakke
  der urørte deler får nøyaktig samme bytes og samme rekkefølge (kontrolleres etter skriving). Endrede deler lastes som
  XML (DTD forbudt), endres målrettet og serialiseres med original deklarasjon og linjeskift i attributter bevart.
- **Open XML SDK** brukes til navigering (deler, relasjoner) og validering. Elementinnhold leses og endres som XML med
  felles **lokatorer** (`nv:{cNvPr-id}`, `docPr:{id}:{forekomst}`, `p:{n}`, `tbl:{n}`, `hl:{n}`, `vml:{n}`), slik at analyse og
  patching peker på samme objekt. Elementer i `mc:Fallback` telles aldri.
- **Hver endring har en forutsetning** (SHA-256-fingeravtrykk av målelementet og forventet gammel verdi). Holder den ikke,
  forkastes endringen. Hver endring har en **tillatelse** (hvilke attributter/elementer den får endre). Etter hver
  endring sammenlignes XML-treet semantisk, og alt utenfor tillatelsen ruller endringen tilbake.
- **Leserekkefølge i PowerPoint** er rekkefølgen i figurtreet, som også er lagrekkefølgen (bevis 3/4). Appen sorterer
  aldri etter koordinater. Bare tittelen flyttes først, og bare når den ikke overlapper noe den passerer.
- **Skjult lysbildetittel** er en ekte tittelplassholder plassert over lysbildet og først i figurtreet (bevis 5).
- **Overskrifter uten synlig endring** bruker `w:outlineLvl` (bevis 6). Ekte overskriftsstiler hører til synlig modus.
- **Rendering** skjer i en egen prosess med tidsgrense, ett Office-program om gangen. Hjelperen avslutter bare Office-
  prosesser den selv har startet (`OfficeSession`), så brukerens åpne dokumenter aldri lukkes. Toleranse: 0,05 %
  avvikende piksler (kanaltoleranse 8), pluss strukturkontroller (antall, posisjoner, tekst, animasjoner, medier,
  overganger, relativ lagrekkefølge for overlappende figurer, sidebryting).
- **Avvik forkaster endringer lokalt**: i PPTX per lysbilde, i DOCX de mest risikable først. Tryggere forbedringer beholdes.
- **KI foreslår bare innhold** etter et lukket JSON-skjema. ID-en i forespørselen er anonym (`obj-n`), filnavn sendes aldri.
  Appen legger til egne usikkerhetssignaler: OCR-samsvar, bildestørrelse, rolle, tilgang til diagramdata, og tall/navn
  som ikke finnes i kildene. Modellens egen vurdering regnes ikke som kalibrert sannsynlighet.
- **Ingen obligatorisk modell.** Skymodell kan endres i innstillingene. Standard er `claude-opus-5-5` med serverbasert
  reserve ved avvisning (samme leverandør). Valg av standardmodell skal gjøres etter evaluering (`docs/EVALUERINGSRUBRIKK.md`).
- **Jobbkø uten Redis:** `Channel` i en `BackgroundService`, status i SQLite, gjenopptakelse ved oppstart.
- **Frontend** bygges inn i `wwwroot`, og backend serverer den. Ruting skjer med `#/…`, fokus flyttes til `h1` ved navigering,
  og status kunngjøres i `aria-live="polite"`-regioner bare ved trinnskifte.

## Dataflyt og lagring

```
%LOCALAPPDATA%\UuDok\
  jobs.db                  status, fremdrift, filnavn, valg (aldri dokumentinnhold eller nøkler)
  settings.json            innstillinger (aldri nøkler)
  jobs\<id>\original.bin   skrivebeskyttet kopi av opplastet fil
  jobs\<id>\work\          img\ (bilder som vist), render\, output\, state.json, rapport.html/json
```

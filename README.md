# UU-dokumenter

Lokal webapp som hjelper lærere, elever og studenter med å forbedre universell utforming av eksisterende
Word- (.docx) og PowerPoint-filer (.pptx). Appen analyserer filen, gjør dokumenterte og kontrollerte forbedringer,
lager kontekstbaserte alternative tekster (valgfritt med KI), og leverer en redigerbar fil og en forståelig rapport.

Appen lover ikke full WCAG-etterlevelse og kaller aldri et dokument «universelt utformet».

## Kom i gang (Windows)

1. Installer forutsetninger (én gang): `.NET 10 SDK` og `Node.js LTS`, for eksempel med
   `winget install Microsoft.DotNet.SDK.10` og `winget install OpenJS.NodeJS.LTS`.
   Microsoft Word og PowerPoint anbefales for visuell kontroll.
2. Kjør `installer.bat`.
3. Start med `start.bat`, så åpnes appen på <http://127.0.0.1:5317/>. Stopp med `stopp.bat`.

Mer i [docs/INSTALLASJON-OG-BRUK.md](docs/INSTALLASJON-OG-BRUK.md).

## Dokumentasjon

| Dokument | Innhold |
|---|---|
| [Installasjon og bruk](docs/INSTALLASJON-OG-BRUK.md) | Norsk installasjons- og brukerveiledning |
| [Arkitektur](docs/ARKITEKTUR.md) | Moduler, dataflyt og begrunnede tekniske valg |
| [Tekniske bevis](docs/TEKNISKE-BEVIS.md) | Fase A: filbevaring, leserekkefølge, skjulte titler, overskrifter, rendering, OCR |
| [Regelregister](docs/REGELREGISTER.md) | Regler, standardprofiler, kilder med dato |
| [Testresultater](docs/TESTRESULTATER.md) | Resultater fra faktisk gjennomførte tester |
| [Manuell testprotokoll](docs/MANUELL-TESTPROTOKOLL.md) | NVDA + Word/PowerPoint og Office sin tilgjengelighetskontroll (ikke utført ennå) |
| [Evalueringsrubrikk](docs/EVALUERINGSRUBRIKK.md) | Norsk rubrikk for alternative tekster og evaluering av KI-modeller |
| [Begrensninger](docs/BEGRENSNINGER.md) | Kjente begrensninger og støttede Office-versjoner |
| [Sikkerhet og personvern](docs/SIKKERHET-OG-PERSONVERN.md) | Trusselbilde og tiltak |
| [Avhengigheter](docs/AVHENGIGHETER.md) | Låste versjoner og lisenser |

## Utvikling

```bash
kjor-tester.bat
```

```bash
kjor-bevis.bat
```

Testkorpuset i `testkorpus/` er syntetisk: laget med `UuDok.Tools` (bilder og håndskrevet OOXML) og
`UuDok.WinHelper create-corpus` (filer laget av Word/PowerPoint).

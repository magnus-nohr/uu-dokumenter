# UU-dokumenter

Lokal webapp som hjelper lærere, elever og studenter med å forbedre universell utforming av eksisterende
Word- (.docx) og PowerPoint-filer (.pptx). Appen analyserer filen, gjør dokumenterte og kontrollerte forbedringer,
lager kontekstbaserte alternative tekster (valgfritt med KI), og leverer en redigerbar fil og en forståelig rapport.

Appen lover ikke full WCAG-etterlevelse og kaller aldri et dokument «universelt utformet».

**Status:** Tidlig versjon. Appen er bare testet på én Windows 11-maskin med Microsoft 365, og den manuelle testen med
skjermleser er ikke utført ennå. Les gjennom resultatet før du deler filene videre.

## Hva appen gjør

- Legger til alternative tekster på bilder, lysbildetitler, dokumenttittel, språk og struktur.
- Flytter tekst fra bilder inn som ekte tittel og kulepunkter bak bildet, slik at skjermlesere og leselister får den.
- Bevarer utseendet som standard, og kontrollerer det i Word og PowerPoint når de er installert.
- Lager en rapport over hva som er gjort, hva som er usikkert og hva du må vurdere selv.
- Kjører lokalt på din maskin. KI er valgfritt: lokal modell (Ollama) eller din egen ChatGPT Plus/Pro-konto.

## Kom i gang (Windows)

- **[Last ned siste versjon](https://github.com/magnus-nohr/uu-dokumenter/releases/latest):** ferdig pakke for Windows.
  Du trenger ikke installere noe annet.
- **[Installasjonsveileder](docs/INSTALLASJON.md):** fra nedlasting til app som kjører, uten at du trenger å kunne
  programmering.
- **[Brukerveiledning](docs/BRUKERVEILEDNING.md):** slik forbedrer du filene dine.

Vil du bygge selv: installer `.NET 10 SDK` og `Node.js LTS`, kjør `installer.bat`, start med `start.bat`
(<http://127.0.0.1:5317/>) og stopp med `stopp.bat`.

## Dokumentasjon

| Dokument | Innhold |
|---|---|
| [Installasjonsveileder](docs/INSTALLASJON.md) | Installere, oppdatere, avinstallere og feilsøke |
| [Brukerveiledning](docs/BRUKERVEILEDNING.md) | Bruk av appen, ChatGPT, personvern og feilmeldinger |
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
`UuDok.WinHelper create-corpus` (filer laget av Word/PowerPoint). Mappen `testkorpus/ondsinnet/` inneholder bevisst
skadelige testfiler, som ZIP-bomber. Antivirusprogrammer kan reagere på dem.

## Lisens

Copyright © 2026 Magnus Nohr. Lisensiert under [GNU General Public License v3.0](LICENSE). Programmet leveres uten
noen garanti.

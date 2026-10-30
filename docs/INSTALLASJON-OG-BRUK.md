# Installasjon og bruk

## Hva du trenger

- Windows 10 eller 11 (64-bit).
- .NET 10 SDK og Node.js LTS (bare for installasjonen; appen kjører uten Node etterpå).
- Anbefalt: Microsoft 365 Word og PowerPoint. Da kontrollerer appen at utseendet er uendret. Uten Office fungerer
  appen, men rapporten sier «Visuell bevaring er ikke kontrollert i Office».
- Valgfritt: norsk OCR-språk i Windows (Innstillinger → Tid og språk → Språk → Norsk bokmål → Språkalternativer →
  Optisk tegngjenkjenning). Brukes til å lese tekst i bilder lokalt.
- Valgfritt: en lokal KI-modell med bildestøtte (for eksempel via Ollama på `http://127.0.0.1:11434`).

## Installere

1. Last ned eller kopier mappen `UU-dokumenter` til maskinen.
2. Dobbeltklikk `installer.bat`. Den
   - kontrollerer .NET og Node,
   - henter pakker med låste versjoner,
   - bygger brukergrensesnittet og appen til mappen `publish`,
   - viser hvilke Office-versjoner som ble funnet.

## Starte og stoppe

- **Start:** dobbeltklikk ikonet **UU-dokumenter** på skrivebordet (installeren lager det), eller `start.bat`. Appen
  åpnes i nettleseren på <http://127.0.0.1:5317/>. Den er bare tilgjengelig fra denne maskinen.
- **Stopp:** dobbeltklikk `stopp.bat`. Jobber som var i gang, fortsetter neste gang appen startes.

Data (jobber, innstillinger) lagres i `%LOCALAPPDATA%\UuDok`. API-nøkler lagres i Windows Legitimasjonsbehandling.

## Bruke appen

1. **Velg filer:** «Velg filer» eller dra og slipp. Word (.docx) og PowerPoint (.pptx), opptil 500 MB per fil.
2. **Velg behandling:**
   - *Bevar utseendet* (standard): bare usynlige forbedringer.
   - *Tillat synlige forbedringer*: du kan i tillegg velge synlige endringer (kontrast, lenketekster). De lagres i en
     egen fil, og originalvarianten finnes fortsatt.
3. **Velg beskrivelser:** *Uten KI*, *Lokal KI-modell* eller *Skytjeneste* (bare hvis du har godkjent den i
   Innstillinger). Finnes en lokal bildemodell (Ollama) på maskinen, velger appen den automatisk. Når KI er valgt,
   bestemmer du hva som skjer med usikre forslag:
   - *Godt nok* (standard): forslagene brukes automatisk og merkes som usikre i rapporten. Bare forslag med tall eller
     navn som ikke finnes i kildene, holdes tilbake.
   - *Forsiktig*: bare sikre forslag brukes, resten vises etterpå.
   - *Gjennomgå forslag*: du vurderer de usikre.
4. **Forbedre filene.** Du ser fremdrift og kan avbryte.
5. **Resultat:** les oppsummeringen og last ned den forbedrede filen og rapporten (HTML eller JSON).
6. **Bruk alle forslag:** øverst i resultatet (og i gjennomgangen) bruker én knapp alle forslagene som venter, og
   lager filen på nytt. Forslagene merkes som «ikke kontrollert enkeltvis» i rapporten.
7. **Gjennomgå forslag (valgfritt):** for hvert forslag ser du bildet, konteksten, eksisterende tekst, forslaget og hvorfor det er
   usikkert. Velg *Godta*, *Rediger*, *Behold original* eller *Hopp over*. Like problemer er gruppert, men hvert bilde
   beholder sin egen beskrivelse. Etterpå lages filen på nytt og kontrolleres igjen.

### Bilder med tekst i (for eksempel KI-genererte lysbildebilder)

Mangler et lysbilde tittel og består av et stort bilde med tekst i, leser appen teksten lokalt (Windows-OCR). Den
øverste av de største linjene blir lysbildets **tittel**, og resten blir **brødtekst**. Begge legges som ekte
tittel- og brødtekstplassholdere **bak bildet**, slik at skjermlesere og leselister får teksten, mens lysbildet ser
helt likt ut (kontrolleres i PowerPoint). Leserekkefølgen blir tittel, brødtekst, bilde. Har bildet gjennomsiktighet,
legges feltene utenfor lysbildet i stedet, slik at ingenting kan skinne gjennom.

Bildets alternative tekst skal da bare beskrive selve illustrasjonen. Det krever KI eller at du skriver den selv;
uten KI står bildet igjen som «mangler beskrivelse» i rapporten. Er lesingen usikker, eller bildet har mye tekst
(over 12 linjer), blir det et forslag du godkjenner og kan rette i gjennomgangen (første linje er tittel).

### Innstillinger

- **Lokal KI:** adresse (må være på denne maskinen) og modellnavn. «Test tilkoblingen» viser tilgjengelige modeller.
- **Skytjeneste:** les hva som sendes, og kryss av for godkjenning. Lagre API-nøkkelen (den vises aldri igjen).
  Lokal KI faller aldri automatisk tilbake til skytjenesten.
- **Oppbevaring:** jobber slettes automatisk etter valgt antall dager (standard 7). Du kan slette jobber selv.
- **Visuell kontroll:** av/på. **Rapportprofil:** norsk regelverk, EN 301 549 V3.2.1, V4.1.1 eller beste praksis.

## Feilmeldinger du kan få

| Melding | Hva du kan gjøre |
|---|---|
| Passordbeskyttet eller gammelt binærformat | Fjern passordet i Office, eller lagre som .docx/.pptx. |
| Makroer | Lagre som .docx/.pptx uten makroer. |
| Skadet fil | Åpne og lagre filen på nytt i Office. |
| Beskyttet mot redigering | Appen analyserer, men endrer ikke filen. Fjern beskyttelsen selv hvis du har lov. |
| Digitalt signert | Kryss av «Endre digitalt signerte filer» hvis du godtar at signaturen blir ugyldig. |
| Visuell bevaring er ikke kontrollert i Office | Word/PowerPoint mangler, eller kontrollen er slått av. Se gjennom filen selv. |

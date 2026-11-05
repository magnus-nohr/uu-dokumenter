# Installasjonsveileder

Denne veiledningen tar deg fra GitHub til en app som kjører på din egen Windows-maskin. Du trenger ikke kunne
programmering. Regn med 15–20 minutter første gang, mest til nedlastinger.

Når appen er installert, går du videre til [brukerveiledningen](BRUKERVEILEDNING.md).

## Dette trenger du

| | Hva | Merknad |
|---|---|---|
| **Må ha** | Windows 10 eller 11 (64-bit) | Appen er bare testet på Windows 11. |
| **Må ha** | .NET 10 SDK og Node.js LTS | Brukes til å bygge appen på maskinen din. Se steg 1. |
| Anbefalt | Microsoft 365 Word og PowerPoint | Da kontrollerer appen at utseendet er uendret. Uten Office virker appen, men rapporten sier at utseendet ikke er kontrollert. |
| Anbefalt | Norsk tekstgjenkjenning i Windows | Trengs for å lese tekst i bilder. Se [valgfrie tillegg](#valgfrie-tillegg). |
| Valgfritt | Lokal KI-modell eller ChatGPT Plus/Pro | Trengs bare for å lage beskrivelser av bilder. Se [valgfrie tillegg](#valgfrie-tillegg). |

## Steg 1: Installer .NET og Node.js

Du gjør dette én gang.

1. Trykk på Windows-tasten, skriv `Terminal` og trykk Enter.
2. Lim inn denne kommandoen og trykk Enter. Svar ja hvis Windows spør om tillatelse.

   ```bash
   winget install Microsoft.DotNet.SDK.10
   ```

3. Gjør det samme med denne kommandoen:

   ```bash
   winget install OpenJS.NodeJS.LTS
   ```

4. Lukk Terminal-vinduet.

## Steg 2: Last ned appen

1. Gå til <https://github.com/magnus-nohr/uu-dokumenter>.
2. Trykk den grønne knappen **Code** og velg **Download ZIP**.
3. Høyreklikk den nedlastede filen og velg **Pakk ut alle**.
4. Legg den utpakkede mappen et sted du finner igjen, for eksempel `C:\Programmer-egne\UU-dokumenter`.

Unngå mapper som synkroniseres med OneDrive. Appen består av mange små filer, og synkronisering kan låse dem mens
appen bygges.

Bruker du Git, kan du i stedet hente prosjektet med denne kommandoen:

```bash
git clone https://github.com/magnus-nohr/uu-dokumenter.git
```

## Steg 3: Installer appen

1. Åpne mappen og dobbeltklikk `installer.bat`.
2. Viser Windows meldingen «Windows beskyttet PCen», trykker du **Mer informasjon** og deretter **Kjør likevel**.
   Meldingen vises fordi filen er lastet ned fra nettet og ikke er signert.
3. Vent til vinduet viser `Ferdig`. Det kan ta noen minutter.

Installasjonen lager ikonet **UU-dokumenter** på skrivebordet.

## Steg 4: Start appen

Dobbeltklikk ikonet **UU-dokumenter** på skrivebordet (eller `start.bat` i mappen). Appen åpnes i nettleseren på
<http://127.0.0.1:5317/>. Adressen er bare tilgjengelig fra din egen maskin.

Du stopper appen ved å dobbeltklikke `stopp.bat`. Jobber som var i gang, fortsetter neste gang appen startes.

## Valgfrie tillegg

### Tekst i bilder (norsk tekstgjenkjenning)

Appen bruker Windows sin egen tekstgjenkjenning. Slik slår du den på:

1. Åpne **Innstillinger → Tid og språk → Språk og område**.
2. Legg til **Norsk bokmål** hvis det mangler.
3. Trykk **…** ved språket, velg **Språkalternativer**, og last ned **Optisk tegngjenkjenning** hvis det er tilbudt.

### Bildebeskrivelser med KI

Velg ett av disse. Uten KI lager appen bare beskrivelser fra dokumentets egne data, for eksempel diagrammer og tekst
i bilder.

- **Lokal modell:** Ingenting sendes ut, men maskinen bør ha et godt grafikkort eller mye minne. Appen finner
  modellen selv neste gang den startes. Slik installerer du Ollama og en modell med bildestøtte:

  ```bash
  winget install Ollama.Ollama
  ```

  ```bash
  ollama pull gemma3:12b
  ```

- **ChatGPT:** Krever ChatGPT Plus eller Pro, men ingen API-nøkkel. Du kobler til kontoen i appen under
  **Innstillinger**. Se [brukerveiledningen](BRUKERVEILEDNING.md#chatgpt) for hva som sendes til OpenAI.

## Oppdatere til en ny versjon

1. Last ned den nye versjonen som i steg 2, og pakk den ut over den gamle mappen (eller kjør `git pull`).
2. Dobbeltklikk `installer.bat`. Den stopper appen selv hvis den kjører.

Jobber, innstillinger og ChatGPT-tilkoblingen beholdes.

## Avinstallere

1. Dobbeltklikk `stopp.bat`.
2. Slett mappen med appen og ikonet på skrivebordet.
3. Trykk Windows-tast + R, skriv `%LOCALAPPDATA%\UuDok` og slett mappen som åpnes. Der ligger jobber og innstillinger.
4. Hvis du har koblet til ChatGPT: velg **Koble fra ChatGPT** i appen før du sletter den. Ellers åpner du
   **Legitimasjonsbehandling** i Windows og fjerner oppføringene som starter med `UuDok:`.

## Hvis noe går galt

| Det du ser | Hva du kan gjøre |
|---|---|
| `.NET 10 SDK mangler` eller `Node.js mangler` | Gjør steg 1 på nytt, lukk vinduet og kjør `installer.bat` igjen. |
| `winget` blir ikke gjenkjent | Installer **App Installer** fra Microsoft Store, eller last ned [.NET 10 SDK](https://dotnet.microsoft.com/download) og [Node.js LTS](https://nodejs.org) fra nettsidene. |
| `Installasjonen feilet` | Les meldingene over i vinduet. Sjekk at maskinen er på nett, og flytt mappen ut av OneDrive hvis den ligger der. |
| `Mappen publish kunne ikke tømmes` | Lukk appen og vinduer som viser mappen, og kjør `installer.bat` igjen. |
| `Appen er ikke installert ennå` | Kjør `installer.bat` først. |
| Siden åpner seg ikke | Kjør `stopp.bat`, og start appen på nytt. Port 5317 kan også være i bruk av et annet program. |
| `Grensesnittet mangler` | Kjør `stopp.bat`, så `installer.bat`, og start på nytt. |
| Port 1455 er opptatt ved ChatGPT-innlogging | Lukk andre programmer som logger inn med ChatGPT, for eksempel Codex, og prøv igjen. |

Finner du en feil? Meld den under [Issues](https://github.com/magnus-nohr/uu-dokumenter/issues). Ikke legg ved
dokumenter med personopplysninger.

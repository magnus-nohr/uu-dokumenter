# Installasjonsveileder

Denne veiledningen tar deg fra GitHub til en app som kjører på din egen Windows-maskin. Du trenger ikke kunne
programmering, og du trenger ikke installere noe annet. Regn med fem minutter.

Når appen er installert, går du videre til [brukerveiledningen](BRUKERVEILEDNING.md).

## Dette trenger du

| | Hva | Merknad |
|---|---|---|
| **Må ha** | Windows 10 eller 11 (64-bit) | Appen er bare testet på Windows 11. Maskiner med ARM-prosessor støttes ikke av pakken. |
| Anbefalt | Microsoft 365 Word og PowerPoint | Da kontrollerer appen at utseendet er uendret. Uten Office virker appen, men rapporten sier at utseendet ikke er kontrollert. |
| Anbefalt | Norsk tekstgjenkjenning i Windows | Trengs for å lese tekst i bilder. Se [valgfrie tillegg](#valgfrie-tillegg). |
| Valgfritt | Lokal KI-modell eller ChatGPT Plus/Pro | Trengs bare for å lage beskrivelser av bilder. Se [valgfrie tillegg](#valgfrie-tillegg). |

## Steg 1: Last ned appen

1. Gå til [siste versjon](https://github.com/magnus-nohr/uu-dokumenter/releases/latest).
2. Under **Assets** trykker du filen som heter `UU-dokumenter-…-win-x64.zip`.

Bruk denne filen, ikke **Source code** eller den grønne **Code**-knappen. De inneholder bare kildekoden.

## Steg 2: Pakk ut

1. Høyreklikk den nedlastede filen og velg **Pakk ut alle**.
2. Velg `C:\` som mål. Da får du mappen `C:\UU-dokumenter`.

Ikonet på skrivebordet peker til denne mappen, så velg et fast sted. Unngå mapper som synkroniseres med OneDrive.

## Steg 3: Lag ikon på skrivebordet

1. Åpne mappen og dobbeltklikk `lag-skrivebordsikon.bat`.
2. Viser Windows meldingen «Windows beskyttet PCen», trykker du **Mer informasjon** og deretter **Kjør likevel**.
   Meldingen vises fordi appen er lastet ned fra nettet og ikke er signert med et kommersielt sertifikat. Den kan
   komme igjen første gang du starter appen.

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

1. Dobbeltklikk `stopp.bat`.
2. Last ned den nye ZIP-filen som i steg 1.
3. Slett den gamle mappen, og pakk ut den nye på samme sted.

Jobber, innstillinger, ChatGPT-tilkoblingen og ikonet på skrivebordet beholdes.

## Avinstallere

1. Dobbeltklikk `stopp.bat`.
2. Slett mappen med appen og ikonet på skrivebordet.
3. Trykk Windows-tast + R, skriv `%LOCALAPPDATA%\UuDok` og slett mappen som åpnes. Der ligger jobber og innstillinger.
4. Hvis du har koblet til ChatGPT: velg **Koble fra ChatGPT** i appen før du sletter den. Ellers åpner du
   **Legitimasjonsbehandling** i Windows og fjerner oppføringene som starter med `UuDok:`.

## Hvis noe går galt

| Det du ser | Hva du kan gjøre |
|---|---|
| `Fant ikke appen` | Pakk ut hele ZIP-filen først. Filene virker ikke hvis du åpner dem rett fra ZIP-filen. |
| Windows eller antivirus stopper appen | Trykk **Mer informasjon** og **Kjør likevel**. Er Smart App Control slått på, eller stopper skolens IT-regler appen, må du be IT-avdelingen om hjelp. |
| Siden åpner seg ikke | Kjør `stopp.bat`, og start appen på nytt. Port 5317 kan også være i bruk av et annet program. |
| Ikonet virker ikke lenger | Mappen er flyttet. Kjør `lag-skrivebordsikon.bat` på nytt. |
| Port 1455 er opptatt ved ChatGPT-innlogging | Lukk andre programmer som logger inn med ChatGPT, for eksempel Codex, og prøv igjen. |

Finner du en feil? Meld den under [Issues](https://github.com/magnus-nohr/uu-dokumenter/issues). Ikke legg ved
dokumenter med personopplysninger.

## For utviklere: bygge fra kildekoden

Du trenger .NET 10 SDK og Node.js LTS:

```bash
winget install Microsoft.DotNet.SDK.10
```

```bash
winget install OpenJS.NodeJS.LTS
```

Hent koden, og kjør `installer.bat`. Den bygger appen til mappen `publish` og lager ikonet på skrivebordet.

```bash
git clone https://github.com/magnus-nohr/uu-dokumenter.git
```

Nedlastingspakken lages med `tools\lag-pakke.ps1`, som bygger siste commit med innebygd .NET.

# Brukerveiledning

UU-dokumenter hjelper deg å gjøre Word- og PowerPoint-filer lettere å bruke for dem som leser med skjermleser eller
leselist. Du laster inn en fil, og appen lager en forbedret kopi og en rapport. Originalfilen din endres aldri.

Appen er et hjelpemiddel. Den kan ikke garantere at et dokument er universelt utformet, og noen ting må et menneske
vurdere. Rapporten viser hva som er gjort, og hva som gjenstår.

Har du ikke installert appen ennå? Se [installasjonsveilederen](INSTALLASJON.md).

## Kort fortalt

1. Start appen med ikonet **UU-dokumenter** på skrivebordet.
2. Velg filene dine.
3. Trykk **Forbedre filene**.
4. Trykk **Bruk alle forslag** hvis appen har forslag som venter.
5. Last ned den forbedrede filen og rapporten.

Standardvalgene passer for de fleste.

## Steg for steg

### 1. Velg filer

Trykk **Velg filer**, eller dra og slipp filene på siden. Appen tar Word (.docx) og PowerPoint (.pptx), opptil 500 MB
per fil og 20 filer om gangen.

### 2. Velg hvordan filene skal behandles

- **Bevar utseendet (anbefalt):** Bare usynlige forbedringer, som beskrivelser, titler og struktur. Tekst, farger og
  oppsett endres ikke.
- **Tillat synlige forbedringer:** Du kan i tillegg velge synlige endringer, som bedre kontrast og tydeligere
  lenketekster. De lagres i en egen fil.

### 3. Velg hvordan bilder skal beskrives

- **Uten KI:** Beskrivelser lages bare fra dokumentets egne data og tekst i bilder.
- **Lokal KI-modell:** Bildene behandles på maskinen din, og ingenting sendes ut. Valget vises når appen har funnet en
  modell.
- **ChatGPT:** Bildene sendes til OpenAI og bruken trekkes fra abonnementet ditt. Valget er aktivt når kontoen er
  koblet til. Se [ChatGPT](#chatgpt).

Når du bruker KI, velger du også hva som skjer med usikre forslag:

- **Godt nok (anbefalt):** Forslagene brukes automatisk, og de usikre merkes i rapporten. Bare forslag med tall eller
  navn som ikke finnes i kildene, holdes tilbake.
- **Forsiktig:** Bare sikre forslag brukes. Resten vises etterpå.
- **Gjennomgå forslag:** Du vurderer de usikre selv.

### 4. Forbedre filene

Trykk **Forbedre filene**. Du ser fremdriften og kan avbryte underveis. Filene ligger også under **Mine jobber**.

### 5. Se resultatet

Du får en oppsummering av hva som er gjort. Herfra laster du ned den forbedrede filen og rapporten (HTML eller JSON).

Venter det forslag, kan du velge:

- **Bruk alle forslag:** Én knapp bruker alle forslagene og lager filen på nytt. De merkes som «ikke kontrollert
  enkeltvis» i rapporten.
- **Gjennomgå forslag:** For hvert forslag ser du bildet, konteksten, eksisterende tekst, forslaget og hvorfor det er
  usikkert. Velg **Godta**, **Rediger**, **Behold original** eller **Hopp over**. Etterpå lages filen på nytt og
  kontrolleres igjen.

### 6. Les gjennom

Åpne den forbedrede filen og se over den. Dette gjelder særlig forslag som er merket usikre i rapporten. KI og
tekstgjenkjenning kan ta feil.

## Bilder med tekst i

Mange lysbilder består av ett stort bilde med tekst, for eksempel KI-genererte lysbilder. En skjermleser kan ikke lese
den teksten. Mangler lysbildet tittel, gjør appen dette:

- Den leser teksten i bildet lokalt med Windows sin tekstgjenkjenning.
- Den største teksten øverst blir lysbildets **tittel**. Resten blir **brødtekst** med kulepunkter.
- Teksten legges **bak bildet**, så lysbildet ser likt ut. Leserekkefølgen blir tittel, brødtekst, bilde.
- Tomme plassholdere fra malen («Klikk for å legge til tekst») fylles. Dermed kan skjermlesere og leselister gå
  gjennom teksten punkt for punkt.
- Linjer som bare er brutt i bildet, slås sammen til ett punkt.

Har bildet gjennomsiktige områder, legges teksten utenfor lysbildet i stedet, så den ikke skinner gjennom.

Bildets alternative tekst skal da bare beskrive selve illustrasjonen. Det krever KI, eller at du skriver den selv.
Tekstgjenkjenningen kan lese feil, særlig i små tekster og snakkebobler. Er lesingen usikker, eller bildet har over 12
tekstlinjer, kan appen vise teksten som et forslag du kan rette (første linje er tittel).

## Innstillinger

### Lokal KI-modell

Appen finner en lokal modell med bildestøtte selv. Du kan også oppgi adresse og modellnavn. Adressen må være på denne
maskinen. **Test tilkoblingen** viser hvilke modeller som finnes. Tomt modellnavn slår av lokal KI.

### ChatGPT

1. Les hva som sendes, og kryss av.
2. Trykk **Koble til ChatGPT-kontoen**.
3. Logg inn hos OpenAI i fanen som åpnes. Appen ser aldri passordet ditt, og du trenger ingen API-nøkkel.

Dette krever ChatGPT Plus eller Pro. Bruken trekkes fra abonnementet ditt, og **Administrer bruk** viser forbruket.
**Koble fra ChatGPT** trekker tilgangen tilbake. Lokal KI går aldri automatisk over til ChatGPT.

**Dette sendes til OpenAI:** bildet slik det vises i dokumentet, og kort tekst rundt det (overskrift, bildetekst,
eksisterende beskrivelse og tekst lest fra bildet). Filnavn og resten av dokumentet sendes ikke. Appen ber OpenAI om
ikke å lagre svarene.

**Viktig:** En privat ChatGPT-konto er ikke dekket av avtalene til skolen eller arbeidsgiveren din. Ikke bruk den for
dokumenter med personopplysninger eller annet innhold som ikke kan deles med OpenAI. Bruk heller lokal KI eller
**Uten KI** for slike dokumenter.

### Andre innstillinger

- **Oppbevaring:** Jobber slettes automatisk etter valgt antall dager (standard 7). Du kan også slette dem selv.
- **Visuell kontroll:** Lar appen sammenligne utseendet i Word og PowerPoint.
- **Standardprofil i rapporten:** Norsk regelverk, EN 301 549 V3.2.1, V4.1.1 eller beste praksis.

## Personvern

- Appen kjører på maskinen din og kan bare nås derfra.
- Filer og jobber ligger i `%LOCALAPPDATA%\UuDok`, og slettes etter oppbevaringstiden.
- Uten KI og med lokal KI sendes ingenting ut.
- Innloggingen til ChatGPT lagres i Windows Legitimasjonsbehandling.

Mer om dette står i [Sikkerhet og personvern](SIKKERHET-OG-PERSONVERN.md).

## Feilmeldinger du kan få

| Melding | Hva du kan gjøre |
|---|---|
| Passordbeskyttet eller gammelt binærformat | Fjern passordet i Office, eller lagre som .docx/.pptx. |
| Makroer | Lagre som .docx/.pptx uten makroer. |
| Skadet fil | Åpne og lagre filen på nytt i Office. |
| Beskyttet mot redigering | Appen analyserer, men endrer ikke filen. Fjern beskyttelsen selv hvis du har lov. |
| Digitalt signert | Kryss av «Endre digitalt signerte filer» hvis du godtar at signaturen blir ugyldig. |
| Visuell bevaring er ikke kontrollert i Office | Word/PowerPoint mangler, eller kontrollen er slått av. Se gjennom filen selv. |
| Krever ChatGPT Plus eller Pro | Kontoen din har ikke et abonnement som kan brukes. Velg lokal KI eller **Uten KI**. |

Se også [kjente begrensninger](BEGRENSNINGER.md).

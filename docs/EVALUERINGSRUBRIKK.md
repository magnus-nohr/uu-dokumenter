# Evalueringsrubrikk for alternative tekster

Rubrikken brukes til menneskelig vurdering av alternative tekster laget av KI eller mennesker, og til å velge
standardmodell. Mockede KI-svar i de automatiske testene dokumenterer bare integrasjonen, ikke modellkvalitet.

## Kriterier (0–2 poeng hver)

| Kriterium | 0 | 1 | 2 |
|---|---|---|---|
| **Korrekthet** | Feil innhold, eller tolker uthevinger/tekst feil | Mindre unøyaktigheter | Alt som sies, stemmer med bildet og kildene |
| **Relevans** | Beskriver uvesentlige detaljer | Delvis det viktige | Formidler det bildet tilfører i sammenhengen |
| **Kontekst** | Ignorerer overskrift/avsnitt | Noe kobling | Tilpasset funksjonen i undervisningen (for eksempel hva skjermbildet viser at man skal gjøre) |
| **Lesbarhet** | Uforståelig eller objektliste | Stivt eller for langt | Klart, kort, vanlig norsk |
| **Unødvendig gjentakelse** | Gjentar tittel/bildetekst/avsnitt ordrett | Noe gjentakelse | Ingen gjentakelse av tekst som allerede leses |
| **Oppdiktede detaljer** | Navn, tall, tekst eller formål som ikke finnes | Usikker antakelse uten merking | Ingen oppdiktede detaljer; usikkerhet er merket |

Hvis **Oppdiktede detaljer = 0**, regnes svaret som underkjent uansett total.

Tilleggsregler:
- Identifiserer personer eller gjetter sensitive egenskaper → underkjent.
- Dekorasjon markert som dekorativ → 2 på alle kriterier.
- «Krever gjennomgang» på et bilde med for lite grunnlag er riktig oppførsel og gir full uttelling.

## Evalueringsrutine før valg av standardmodell

1. Utvid `evaluering/sett.json` med lovlig tilgjengelige, norske pedagogiske bilder: illustrasjoner, grafer (med og uten
   diagramdata), skjermbilder, tekstbilder, kart, formler, QR-koder og KI-genererte lysbildebilder. Ingen personbilder
   uten samtykke. Minst 50 elementer, fordelt på typene.
2. Kjør for hver kandidatmodell:
   ```bash
   tools\UuDok.Tools\bin\Debug\net10.0\UuDok.Tools.exe eval-ai --set evaluering\sett.json --ollama-model <modell> --out evaluering\resultater
   ```
   Skyleverandører evalueres med tilsvarende adapter (`AnthropicProvider`) bare med data som kan deles eksternt.
3. Automatiske kolonner: riktig rolle, nødvendige fakta nevnt, forbudt innhold, «Bilde av», gjennomgangsflagg, tid.
4. To vurderere fyller ut rubrikkolonnene uavhengig av hverandre, uten å se modellnavnet. Uenighet over 1 poeng diskuteres.
5. Velg modellen med høyest gjennomsnitt **og** null underkjente på «oppdiktede detaljer» i grafer og skjermbilder.
   Dokumenter valget med dato og versjon i `docs/BEGRENSNINGER.md`.

**Status:** Rutinen og et lite syntetisk startsett finnes og er kjørt med en mock-leverandør for å teste verktøyet.
Ingen modell er evaluert ennå, så ingen modell er valgt som dokumentert standard på grunnlag av kvalitet.

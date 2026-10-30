# Manuell testprotokoll: skjermleser og Office sin tilgjengelighetskontroll

**Status: IKKE UTFØRT.** Ingen av testene under er gjennomført ennå. Fyll inn dato, versjoner og resultat når de er utført.
Rapporten fra appen viser disse punktene som «ikke testet» inntil de er dokumentert her.

## Testmiljø (fyll inn)

| Komponent | Versjon |
|---|---|
| Windows | |
| Word (Fil → Konto → Om Word) | |
| PowerPoint | |
| NVDA (NVDA-meny → Hjelp → Om) | |
| UU-dokumenter | |
| Testfil (fra `testkorpus/`) | |

Forbered: kjør filene `office-presentasjon.pptx`, `office-dokument.docx` og `sdk-mangler-mye.docx` gjennom appen i
«Bevar utseendet» uten KI. Behold både original og forbedret fil.

## A. NVDA og PowerPoint (redigeringsvisning og fremvisning)

| # | Steg | Forventet | Resultat original | Resultat forbedret | Merknad |
|---|---|---|---|---|---|
| A1 | Åpne lysbilde 3 (KI-bilde). Les lysbildet med NVDA (pil ned gjennom objekter / `NVDA+Tab`). | Forbedret: «Fotosyntese» leses som tittel først, deretter bildet med «Tekst i bildet: …». | | | |
| A2 | Start fremvisning (F5), gå til lysbilde 3. | Tittelen kunngjøres ved lysbildeskifte. Ingenting synes over lysbildet. | | | |
| A3 | Lysbilde 2: naviger til bildet. | Ny alternativ tekst leses. | | | |
| A4 | Lysbilde 4: naviger gjennom figurene. | Dekorlinjen hoppes over (dekorativ). Rekkefølge: tittel, A, B, gruppe. | | | |
| A5 | Lysbilde 7: skjermbilde med tidligere «tekst, datamaskin …». | Eksisterende god tekst på «Celle med god tekst» er uendret. | | | |
| A6 | Outline-visning (Vis → Disposisjon). | Skjulte titler vises i disposisjonen. | | | |
| A7 | Lysbilde 5: lenken «Klikk her». | Leses fortsatt som «Klikk her» (synlig tekst endres ikke i standardmodus). | | | |

## B. NVDA og Word

| # | Steg | Forventet | Resultat | Merknad |
|---|---|---|---|---|
| B1 | `NVDA+F7` (elementliste) → Overskrifter, i `sdk-mangler-mye.docx` etter godkjente overskriftsforslag. | «Årsplan i samfunnsfag» (nivå 1) og «Vurdering» (nivå 2) vises. **Usikkert om NVDA viser avsnitt med bare dispositionsnivå** – noter faktisk resultat. | | |
| B2 | Navigasjonsruten (Vis → Navigasjonsrute). | Samme overskrifter vises. | | |
| B3 | `G` (neste grafikk) i bla-modus. | Alternativ tekst leses for innebygd bilde og eldre VML-bilde. | | |
| B4 | Tittellinjen og `NVDA+T`. | Dokumenttittel «Årsplan i samfunnsfag» leses (der Word viser tittel). | | |
| B5 | Tabell med overskriftsrad (`office-dokument.docx`). | Kolonneoverskrifter leses ved navigering i tabellen (`Ctrl+Alt+piler`). | | |
| B6 | Språk: les en setning. | Norsk stemme/uttale brukes. | | |

## C. Office sin tilgjengelighetskontroll

Se gjennom → Kontroller tilgjengelighet. Noter antall og type før og etter.

| Fil | Feil før | Feil etter | Advarsler før | Advarsler etter | Gjenstående som appen også rapporterer? |
|---|---|---|---|---|---|
| office-presentasjon.pptx | | | | | |
| office-dokument.docx | | | | | |
| sdk-mangler-mye.docx | | | | | |

Kontroller særlig at PowerPoint ikke rapporterer «Mangler lysbildetittel» for lysbilde 3 og 8 etter behandling.

## D. Leserekkefølge-ruten i PowerPoint

1. Åpne en kopi av `office-presentasjon.pptx`, lysbilde 4. Se gjennom → Kontroller tilgjengelighet → Leserekkefølgerute.
2. Flytt «Dekorlinje» øverst i ruten, og lagre som ny fil.
3. Kjør `UuDok.Tools process <fil> <mappe> --no-render` og sammenlign `p:spTree` i lysbilde 4 med originalen
   (eller pakk ut begge filene og sammenlign `ppt/slides/slide4.xml`).
4. Noter: endret ruten rekkefølgen i `spTree`? Ble det lagt til noen egen utvidelse?

Resultat: ______

## E. Appens webgrensesnitt med NVDA (Edge eller Firefox)

| # | Steg | Forventet | Resultat |
|---|---|---|---|
| E1 | Last siden, `Tab`. | «Hopp til hovedinnhold» først. | |
| E2 | Velg fil med tastatur. | Knappen «Velg filer» åpner filvelgeren; valgt fil kunngjøres («1 fil valgt»). | |
| E3 | Send uten fil. | Feil kunngjøres, fokus til filvelger. | |
| E4 | Fremdrift. | Trinn kunngjøres høflig, ikke hver prosent. | |
| E5 | Gjennomgang. | Knapper har unike navn med kontekst; valg kunngjøres. | |
| E6 | 200 % og 400 % zoom. | Ingen tap av innhold eller funksjon. | |

Utført av: ______ Dato: ______

# Regelregister

Generert fra `src/UuDok.Core/Rules/regelregister.json` (versjon **2026.10.30-1**, kilder kontrollert **2026-10-30**). Ikke rediger for hånd: `UuDok.Tools regler docs/REGELREGISTER.md`.

> Registeret er et arbeidsverktøy. Det gir ikke en juridisk vurdering, og et «bestått» resultat betyr bare at den automatiske kontrollen ikke fant feil. Full WCAG-etterlevelse kan ikke kontrolleres automatisk.

Resultatverdier: **bestått**, **ikke bestått**, **krever vurdering**, **ikke relevant**, **ikke testet**. Manuelle og heuristiske regler kan aldri gi «bestått».

## Profiler

| ID | Navn | Normativ | Grunnlag | Kilder |
|---|---|---|---|---|
| `no` | Norsk regelverksprofil | ja | WCAG 2.1 A/AA via EN 301 549 V3.2.1 for offentlig sektor (webdirektivet). For privat sektor gjelder ifølge Uutilsynet fortsatt WCAG 2.0-kravene (35 suksesskriterier); kriterier som bare finnes i 2.1 er merket. Om et dokument er omfattet (virkeområde, unntak, publisering) avgjøres ikke av appen. | uu-forskrift, uu-wad, lovdata-forskrift |
| `en321` | EN 301 549 V3.2.1 – kapittel 10 | ja | Krav til ikke-nettbaserte dokumenter, WCAG 2.1-basert. | en301549-v321 |
| `en411` | EN 301 549 V4.1.1 – teknisk profil | nei | WCAG 2.2-basert versjon. Holdes adskilt fra norsk lovkrav. | en301549-v411 |
| `bp` | Beste praksis (WCAG 2.2 A/AA + WCAG2ICT) | nei | Relevante WCAG 2.2 A/AA-kriterier tolket for dokumenter etter WCAG2ICT, pluss Office sine anbefalinger der de er nyttige. | wcag22, wcag2ict, ms-a11y-checker |
| `web` | WCAG 2.2 AA for appens webgrensesnitt | nei | Brukes på appens eget grensesnitt (testet med axe-core og manuell protokoll), ikke på dokumentene. | wcag22 |

## Regler

### ALT-MISSING – Bilde eller grafikk mangler alternativ tekst

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.1.1 (A)
- **Kontroll:** automatisk
- **Reparasjon:** forslag (automatisk bare når sikkert)
- **Norsk regelverksprofil:** WCAG 2.1 1.1.1 Ikke-tekstlig innhold (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.1.1 Non-text content
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.1.1 Non-text content (WCAG 2.2)
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.1.1 (A); WCAG2ICT 1.1.1
- **Testmetode:** Bilder, diagrammer, SmartArt, grupper, figurer med bildefyll og eldre VML-bilder uten descr/alt og uten dekorativ-markering.
- **Begrensninger:** Om en tekst er riktig og tilstrekkelig kan ikke kontrolleres automatisk. Figurer uten tekst regnes som mulig dekorasjon (se DECOR-CANDIDATE).

### ALT-POOR – Alternativ tekst ser generisk eller automatisk generert ut

- **Type:** Beste praksis / informasjon; gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.1.1 (A)
- **Kontroll:** heuristisk (kan ikke gi bestått)
- **Reparasjon:** forslag (automatisk bare når sikkert)
- **Norsk regelverksprofil:** WCAG 2.1 1.1.1 (A) – kvalitet
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.1.1
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.1.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.1.1 (A)
- **Testmetode:** Filnavn, enkeltord («bilde», «graf»), Office sine automatiske beskrivelser og kommaseparerte objektlister.
- **Begrensninger:** Heuristisk. Gode, korte beskrivelser kan bli flagget, og dårlige beskrivelser kan slippe gjennom.

### ALT-TEXTIMAGE – Tekst finnes bare i et bilde

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.4.5 (AA)
- **Kontroll:** automatisk
- **Reparasjon:** forslag (automatisk bare når sikkert)
- **Norsk regelverksprofil:** WCAG 2.1 1.1.1 (A); WCAG 2.1 1.4.5 Bilder av tekst (AA)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.1.1; 10.1.4.5 Images of text
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.1.1; 10.1.4.5
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.1.1; WCAG 2.2 1.4.5
- **Testmetode:** Lokal OCR (Windows.Media.Ocr) av bildet slik det vises. Tekst som ikke finnes i alternativ tekst eller rundt bildet.
- **Begrensninger:** OCR kan feile på stilisert eller forvrengt tekst. Uten OCR blir regelen «ikke testet». Et tekstalternativ løser 1.1.1, men 1.4.5 kan fortsatt kreve synlig tekst.

### ALT-COMPLEX – Komplekst bilde (diagram, graf, kart) trenger utfyllende beskrivelse

- **Type:** Beste praksis / informasjon; gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.1.1 (A)
- **Kontroll:** heuristisk (kan ikke gi bestått)
- **Reparasjon:** forslag (automatisk bare når sikkert)
- **Norsk regelverksprofil:** WCAG 2.1 1.1.1 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.1.1
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.1.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.1.1; WCAG2ICT
- **Testmetode:** Diagrammer med datadel, SmartArt og bilder klassifisert som graf/kart/sammensatt.
- **Begrensninger:** Appen lager kort tekst og et utkast til lang beskrivelse. Om informasjonen er fullt tilgjengelig i dokumentet må vurderes av et menneske.

### DECOR-CANDIDATE – Mulig dekorasjon er ikke markert som dekorativ

- **Type:** Beste praksis / informasjon; gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.1.1 (A)
- **Kontroll:** heuristisk (kan ikke gi bestått)
- **Reparasjon:** automatisk når forutsetningene holder
- **Norsk regelverksprofil:** WCAG 2.1 1.1.1 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.1.1
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.1.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.1.1
- **Testmetode:** Koblingslinjer, svært smale bilder (skillelinjer) og figurer uten tekst eller bildefyll.
- **Begrensninger:** Automatisk markering bare for koblingslinjer og tynne skillelinjer. Andre figurer foreslås til gjennomgang.

### DOC-TITLE – Dokumentet mangler tittel i dokumentegenskapene

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 2.4.2 (A)
- **Kontroll:** automatisk
- **Reparasjon:** automatisk når forutsetningene holder
- **Norsk regelverksprofil:** WCAG 2.1 2.4.2 Sidetitler (A), tolket for dokumenter
- **EN 301 549 V3.2.1 – kapittel 10:** 10.2.4.2 Document titled
- **EN 301 549 V4.1.1 – teknisk profil:** 10.2.4.2 Document titled
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 2.4.2; WCAG2ICT 2.4.2
- **Testmetode:** dc:title i docProps/core.xml.
- **Begrensninger:** Tittelen foreslås fra første overskrift eller første lysbildetittel. Om den beskriver dokumentet godt, må vurderes.

### SLIDE-TITLE – Lysbildet mangler tittel

- **Type:** Beste praksis / informasjon; gjelder PPTX
- **WCAG-kriterium:** 1.3.1 (A)
- **Kontroll:** automatisk
- **Reparasjon:** automatisk når forutsetningene holder
- **Norsk regelverksprofil:** WCAG 2.1 1.3.1 (A); WCAG 2.1 2.4.6 (AA)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.1 Info and relationships; 10.2.4.6 Headings and labels
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.1; 10.2.4.6
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.3.1; WCAG 2.2 2.4.6; Office: Mangler lysbildetittel
- **Testmetode:** Tittelplassholder (title/ctrTitle) med tekst.
- **Begrensninger:** En skjult tittel (utenfor lysbildet) er teknisk verifisert i PowerPoint (HasTitle, uendret rendering). Skjermleseropplevelse må bekreftes manuelt.

### SLIDE-TITLE-UNIQUE – Flere lysbilder har samme tittel

- **Type:** Beste praksis / informasjon; gjelder PPTX
- **WCAG-kriterium:** 2.4.6 (AA)
- **Kontroll:** automatisk
- **Reparasjon:** ingen
- **Norsk regelverksprofil:** WCAG 2.1 2.4.6 (AA)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.2.4.6
- **EN 301 549 V4.1.1 – teknisk profil:** 10.2.4.6
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** Office: Duplikate lysbildetitler
- **Testmetode:** Sammenligning av tittelteksten.
- **Begrensninger:** Like titler kan være riktige (for eksempel «Oppgave»). Appen lager aldri kunstige unike titler.

### LANG-DOC – Dokumentspråk mangler eller ser feil ut

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 3.1.1 (A)
- **Kontroll:** automatisk
- **Reparasjon:** automatisk når forutsetningene holder
- **Norsk regelverksprofil:** WCAG 2.1 3.1.1 Språk på siden (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.3.1.1 Language of document
- **EN 301 549 V4.1.1 – teknisk profil:** 10.3.1.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 3.1.1
- **Testmetode:** DOCX: w:lang i docDefaults. PPTX: lang på tekstløp sammenlignet med en enkel språkgjenkjenning (bokmål, nynorsk, engelsk).
- **Begrensninger:** Språkgjenkjenningen er enkel og brukes bare ved tydelige tilfeller med nok tekst.

### LANG-PARTS – Deler på annet språk

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 3.1.2 (AA)
- **Kontroll:** manuell (ikke testet av appen)
- **Reparasjon:** ingen
- **Norsk regelverksprofil:** WCAG 2.1 3.1.2 Språk på deler av innhold (AA)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.3.1.2
- **EN 301 549 V4.1.1 – teknisk profil:** 10.3.1.2
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 3.1.2
- **Testmetode:** Krever manuell vurdering. Appen viser hvilke språk som er merket.
- **Begrensninger:** Umerket fremmedspråklig tekst oppdages ikke pålitelig.

### HEAD-NONE – Langt dokument uten overskrifter

- **Type:** Krav (normativ kilde); gjelder DOCX
- **WCAG-kriterium:** 1.3.1 (A)
- **Kontroll:** automatisk
- **Reparasjon:** forslag (automatisk bare når sikkert)
- **Norsk regelverksprofil:** WCAG 2.1 1.3.1 (A); WCAG 2.1 2.4.6 (AA)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.1; 10.2.4.6
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.1; 10.2.4.6
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.3.1
- **Testmetode:** Ingen avsnitt med overskriftsstil eller dispositionsnivå i et dokument med mer enn 20 avsnitt med tekst.
- **Begrensninger:** Korte dokumenter trenger ikke nødvendigvis overskrifter.

### HEAD-FAKE – Tekst ser ut som en overskrift, men er ikke merket

- **Type:** Krav (normativ kilde); gjelder DOCX
- **WCAG-kriterium:** 1.3.1 (A)
- **Kontroll:** heuristisk (kan ikke gi bestått)
- **Reparasjon:** forslag (automatisk bare når sikkert)
- **Norsk regelverksprofil:** WCAG 2.1 1.3.1 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.1
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.3.1
- **Testmetode:** Korte avsnitt i stor eller fet skrift fulgt av brødtekst.
- **Begrensninger:** I «Bevar utseendet» brukes w:outlineLvl (synlig nøytralt, verifisert i Word). Om NVDA kunngjør dette som overskrift er ikke bekreftet. Krever gjennomgang.

### HEAD-SKIP – Overskriftsnivå hopper over et nivå

- **Type:** Beste praksis / informasjon; gjelder DOCX
- **WCAG-kriterium:** 1.3.1 (A)
- **Kontroll:** automatisk
- **Reparasjon:** ingen
- **Norsk regelverksprofil:** WCAG 2.1 1.3.1 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.1
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.3.1
- **Testmetode:** Nivå N+2 etter nivå N.
- **Begrensninger:** Ikke et absolutt krav, men vanlig anbefaling.

### LIST-FAKE – Liste laget med tegn i stedet for ekte liste

- **Type:** Krav (normativ kilde); gjelder DOCX
- **WCAG-kriterium:** 1.3.1 (A)
- **Kontroll:** heuristisk (kan ikke gi bestått)
- **Reparasjon:** krever synlig endring (egen variant)
- **Norsk regelverksprofil:** WCAG 2.1 1.3.1 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.1
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.3.1
- **Testmetode:** Avsnitt som starter med «-», «•», «1)» og lignende uten listeformatering.
- **Begrensninger:** Ekte liste endrer innrykk og tegn. Rapporteres og repareres ikke automatisk.

### TABLE-HEADER – Tabell mangler markert overskriftsrad

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.3.1 (A)
- **Kontroll:** automatisk
- **Reparasjon:** forslag (automatisk bare når sikkert)
- **Norsk regelverksprofil:** WCAG 2.1 1.3.1 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.1
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.3.1
- **Testmetode:** DOCX: w:tblHeader på første rad. PPTX: firstRow på a:tblPr.
- **Begrensninger:** DOCX: w:tblHeader gjentar raden på nye sider (synlig hvis tabellen deles over sider) og brukes bare etter visuell kontroll. PPTX: firstRow kan endre tabellstilens utseende og hører til «Tillat synlige forbedringer».

### TABLE-MERGED – Tabell med sammenslåtte celler

- **Type:** Beste praksis / informasjon; gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.3.1 (A)
- **Kontroll:** automatisk
- **Reparasjon:** ingen
- **Norsk regelverksprofil:** WCAG 2.1 1.3.1 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.1
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.1
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** Office: Sammenslåtte celler
- **Testmetode:** gridSpan/vMerge/rowSpan/hMerge.
- **Begrensninger:** Celler slås aldri sammen eller deles automatisk.

### LINK-PURPOSE – Lenketeksten forklarer ikke hvor lenken går

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 2.4.4 (A)
- **Kontroll:** heuristisk (kan ikke gi bestått)
- **Reparasjon:** krever synlig endring (egen variant)
- **Norsk regelverksprofil:** WCAG 2.1 2.4.4 Formål med lenke (i kontekst) (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.2.4.4 Link purpose (in context)
- **EN 301 549 V4.1.1 – teknisk profil:** 10.2.4.4
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 2.4.4
- **Testmetode:** Lenketekster som «her», «klikk her», «les mer», «lenke».
- **Begrensninger:** Konteksten rundt kan gjøre formålet klart. Synlig lenketekst endres bare i «Tillat synlige forbedringer». Skjermtips regnes ikke som tilstrekkelig løsning.

### LINK-RAWURL – Nettadresse brukt som lenketekst

- **Type:** Beste praksis / informasjon; gjelder DOCX og PPTX
- **WCAG-kriterium:** 2.4.4 (A)
- **Kontroll:** automatisk
- **Reparasjon:** krever synlig endring (egen variant)
- **Norsk regelverksprofil:** WCAG 2.1 2.4.4 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.2.4.4
- **EN 301 549 V4.1.1 – teknisk profil:** 10.2.4.4
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 2.4.4
- **Testmetode:** Lenketekst som starter med http(s):// eller www.
- **Begrensninger:** Korte, lesbare adresser kan være akseptable, særlig i trykte dokumenter.

### CONTRAST-TEXT – For lav tekstkontrast

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.4.3 (AA)
- **Kontroll:** automatisk
- **Reparasjon:** krever synlig endring (egen variant)
- **Norsk regelverksprofil:** WCAG 2.1 1.4.3 Kontrast (minimum) (AA)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.4.3 Contrast (minimum)
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.4.3
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.4.3
- **Testmetode:** DOCX: eksplisitte tekstfarger mot uthevings-/skyggefarge eller sidebakgrunn (4,5:1, stor tekst 3:1). PPTX: ikke implementert i denne versjonen.
- **Begrensninger:** Tekst over bilder, gradienter og temafarger uten verdi beregnes ikke. En skjult beskrivelse løser aldri kontrastproblemer; de krever synlig endring.

### ORDER-SLIDE – Leserekkefølgen på lysbildet kan være ulogisk

- **Type:** Krav (normativ kilde); gjelder PPTX
- **WCAG-kriterium:** 1.3.2 (A)
- **Kontroll:** heuristisk (kan ikke gi bestått)
- **Reparasjon:** forslag (automatisk bare når sikkert)
- **Norsk regelverksprofil:** WCAG 2.1 1.3.2 Meningsfylt rekkefølge (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.2 Meaningful sequence
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.2
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.3.2
- **Testmetode:** Tittelen leses ikke først. Leserekkefølge = figurtreets rekkefølge = lagrekkefølge (bevis 3/4).
- **Begrensninger:** Tittelen flyttes først bare når den ikke overlapper noen figur den passerer, og resultatet består visuell sammenligning. Øvrig rekkefølge sorteres aldri automatisk.

### ORDER-FLOAT – Flytende objekter kan få uventet leserekkefølge

- **Type:** Krav (normativ kilde); gjelder DOCX
- **WCAG-kriterium:** 1.3.2 (A)
- **Kontroll:** heuristisk (kan ikke gi bestått)
- **Reparasjon:** ingen
- **Norsk regelverksprofil:** WCAG 2.1 1.3.2 (A)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.3.2
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.3.2
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** Office: Objekt er ikke på linje med tekst
- **Testmetode:** Bilder og tekstbokser med flytende plassering (wp:anchor).
- **Begrensninger:** Endres ikke automatisk fordi det endrer oppsettet.

### MEDIA-CAPTIONS – Video eller lyd: teksting og alternativer kan ikke bekreftes

- **Type:** Krav (normativ kilde); gjelder DOCX og PPTX
- **WCAG-kriterium:** 1.2.2 (A)
- **Kontroll:** manuell (ikke testet av appen)
- **Reparasjon:** ingen
- **Norsk regelverksprofil:** WCAG 2.1 1.2.1 (A); WCAG 2.1 1.2.2 Teksting (A); WCAG 2.1 1.2.5 Synstolking (AA)
- **EN 301 549 V3.2.1 – kapittel 10:** 10.1.2.1; 10.1.2.2; 10.1.2.5
- **EN 301 549 V4.1.1 – teknisk profil:** 10.1.2.1; 10.1.2.2; 10.1.2.5
- **Beste praksis (WCAG 2.2 A/AA + WCAG2ICT):** WCAG 2.2 1.2.x
- **Testmetode:** Medieobjekter oppdages. Teksting kontrolleres ikke i denne versjonen.
- **Begrensninger:** En tekstfil ved siden av presentasjonen betyr ikke at mediet er tekstet.

### INFO-SIGNED – Dokumentet er digitalt signert

- **Type:** Beste praksis / informasjon; gjelder DOCX og PPTX
- **Kontroll:** automatisk
- **Reparasjon:** ingen
- **Testmetode:** Signaturrelasjon i pakken.
- **Begrensninger:** Endring ugyldiggjør signaturen. Appen endrer bare etter eksplisitt valg.

### INFO-PROTECTED – Dokumentet er beskyttet mot redigering

- **Type:** Beste praksis / informasjon; gjelder DOCX og PPTX
- **Kontroll:** automatisk
- **Reparasjon:** ingen
- **Testmetode:** documentProtection/writeProtection/modifyVerifier.
- **Begrensninger:** Appen omgår aldri beskyttelse og endrer ikke slike filer.

## Kilder

- **Uutilsynet: Kva seier forskrifta?** – <https://www.uutilsynet.no/regelverk/kva-seier-forskrifta/153> (kontrollert 2026-10-30). Offentlig sektor: WCAG 2.1 A/AA gjennom EN 301 549 V3.2.1 (webdirektivet) fra 1.2.2023. Privat sektor: WCAG 2.0 (35 krav) ifølge flere kilder; sekundærkilder er uenige – bekreft direkte hos Uutilsynet før juridisk bruk.
- **Uutilsynet: EUs webdirektiv (WAD)** – <https://www.uutilsynet.no/webdirektivet-wad/eus-webdirektiv-wad/265> (kontrollert 2026-10-30). Dokumenter publisert på nettsteder omfattes; unntak for kontorformater publisert før 23.9.2018 som ikke trengs i aktive prosesser.
- **Forskrift om universell utforming av IKT-løsninger (FOR-2013-06-21-732)** – <https://lovdata.no/dokument/SF/forskrift/2013-06-21-732> (kontrollert 2026-10-30). Gjeldende tekst skal leses på Lovdata. Ikke lest i fulltekst ved denne kildekontrollen.
- **ETSI EN 301 549 V3.2.1 (2021-03)** – <https://www.etsi.org/deliver/etsi_en/301500_301599/301549/03.02.01_60/en_301549v030201p.pdf> (kontrollert 2026-10-30). Kapittel 10: ikke-nettbaserte dokumenter (WCAG 2.1). Kapittel 11.8: forfatterverktøy. Sitert i EU-tidende (harmonisert).
- **ETSI EN 301 549 V4.1.1 (2026)** – <https://accessible-eu-centre.ec.europa.eu/content-corner/news/european-accessibility-standard-en-301-549-has-been-updated-2026-09-07_en> (kontrollert 2026-10-30). Publisert september 2026, bygger på WCAG 2.2. Per kildekontroll ikke sitert i EU-tidende og ikke norsk lovkrav. Klausulnummer for dokumentkrav er her antatt lik V3.2.1 (10.x = WCAG-nummer) og må verifiseres mot ETSI-teksten.
- **W3C WCAG 2.2** – <https://www.w3.org/TR/WCAG22/> (kontrollert 2026-10-30). W3C-anbefaling.
- **W3C Guidance on Applying WCAG 2 to Non-Web ICT (WCAG2ICT)** – <https://www.w3.org/TR/wcag2ict-22/> (kontrollert 2026-10-30). Veiledning (Group Note), ikke normativ.
- **Microsoft: Regler for tilgjengelighetskontrollen** – <https://support.microsoft.com/office/rules-for-the-accessibility-checker-651e08f2-0fc3-4e10-aaca-74b4a67101c1> (kontrollert 2026-10-30). Beste praksis i Office; ikke normativ.

## Forfatterverktøy (EN 301 549, kapittel 11.8)

Appen endrer dokumenter og må derfor vurderes mot kravene til forfatterverktøy: bevaring av tilgjengelighetsinformasjon ved transformasjon (11.8.3), støtte for reparasjon (11.8.4) og tilgjengelig innhold fra eget arbeid (11.8.2). Designet følger dette: originalinformasjon bevares (urørte deler er byte-like, eksisterende alternativ tekst overskrives ikke uten grunn), funn har reparasjonsforslag, og appen lager ikke utilgjengelig innhold selv. Dette er en designvurdering, ikke en dokumentert samsvarsvurdering. Klausulnumre er hentet fra V3.2.1 og må kontrolleres mot V4.1.1-teksten.

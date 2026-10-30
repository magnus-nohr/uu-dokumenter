# Tekniske bevis (fase A)

De rå, automatisk genererte resultatene ligger i [tekniske-bevis/RESULTATER.md](tekniske-bevis/RESULTATER.md)
og `tekniske-bevis/resultater.json`. Kjør dem på nytt med `kjor-bevis.bat`. Bilder (før/etter/diff) havner i
`probes/output/` (ikke versjonert).

**Testmiljø ved siste kjøring (2026-10-30):** Windows 11 (10.0.26300), Microsoft 365 Apps x64,
Word og PowerPoint versjon 16.0 build 20527 (Click-to-Run 16.0.20527.20112). Windows-OCR med språkpakke `nb`.
Testfilene er syntetiske: laget med PowerPoint/Word via COM (`UuDok.WinHelper create-corpus`) og med
håndskrevet OOXML (`UuDok.Tools crafted`).

Metode for alle bevis som endrer filer: (1) patch med `UuDok.Patching`, (2) kontroller at alle urørte
pakkedeler har identiske dekomprimerte bytes, (3) semantisk XML-diff av endrede deler mot tillatte endringer,
(4) Open XML SDK-validering før/etter (ingen nye feil), (5) åpne original og resultat i Office,
rendre til PNG og sammenligne piksel for piksel, (6) lese tilbake verdien gjennom Office sitt objektmodell-API.

| # | Spørsmål | Resultat | Hva som ble observert |
|---|---|---|---|
| 1 | Kan alt-tekst endres uten andre endringer? | **Bekreftet** (PPTX og DOCX) | Bare `descr` på `p:cNvPr` / `wp:docPr` endret. 67/67 (PPTX) og 27/27 (DOCX) urørte deler byte-like. 0 nye valideringsfeil. 0 avvikende piksler. PowerPoint/Word leser den nye teksten. Gjelder også eldre VML-bilder (`v:shape/@alt`). |
| 2 | Beholdes ukjente utvidelser og urørte deler? | **Bekreftet** | Ignorerbart ukjent navnerom (attributt + element), ukjent `p:extLst`-oppføring, ukjent pakkedel med egen relasjonstype og innholdstype: alt bevart, og Office åpner både injisert original og resultat. |
| 3 | Hvordan virker leserekkefølge i PowerPoint? | **Bekreftet** | PowerPoint lagret ingen egen leserekkefølge-representasjon. Rekkefølgen er rekkefølgen i `p:spTree`, som også er `ZOrderPosition`. |
| 4 | Påvirker omordning overlapping og lag? | **Bekreftet** | Bytte av to overlappende figurer: 580 piksler endret. Bytte av to ikke-overlappende naboer: 0 piksler. Animasjon følger figuren (refererer `spid`), ikke posisjonen. |
| 5 | Lysbildetittel uten synlig endring? | **Bekreftet (teknisk)** | Tittelplassholder (`p:ph type="title"`) satt inn først i `spTree` og plassert over lysbildets øvre kant: `HasTitle = true`, riktig tittel, 0 avvikende piksler. Skjermleseropplevelse er **ikke** testet. |
| 6 | Overskriftsstruktur i Word uten endret layout? | **Bekreftet (teknisk)** | `w:outlineLvl` på avsnitt: Word rapporterer `OutlineLevel` 1 og 2, samme sidetall og samme sidebrudd per avsnitt, 0 avvikende piksler. Om NVDA kunngjør dette som overskrift er **ikke** testet. |
| 6b | Dokumenttittel og språk der metadata mangler | **Bekreftet** | Ny `docProps/core.xml` med relasjon og innholdstype, `w:lang` i `docDefaults`: Word åpner og viser likt. |
| 7 | Før/etter-rendering på brukerens maskin | **Bekreftet** | PowerPoint: `Slide.Export` til PNG (ca. 1 s for 8 lysbilder). Word: `ExportAsFixedFormat` til PDF og rasterisering med `Windows.Data.Pdf` (ca. 7–8 s for 2 sider, mest oppstart). Samme fil rendret to ganger gir 0 avvikende piksler (ingen målbar støy). |
| 8 | Lokal OCR av tekst i KI-bilder | **Bekreftet** | Windows.Media.Ocr (`nb`) leste «Fotosyntese» / «Slik lager plantene næring av sollys» og den skjeve «Vannets kretsløp» / «Fordamping, kondensasjon og nedbør» riktig. Et 40 × 30-bilde ga ingen tekst (riktig: for lite grunnlag). |

## Feil som bevisene avdekket (og som er rettet)

- **Linjeskift i attributter.** Word skriver `&#xA;` inne i attributter (VML-reserven `o:gfxdata`). Første versjon
  av serialiseringen skrev rå linjeskift, som blir mellomrom ved ny innlesing. Rundtur-kontrollen i
  `Patcher` oppdaget avviket og rullet endringen tilbake. Rettet med `NewLineHandling.Entitize`.
- **Word og `Visible:false`.** `InlineShape.AlternativeText` gir COM-feil når Word åpner dokumentet med
  `Visible:false`. Hjelperen utelater nå parameteren i sin egen, usynlige Word-instans.
- **Brukerens Office-vinduer.** En tidlig versjon av hjelperen kalte `Quit` på Word uten å kontrollere hvem
  som eide prosessen. Nå avsluttes Word/PowerPoint bare når hjelperen kan påvise at den startet prosessen selv
  (`OfficeSession`), og ellers lukkes bare egne dokumenter.

## Konsekvenser for løsningen

1. **Leserekkefølge (PPTX):** Appen sorterer aldri figurer automatisk etter koordinater. Endring av rekkefølge
   foreslås bare når figuren ikke overlapper noen figur den passerer i treet (geometrisk kontroll), og
   resultatet må bestå visuell sammenligning. Overlappende tilfeller rapporteres som «krever vurdering».
2. **Skjult tittel (PPTX):** Brukes når et lysbilde mangler tittel. Teksten hentes fra synlig tekst eller
   (for KI-bilder) fra OCR med høy sikkerhet. Plasseringen over lysbildet er valgt fordi den aldri kan
   overlappe innhold. Den vises ikke i fremvisning eller utskrift, men kan sees i redigeringsvisningen
   utenfor lysbildet. Dette står i rapporten.
3. **Overskrifter (DOCX):** I «Bevar utseendet» brukes bare `w:outlineLvl` (synlig nøytralt), og bare etter
   gjennomgang fordi det er heuristisk hvilke avsnitt som er overskrifter. Ekte overskriftsstiler hører til
   «Tillat synlige forbedringer».
4. **Rendering:** Office er standardmotor når den er installert. Toleransen er satt til 0,05 % avvikende
   piksler (kanaltoleranse 8). Det er strengere enn nødvendig, gitt null observert støy.

## Ikke dekket av automatiske bevis (krever manuell test)

Disse står som «ikke testet» til de er utført etter [MANUELL-TESTPROTOKOLL.md](MANUELL-TESTPROTOKOLL.md):

- NVDA i PowerPoint: leses den skjulte tittelen som lysbildetittel, og i riktig rekkefølge?
- NVDA i Word: kunngjøres avsnitt med bare `w:outlineLvl` som overskrift? (Navigasjonsruten viser dem.)
- Office sin tilgjengelighetskontroll før og etter for de samme filene.
- PowerPoint sin «Leserekkefølge»-rute: lagrer den noe annet enn `spTree`-rekkefølgen i denne versjonen?
  Bevis 3 viser bare hva PowerPoint skrev for filer laget via COM.

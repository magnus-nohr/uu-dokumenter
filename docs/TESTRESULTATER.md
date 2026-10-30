# Testresultater

Resultater fra faktisk gjennomførte tester på utviklingsmaskinen **2026-10-30**. Testene som ikke er utført, står oppført
nederst. Kjør alt på nytt med `kjor-tester.bat` og `kjor-bevis.bat`.

**Miljø:** Windows 11 (10.0.26300) x64, .NET SDK 10.0.403, Node.js 26.13.0, Microsoft 365 Word og PowerPoint 16.0
build 20527, Microsoft Edge (Chromium, installert), Windows-OCR `nb`.

## Sammendrag

| Testsett | Antall | Bestått | Feilet | Hoppet over |
|---|---|---|---|---|
| Enhets-, integrasjons- og webtester (`UuDok.Tests`, uten Office) | 65 | 65 | 0 | 0 |
| Office-tester (`UuDok.Tests`, `Category=Office`) | 6 | 6 | 0 | 0 |
| Grensesnitt: Playwright + axe-core i Edge (`UuDok.E2E`) | 8 | 8 | 0 | 0 |
| Frontendkontrakter (Vitest) | 3 | 3 | 0 | 0 |
| Tekniske bevis (`UuDok.Probes`) | 9 | 9 bekreftet | 0 | – |

## Dekning av kravene til testing

| Krav | Dekket av |
|---|---|
| 1. Pakkedeler, relasjoner og bevaring av urørte data | `PatchingTests.Urorte_deler…`, `…Ukjente_utvidelser…`, `…Manglende_kjerneegenskaper…`, bevis P1/P2 |
| 2. Tillatte XML-endringer | `PatchingTests.Semantisk_diff_avviser…`, `…Flytting_i_leserekkefolge…`, `…Dekorativ_markering…` |
| 3. Open XML-validering før/etter | `PatchingTests.Ingen_nye_valideringsfeil…`, pipeline (alle jobber), bevis P1–P6b |
| 4. Idempotens | `AiAndPipelineTests.Ny_behandling_av_resultatet_gjentar_ikke_reparasjoner` |
| 5. Ingen uautorisert nettverkstrafikk i lokal modus | `…Ingen_nettverkstrafikk_i_lokal_modus_uten_KI` (DiagnosticListener), `…Lokal_modell_kalles_bare_paa_loopback…`, `Ekstern_relasjon…hentes_ikke`, `Skyjobb_uten_godkjenning…faller_ikke_tilbake` |
| 6. Reversering ved feil | `PatchingTests.Endring_forkastes_naar_forutsetningen…`, `…Eksisterende_menneskeskrevet…`, Office: forkasting av `w:tblHeader` når tabellen deles over sider |
| 7. Meningsfulle regelresultater | `AnalysisAndRulesTests.Manglende_teststotte_gir_aldri_bestatt`, `…Generisk_objektliste…`, `…Kontrastproblem_krever_synlig_endring…` |
| 8. Visuell regresjon | `OfficeTests.Resultatet_aapnes_i_Office_og_ser_likt_ut` (3 filer), `…Rendering_av_samme_fil_er_stabil`, bevis P1–P7 |
| 9. Tilgjengelig webgrensesnitt | `UuDok.E2E` (axe-core WCAG 2.0–2.2 A/AA på alle sider, tastatur, fokus, feil, 320 px) |
| 10. Eksport åpner uten reparasjonsvarsel | Office-testene og bevisene åpner hvert resultat med `DisplayAlerts=none` og `OpenAndRepair=false`; åpningen lykkes og rendres likt. Se merknad under. |

## Utvalgte observasjoner

- **Stor presentasjon (400 lysbilder, laget av PowerPoint):** 957 funn, 351 endringer utført og verifisert i PowerPoint,
  150 forslag til gjennomgang. Tid totalt med rendering før/etter: 121–171 s.
- **KI-genererte bilder med tekst:** lysbilde 3 og 8 fikk skjulte titler («Fotosyntese», «Vannets kretsløp») fra lokal
  OCR. Bildene fikk alternativ tekst med undertekstene uten å gjenta tittelen. Rendringen var uendret.
- **Forkasting:** I `sdk-mangler-mye.docx` ga `w:tblHeader` 0,75 % avvik på side 2, fordi tabellen deles over sider og
  raden ble gjentatt. Endringen ble forkastet, og de andre endringene ble beholdt.
- **Ondsinnede filer:** zip-slip, ZIP-bombe (150 MB nuller), 20 500 deler, XXE, CFB (kryptert/.doc), forkledd makrofil,
  tekstfil med .docx-endelse og avkuttet ZIP ble avvist med norsk forklaring. Signert fil ble godtatt bare for analyse.
- **Sikkerhet mot kjørende tjeneste:** POST uten CSRF-token ga 403, fremmed Origin ga 403 og fremmed Host ga 421.

## Merknad om «uten reparasjonsvarsel»

Office-testene åpner hvert resultat skrivebeskyttet, med varsler slått av og uten reparasjon. En fil som krever
reparasjon, gir da en feil ved åpning. Alle resultater åpnet uten feil. Et visuelt reparasjonsvarsel i et interaktivt
Word/PowerPoint-vindu er **ikke** kontrollert manuelt. Det står i den manuelle protokollen.

## Ikke utført

- **Manuell NVDA-test** med Word og PowerPoint, og **Office sin tilgjengelighetskontroll** før og etter
  ([MANUELL-TESTPROTOKOLL.md](MANUELL-TESTPROTOKOLL.md)): ikke gjennomført.
- **Modellkvalitet:** ingen lokal modell eller skymodell er evaluert med rubrikken. KI-testene bruker mockede svar.
- **Andre Office-versjoner** enn Microsoft 365 build 20527: ikke testet.
- **LibreOffice** som alternativ renderer: ikke implementert (grensesnittet `IRenderer` er klart).

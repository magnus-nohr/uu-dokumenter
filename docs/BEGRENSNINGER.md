# Begrensninger og støttede versjoner

## Støttet og testet

| Komponent | Versjon | Status |
|---|---|---|
| Windows | 11 (10.0.26300), x64 | Testet |
| Microsoft 365 Word | 16.0 build 20527 (Click-to-Run 16.0.20527.20112) | Tekniske bevis og Office-tester bestått |
| Microsoft 365 PowerPoint | 16.0 build 20527 | Tekniske bevis og Office-tester bestått |
| .NET | 10.0 (SDK 10.0.403) | Byggemiljø |
| Nettleser for grensesnittet | Microsoft Edge (Chromium) | Se `TESTRESULTATER.md` for status på automatiske grensesnittester |
| Windows-OCR | Språkpakke `nb` | Testet |

Andre Office-versjoner (2016/2019/2021, Mac, nett) er **ikke testet**. Office-for-nett og Mac støttes ikke for visuell kontroll.

## Kjente begrensninger

**Generelt**
- Appen kan ikke kontrollere full WCAG-etterlevelse. Mange kriterier krever menneskelig vurdering og står som
  «krever vurdering» eller «ikke testet».
- Bare .docx og .pptx. Maler, fremvisningsfiler, makroformater, krypterte filer og gamle binærformater avvises.
- Signerte filer endres bare etter eksplisitt valg (signaturen blir ugyldig). Beskyttede filer endres aldri.

**PowerPoint**
- Kontrast i PPTX er ikke implementert («ikke testet»). Temafarger, tekst over bilder og gradienter beregnes ikke.
- Leserekkefølge: bare tittelen flyttes først, og bare uten overlapping. Øvrig rekkefølge rapporteres.
- Skjult tittel: teknisk verifisert (`HasTitle`, uendret rendering). Skjermleseropplevelsen er **ikke** bekreftet (se
  manuell protokoll). Tittelen kan sees over lysbildet i redigeringsvisningen.
- Lysbildetittel fra tekst i bilder krever Windows-OCR og et stort bilde (≥ 30 % av lysbildet) med en tydelig tittellinje.
- Diagrammer: beskrivelse uten KI lages fra diagramdataenes hurtigbuffer; avanserte diagramtyper (chartex) leses ikke.
- Tabelloverskrift i PPTX (`firstRow`) kan endre tabellstilen og gjøres ikke automatisk.
- Teksting av video/lyd oppdages ikke; medier rapporteres som «krever vurdering».

**Word**
- Overskrifter i «Bevar utseendet» bruker dispositionsnivå (`w:outlineLvl`). Navigasjonsruten viser dem; om NVDA
  kunngjør dem som overskrifter er ikke bekreftet.
- Overskriftsforslag, falske lister og tabelloverskrifter bygger på heuristikk og krever gjennomgang.
- `w:tblHeader` forkastes automatisk når tabellen deles over sider (raden ville blitt gjentatt).
- Kontrast beregnes bare for eksplisitte farger mot utheving, skygge eller sidebakgrunn. Temafarger uten verdi, tekst i
  tekstbokser over bilder og farger fra tegnstiler i tabeller beregnes ikke alltid.
- Lenker som felt (`HYPERLINK` i feltkoder) analyseres ikke; bare `w:hyperlink`.
- Formler som bilder oppdages ikke; ekte formler (OMML) bevares og rapporteres.
- Sidetall per avsnitt fra Word brukes som sidebrytningskontroll; en endring som flytter et avsnitt til en annen side forkastes.

**KI**
- Ingen modell er evaluert med rubrikken ennå. Skystandard (`claude-opus-5-5`) er et teknisk utgangspunkt, ikke et
  dokumentert kvalitetsvalg.
- Lokal modell krever en tjeneste med Ollama-kompatibelt API og bildestøtte.
- Lange beskrivelser legges i rapporten som utkast, ikke i dokumentet.

**Rendering**
- Statisk rendering beviser ikke at animasjoner og interaksjoner fungerer. Antall animasjoner, overganger og medier sammenlignes strukturelt.
- Rendering av svært store filer tar tid (400 lysbilder: omtrent 2 minutter totalt på testmaskinen).
- Bare én Office-rendering om gangen. Har brukeren PowerPoint åpen, brukes samme PowerPoint-prosess (filen åpnes uten vindu og lukkes etterpå).

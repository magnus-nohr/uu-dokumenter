# Resultater fra tekniske bevis (automatisk generert)

Generert av `probes/UuDok.Probes`. Ikke rediger for hånd – kjør `kjor-bevis.bat` på nytt.

- **maskin**: lokal Windows-maskin
- **os**: Microsoft Windows NT 10.0.26300.0
- **dato**: 2026-10-30 10:22
- **powerpoint**: PowerPoint 16.0 (build 20527)
- **word**: Word 16.0 (build 16.0.20527)

## P7-kalibrering: Rendering av samme fil to ganger (renderingsstøy)

**Status:** bekreftet – Renderingen er deterministisk nok til å kalibrere toleranser.

- PowerPoint-rendering: 8 lysbilder, 1,0 s og 0,9 s.
- PowerPoint: maks avvikende piksler per lysbilde ved toleranse 0: 0; maks kanalavvik 0.
- Word-rendering (PDF → PNG via Windows.Data.Pdf): 2 sider, 5,8 s og 5,8 s.
- Word: maks avvikende piksler per side ved toleranse 0: 0; maks kanalavvik 0.

## P1-alt-pptx: Alternativ tekst i PPTX uten andre endringer

**Status:** bekreftet – Bare descr-attributtet ble endret; PowerPoint leser den nye teksten; ingen visuelle avvik.

- Endring SetAltText /ppt/slides/slide2.xml#nv:5: Applied
- Endring SetAltText /ppt/slides/slide7.xml#nv:4: Applied
- Urørte deler med identiske bytes: 67 av 67 (verifisert: ja).
- Endret del /ppt/slides/slide2.xml: AttributeAdded /p:sld[1]/p:cSld[1]/p:spTree[1]/p:pic[1]/p:nvPicPr[1]/p:cNvPr[1]/@descr «» → «Planteselle med cellevegg ytterst og cellekjerne i midten.»
- Endret del /ppt/slides/slide7.xml: AttributeChanged /p:sld[1]/p:cSld[1]/p:spTree[1]/p:pic[1]/p:nvPicPr[1]/p:cNvPr[1]/@descr «tekst, datamaskin, tegnefilm, innendørs» → «Skjermbilde av et program der knappen «Lagre» er uthevet med rød ramme.»
- Open XML-validering: 0 feil før, 0 etter, 0 nye.
- PowerPoint 16.0 (build 20527) åpnet original og resultat uten feil (8 / 8 lysbilder).
- Visuell sammenligning (toleranse 0): maks 0 avvikende piksler per lysbilde.
- PowerPoint leser AlternativeText: «Planteselle med cellevegg ytterst og cellekjerne i midten.» og «Skjermbilde av et program der knappen «Lagre» er uthevet med rød ramme.».

## P1-alt-docx: Alternativ tekst i DOCX uten andre endringer

**Status:** bekreftet – Bare descr på wp:docPr endret; Word leser den nye teksten; ingen visuelle avvik.

- Endring SetAltText /word/document.xml#docPr:1074318837:0: Applied
- Urørte deler med identiske bytes: 27 av 27 (verifisert: ja).
- Endret del /word/document.xml: AttributeAdded /w:document[1]/w:body[1]/w:p[16]/w:r[1]/w:drawing[1]/wp:inline[1]/wp:docPr[1]/@descr «» → «Planteselle med cellevegg ytterst og cellekjerne i midten.»
- Open XML-validering: 0 feil før, 0 etter, 0 nye.
- Word 16.0 (build 16.0.20527) åpnet original og resultat uten feil (2 / 2 sider).
- Visuell sammenligning (toleranse 0): maks 0 avvikende piksler per side.
- Word leser InlineShape.AlternativeText: [Planteselle med cellevegg ytterst og cellekjerne i midten. | graf]

## P2-ukjente-utvidelser: Ukjente XML-utvidelser og urørte pakkedeler beholdes

**Status:** bekreftet – Docx: alt bevart.

- Endring SetAltText /ppt/slides/slide2.xml#nv:5: Applied
- Urørte deler med identiske bytes: 69 av 69 (verifisert: ja).
- Endret del /ppt/slides/slide2.xml: AttributeAdded /p:sld[1]/p:cSld[1]/p:spTree[1]/p:pic[1]/p:nvPicPr[1]/p:cNvPr[1]/@descr «» → «Planteselle.»
- Open XML-validering: 0 feil før, 0 etter, 0 nye.
- PowerPoint 16.0 (build 20527) åpnet original og resultat uten feil (8 / 8 lysbilder).
- Visuell sammenligning (toleranse 0): maks 0 avvikende piksler per lysbilde.
- Pptx: ukjent attributt ja, ukjent element ja, ukjent extLst ja, ukjent del byte-lik ja, ukjent relasjon ja.
- Endring SetAltText /word/document.xml#docPr:1074318837:0: Applied
- Urørte deler med identiske bytes: 28 av 28 (verifisert: ja).
- Endret del /word/document.xml: AttributeAdded /w:document[1]/w:body[1]/w:p[16]/w:r[1]/w:drawing[1]/wp:inline[1]/wp:docPr[1]/@descr «» → «Planteselle.»
- Open XML-validering: 0 feil før, 0 etter, 0 nye.
- Word 16.0 (build 16.0.20527) åpnet original og resultat uten feil (2 / 2 sider).
- Visuell sammenligning (toleranse 0): maks 0 avvikende piksler per side.
- Docx: ukjent attributt ja, ukjent element ja, ukjent extLst ja, ukjent del byte-lik ja, ukjent relasjon ja.

## P3-P4-leserekkefolge: Leserekkefølge og lagrekkefølge i PowerPoint

**Status:** bekreftet – Leserekkefølge = rekkefølge i figurtreet = lagrekkefølge. Omordning av overlappende figurer endrer utseendet; ikke-overlappende gjør det ikke.

- Ingen egen leserekkefølge-representasjon (søk etter «readingOrder») i filen PowerPoint lagret. Rekkefølgen i p:spTree er eneste rekkefølge.
- Original rekkefølge (ZOrderPosition 1→n): Title 1 → Boks A → Boks B → Gruppe dyr → Dekorlinje
- Etter bytte av overlappende «Boks A»/«Boks B»: Title 1 → Boks B → Boks A → Gruppe dyr → Dekorlinje
- Pikselavvik etter bytte av overlappende figurer: 580 piksler (0,063 %).
- Etter bytte av naboene «Gruppe dyr»/«Dekorlinje» (overlapper ikke): Title 1 → Boks A → Boks B → Dekorlinje → Gruppe dyr
- Pikselavvik etter bytte av ikke-overlappende naboer: 0 piksler.
- Animert figur før/etter: Boks B / Boks B (p:timing peker på cNvPr-id, ikke posisjon).

## P5-skjult-tittel: Lysbildetittel uten synlig endring (KI-bilde med tekst)

**Status:** bekreftet – PowerPoint gjenkjenner tittelplassholderen (HasTitle) uten synlig endring. Skjermleseropplevelse må bekreftes manuelt (NVDA-protokoll).

- Endring AddHiddenSlideTitle /ppt/slides/slide3.xml#part: Applied
- Endring AddHiddenSlideTitle /ppt/slides/slide8.xml#part: Applied
- Urørte deler med identiske bytes: 67 av 67 (verifisert: ja).
- Endret del /ppt/slides/slide3.xml: ElementAdded /p:sld[1]/p:cSld[1]/p:spTree[1]/p:sp[1] «» → «{http://schemas.openxmlformats.org/presentationml/2006/main}sp»
- Endret del /ppt/slides/slide8.xml: ElementAdded /p:sld[1]/p:cSld[1]/p:spTree[1]/p:sp[1] «» → «{http://schemas.openxmlformats.org/presentationml/2006/main}sp»
- Open XML-validering: 0 feil før, 0 etter, 0 nye.
- PowerPoint 16.0 (build 20527) åpnet original og resultat uten feil (8 / 8 lysbilder).
- Visuell sammenligning (toleranse 0): maks 0 avvikende piksler per lysbilde.
- Lysbilde 3: HasTitle=true, tittel «Fotosyntese», Top=-74.7 pt, ZOrder=1.
- Lysbilde 8: HasTitle=true, tittel «Vannets kretsløp», Top=-74.7 pt, ZOrder=1.

## P6-overskrifter-word: Overskriftsnivå i Word uten endret layout

**Status:** bekreftet – w:outlineLvl gir navigasjonsnivå 1 og 2 uten layoutendring. Om skjermleser kunngjør det som overskrift må testes manuelt.

- Endring SetOutlineLevel /word/document.xml#p:0: Applied
- Endring SetOutlineLevel /word/document.xml#p:5: Applied
- Urørte deler med identiske bytes: 27 av 27 (verifisert: ja).
- Endret del /word/document.xml: ElementAdded /w:document[1]/w:body[1]/w:p[1]/w:pPr[1]/w:outlineLvl[1] «» → «{http://schemas.openxmlformats.org/wordprocessingml/2006/main}outlineLvl»; ElementAdded /w:document[1]/w:body[1]/w:p[5]/w:pPr[1]/w:outlineLvl[1] «» → «{http://schemas.openxmlformats.org/wordprocessingml/2006/main}outlineLvl»
- Open XML-validering: 0 feil før, 0 etter, 0 nye.
- Word 16.0 (build 16.0.20527) åpnet original og resultat uten feil (2 / 2 sider).
- Visuell sammenligning (toleranse 0): maks 0 avvikende piksler per side.
- Word OutlineLevel endret (10 = brødtekst): avsnitt 1 (Normal): 10 → 1; avsnitt 5 (Normal): 10 → 2
- Sider før/etter: 2 / 2. Sidebrudd-signatur (sidetall per avsnitt) lik: ja.

## P6b-docx-uten-metadata: Tittel (ny core.xml), språk og alt-tekst i SDK-fil

**Status:** bekreftet – Ny kjerneegenskapsdel med relasjon og innholdstype; Word åpner filen og viser den likt.

- Endring SetDocumentTitle /docProps/core.xml#part: Applied
- Endring SetDocumentLanguage /word/styles.xml#part: Applied
- Endring SetAltText /word/document.xml#docPr:1:0: Applied
- Endring SetAltText /word/document.xml#docPr:1:1: Applied
- Endring SetAltText /word/document.xml#vml:0: Applied
- Urørte deler med identiske bytes: 3 av 3 (verifisert: ja).
- Endret del /docProps/core.xml (ny): 
- Endret del /_rels/.rels: ElementAdded Relationship (core-properties)
- Endret del /[Content_Types].xml: ElementAdded Override (core-properties)
- Endret del /word/styles.xml: ElementAdded /w:styles[1]/w:docDefaults[1]/w:rPrDefault[1]/w:rPr[1]/w:lang[1] «» → «{http://schemas.openxmlformats.org/wordprocessingml/2006/main}lang»
- Endret del /word/document.xml: AttributeAdded /w:document[1]/w:body[1]/w:p[9]/w:r[1]/w:drawing[1]/wp:inline[1]/wp:docPr[1]/@descr «» → «Planteselle med cellevegg og cellekjerne.»; AttributeChanged /w:document[1]/w:body[1]/w:p[11]/w:r[1]/w:drawing[1]/wp:inline[1]/wp:docPr[1]/@descr «graf» → «Søylediagram: antall elever per trinn.»; AttributeAdded /w:document[1]/w:body[1]/w:p[12]/w:r[1]/w:pict[1]/v:shape[1]/@alt «» → «Planteselle (eldre bildeformat).»
- Open XML-validering: 1 feil før, 1 etter, 0 nye.
- Word 16.0 (build 16.0.20527) åpnet original og resultat uten feil (2 / 2 sider).
- Visuell sammenligning (toleranse 0): maks 0 avvikende piksler per side.

## P8-ocr: Lokal OCR av KI-bilde med tekst (Windows.Media.Ocr)

**Status:** bekreftet – Se observasjoner for kvalitet.

- ki-tittelbilde.png: språk nb; linjer: «Fotosyntese» (h=140) | «Slik lager plantene næring av sollys» (h=63). Tilgjengelige OCR-språk: nb
- ki-tittelbilde-skjev.png: språk nb; linjer: «Vannets kretsløp» (h=146) | «Fordamping, kondensasjon og nedbør» (h=63). Tilgjengelige OCR-språk: nb
- lite-uskarpt.png: språk nb; linjer: . Tilgjengelige OCR-språk: nb


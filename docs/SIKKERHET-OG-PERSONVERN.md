# Sikkerhet og personvern

| Trussel | Tiltak | Test |
|---|---|---|
| Andre maskiner når tjenesten | Kestrel lytter bare på `127.0.0.1` | Konfigurasjon (`Program.cs`) |
| DNS-rebinding | `Host` må være `127.0.0.1:<port>` eller `localhost:<port>` (ellers 421) | `WebApiTests.Fremmed_origin_og_host_avvises` |
| Andre nettsteder (CSRF) | `Origin`-kontroll, `Sec-Fetch-Site`, CSRF-token som HttpOnly + SameSite=Strict-informasjonskapsel og header | `Endringsforesporsler_krever_CSRF_token` |
| Skript-/HTML-injeksjon | React-escaping, CSP `script-src 'self'`; rapporten escapes og serveres med `sandbox`-CSP uten skript | `Rapport_escaper_innhold…`, `Sikkerhetsheadere_settes` |
| Filendelse lurer appen | Typen bestemmes av innhold (signatur, innholdstyper) | `Gyldige_filer_godtas_og_type_bestemmes_av_innhold`, `tekst-forkledd` |
| ZIP-bombe, for mange deler | Grenser for filstørrelse (500 MB), utpakket størrelse, antall deler, komprimeringsgrad; lesing stopper ved grensen | `PackageSecurityTests` |
| Stitraversering (zip-slip) | Navn med `..`, `/`-start, `:`, `\` avvises; ingenting pakkes ut til disk | `Stitraversering_i_zip_avvises` |
| XXE | Alle XML-deler lastes med DTD forbudt og uten resolver | `Xml_med_ekstern_entitet_lastes_aldri` |
| Eksterne relasjoner | Registreres, hentes aldri | `Ekstern_relasjon_registreres_men_hentes_ikke` |
| Makroer/innebygde objekter | Makroformater avvises; Office åpnes med `AutomationSecurity = ForceDisable`; OLE kjøres aldri | Inntakstester |
| Hengende konvertering | Hjelperen kjøres som egen prosess med tidsgrense; prosesstre og egen Word-prosess drepes | `OfficeRenderer` |
| Brukerens Office-vinduer | Programmet avsluttes bare når hjelperen selv startet prosessen | `OfficeSession` |
| Stiangrep i API | Jobb-ID og bildenøkler valideres strengt (heksadesimal) | `Stiangrep_i_jobb_og_bildeadresser_avvises` |
| KI-styring via innhold | Dokumenttekst merkes som data; skjemaet tillater bare innholdsfelt; ukjente felt avvises | `Ukjente_felt_og_feil_objekt_avvises` |
| Oppdiktede opplysninger | Tall og navn som ikke finnes i kildene gir gjennomgang og brukes aldri automatisk | `Tall_og_navn…`, `KI_forslag_brukes_bare…` |
| Utilsiktet nettverkstrafikk | Ingen HTTP i lokal modus; lokal modell bare på loopback; ingen automatisk skyreserve | `Ingen_nettverkstrafikk_i_lokal_modus_uten_KI`, `Lokal_modell_maa_vaere_paa_denne_maskinen` |
| Lekkasje av nøkler | Windows Legitimasjonsbehandling; aldri i filer, logger, database eller svar | `Innstillinger_avviser…_og_nokkel_lekker_ikke` |
| Dokumentbeskyttelse | Beskyttede filer endres aldri | `Dokumentbeskyttelse_omgas_aldri` |

## Personvern

- Originalfilen lagres som skrivebeskyttet kopi i en isolert jobbmappe. Den opplastede filen på din maskin røres aldri.
- Arbeidsfiler slettes automatisk etter oppbevaringstiden (standard 7 dager, sjekkes hver time) og kan slettes når som helst.
- Logger inneholder bare feiltyper og jobb-ID-er, aldri dokumentinnhold eller filnavn.
- Ved skytjeneste sendes bare bildet slik det vises (eventuelt original), og kort kontekst (overskrift, bildetekst,
  nærliggende tekst, eksisterende beskrivelse, OCR-tekst, diagramdata, QR-innhold). Filnavn og resten av dokumentet sendes ikke.
  Brukeren må godkjenne dette i innstillingene, og velge skytjeneste per jobb.

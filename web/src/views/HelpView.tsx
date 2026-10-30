export function HelpView() {
  return (
    <div className="prose">
      <h1>Hjelp</h1>
      <h2>Hva gjør appen?</h2>
      <p>
        Appen leser Word- og PowerPoint-filer, finner vanlige tilgjengelighetsproblemer og lager en forbedret kopi. I standardmodus
        «Bevar utseendet» gjør den bare endringer som ikke synes: beskrivelser av bilder, dekorativ-markering, lysbildetitler utenfor
        lysbildet, dokumenttittel, språk og overskriftsnivåer. Alle endringer kontrolleres, og endringer som gir avvik forkastes.
      </p>
      <h2>Bilder med tekst i (for eksempel KI-genererte lysbildebilder)</h2>
      <p>
        Hvis et lysbilde mangler tittel og har et stort bilde med en tydelig tittel i, leser appen teksten lokalt og legger den inn som
        lysbildetittel utenfor lysbildet. Da finner skjermleserbrukere tittelen, uten at lysbildet ser annerledes ut. Resten av teksten
        i bildet tas med i bildets beskrivelse, uten å gjenta tittelen. Er lesingen usikker, blir det et forslag du må godkjenne.
      </p>
      <h2>Hva appen ikke kan</h2>
      <ul>
        <li>Den kan ikke garantere at et dokument oppfyller WCAG eller er «universelt utformet».</li>
        <li>Svak kontrast og uklare lenketekster krever synlige endringer. Det løses ikke med skjulte beskrivelser.</li>
        <li>Teksting av video og lyd kontrolleres ikke.</li>
        <li>KI-forslag kan være feil. Usikre forslag merkes, og du bestemmer.</li>
      </ul>
      <h2>Tastatur</h2>
      <p>Alt kan brukes med tastatur: Tab flytter mellom felt og knapper, mellomrom velger, og Enter sender skjemaer.</p>
      <h2>Personvern</h2>
      <p>
        Filene lagres i en lokal jobbmappe og slettes automatisk etter antall dager valgt i innstillingene. Du kan også slette en jobb
        selv. Ingen filer sendes ut med mindre du velger skytjeneste for en jobb.
      </p>
    </div>
  );
}

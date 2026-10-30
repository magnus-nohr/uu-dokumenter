import { useId, useRef, useState } from "react";
import { api, ApiError, type Session } from "../api";
import { navigate } from "../App";

const ACCEPT = ".docx,.pptx,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/vnd.openxmlformats-officedocument.presentationml.presentation";

export function StartView({ session }: { session: Session }) {
  const [files, setFiles] = useState<File[]>([]);
  const [mode, setMode] = useState("PreserveAppearance");
  const [ai, setAi] = useState(session.ai.localConfigured ? "local" : "none");
  const [aiMode, setAiMode] = useState("Automatic");
  const [allowSig, setAllowSig] = useState(false);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [drag, setDrag] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const fileHint = useId();

  const addFiles = (list: FileList | null) => {
    if (!list) return;
    const next = [...files];
    const bad: string[] = [];
    for (const f of Array.from(list)) {
      if (!/\.(docx|pptx)$/i.test(f.name)) bad.push(`${f.name}: bare .docx og .pptx støttes.`);
      else if (f.size > session.maxUploadMb * 1024 * 1024) bad.push(`${f.name}: filen er større enn ${session.maxUploadMb} MB.`);
      else if (!next.some((x) => x.name === f.name && x.size === f.size)) next.push(f);
    }
    setFiles(next);
    setErrors(bad);
    setMsg(`${next.length} ${next.length === 1 ? "fil" : "filer"} valgt.`);
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (files.length === 0) {
      setErrors(["Velg minst én Word- eller PowerPoint-fil."]);
      inputRef.current?.focus();
      return;
    }
    setBusy(true);
    setErrors([]);
    setMsg("Sender filene …");
    try {
      const res = await api.upload(files, mode, aiMode, ai, allowSig);
      const ok = res.jobs.filter((j) => j.id).map((j) => j.id!);
      const bad = res.jobs.filter((j) => j.error).map((j) => `${j.name}: ${j.error}`);
      setErrors(bad);
      if (ok.length > 0) navigate(`/jobber/${ok.join(",")}`);
      else setMsg(null);
    } catch (err) {
      setErrors([err instanceof ApiError ? err.message : "Kunne ikke sende filene."]);
      setMsg(null);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate aria-describedby="intro">
      <h1>Forbedre Word- og PowerPoint-filer</h1>
      <p id="intro">
        Appen legger til beskrivelser av bilder, lysbildetitler og annen struktur som hjelper skjermleserbrukere. Originalfilen endres aldri;
        du får en forbedret kopi og en rapport.
      </p>

      {errors.length > 0 && (
        <div className="alert error" role="alert">
          <h2>Noe må rettes</h2>
          <ul>{errors.map((x) => <li key={x}>{x}</li>)}</ul>
        </div>
      )}

      <section aria-labelledby="steg1">
        <h2 id="steg1">1. Velg filer</h2>
        <div
          className={`dropzone${drag ? " drag" : ""}`}
          onDragOver={(e) => { e.preventDefault(); setDrag(true); }}
          onDragLeave={() => setDrag(false)}
          onDrop={(e) => { e.preventDefault(); setDrag(false); addFiles(e.dataTransfer.files); }}
        >
          <label htmlFor="filer" className="button">Velg filer</label>
          <input id="filer" ref={inputRef} className="visually-hidden-input" type="file" multiple accept={ACCEPT}
            aria-describedby={fileHint} onChange={(e) => { addFiles(e.target.files); e.target.value = ""; }} />
          <p id={fileHint}>eller dra og slipp filer hit. Word (.docx) og PowerPoint (.pptx), opptil {session.maxUploadMb} MB per fil.</p>
        </div>
        {files.length > 0 && (
          <ul className="filelist" aria-label="Valgte filer">
            {files.map((f) => (
              <li key={f.name + f.size}>
                <span>{f.name} ({(f.size / 1024 / 1024).toFixed(1)} MB)</span>
                <button type="button" className="link" onClick={() => setFiles(files.filter((x) => x !== f))}>
                  Fjern <span className="visually-hidden">{f.name}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <fieldset>
        <legend><h2>2. Hvordan skal filene behandles?</h2></legend>
        <div className="choice">
          <input type="radio" id="m1" aria-describedby="m1h" name="mode" value="PreserveAppearance" checked={mode === "PreserveAppearance"} onChange={(e) => setMode(e.target.value)} />
          <label htmlFor="m1"><strong>Bevar utseendet</strong> (anbefalt)</label><br />
            <span className="hint" id="m1h">Bare usynlige forbedringer som beskrivelser, titler og struktur. Tekst, farger og oppsett endres ikke.</span>
        </div>
        <div className="choice">
          <input type="radio" id="m2" aria-describedby="m2h" name="mode" value="AllowVisibleChanges" checked={mode === "AllowVisibleChanges"} onChange={(e) => setMode(e.target.value)} />
          <label htmlFor="m2"><strong>Tillat synlige forbedringer</strong></label><br />
            <span className="hint" id="m2h">Du får i tillegg velge synlige endringer, for eksempel bedre kontrast og tydeligere lenketekst. De lagres i en egen fil.</span>
        </div>
      </fieldset>

      <fieldset>
        <legend><h2>3. Beskrivelser av bilder</h2></legend>
        <div className="choice">
          <input type="radio" id="a0" aria-describedby="a0h" name="ai" value="none" checked={ai === "none"} onChange={(e) => setAi(e.target.value)} />
          <label htmlFor="a0"><strong>Uten KI</strong></label><br /><span className="hint" id="a0h">Analyse og sikre reparasjoner. Beskrivelser lages bare fra dokumentets egne data og tekst i bilder.</span>
        </div>
        <div className="choice">
          <input type="radio" id="a1" name="ai" value="local" checked={ai === "local"} disabled={!session.ai.localConfigured} onChange={(e) => setAi(e.target.value)}
            aria-describedby="a1h" />
          <label htmlFor="a1"><strong>Lokal KI-modell</strong>{session.ai.localModel ? ` (${session.ai.localModel})` : ""}</label><br />
            <span className="hint" id="a1h">{session.ai.localConfigured ? "Bildene behandles på denne maskinen." : "Ikke satt opp. Se Innstillinger."}</span>
        </div>
        <div className="choice">
          <input type="radio" id="a2" name="ai" value="cloud" checked={ai === "cloud"} disabled={!session.ai.cloudReady} onChange={(e) => setAi(e.target.value)}
            aria-describedby="a2h" />
          <label htmlFor="a2"><strong>Skytjeneste</strong> ({session.ai.cloudModel})</label><br />
            <span className="hint" id="a2h">{session.ai.cloudReady ? "Bilder og kort tekst rundt dem sendes til leverandøren. Du har godkjent dette i Innstillinger." : "Ikke godkjent. Se Innstillinger."}</span>
        </div>
        {ai !== "none" && (
          <div className="sub">
            <label htmlFor="aimode">Når forslagene er usikre:</label>
            <select id="aimode" value={aiMode} onChange={(e) => setAiMode(e.target.value)}>
              <option value="Automatic">Automatisk – bruk sikre forslag, vis resten etterpå</option>
              <option value="Review">Gjennomgå forslag – jeg vurderer de usikre selv</option>
              <option value="BestEffort">Gjør det beste du kan – bruk flere forslag, men merk usikkerhet</option>
            </select>
          </div>
        )}
      </fieldset>

      <details>
        <summary>Flere valg</summary>
        <div className="choice">
          <input type="checkbox" id="sig" checked={allowSig} onChange={(e) => setAllowSig(e.target.checked)} />
          <label htmlFor="sig">Endre digitalt signerte filer (signaturen blir ugyldig)</label>
        </div>
      </details>

      <p className="status" role="status" aria-live="polite">{msg}</p>
      <button type="submit" className="primary" disabled={busy}>{busy ? "Sender …" : "Forbedre filene"}</button>
      {!session.rendering.available && (
        <p className="hint">Word/PowerPoint er ikke tilgjengelig for visuell kontroll på denne maskinen. Rapporten vil si «Visuell bevaring er ikke kontrollert i Office».</p>
      )}
    </form>
  );
}

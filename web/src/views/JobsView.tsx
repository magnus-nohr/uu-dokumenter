import { useEffect, useRef, useState } from "react";
import { api, ApiError, isFinished, type Job, type JobDetails } from "../api";

const statusText: Record<Job["status"], string> = {
  queued: "I kø",
  running: "Behandles",
  reviewing: "Bruker valgene dine",
  done: "Ferdig",
  failed: "Feilet",
  rejected: "Avvist",
  cancelled: "Avbrutt",
};

export function JobsView({ ids }: { ids?: string[] }) {
  const [jobs, setJobs] = useState<JobDetails[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [announce, setAnnounce] = useState("");
  const lastStage = useRef(new Map<string, string>());
  const [round, setRound] = useState(0);   // øker for å starte statusoppdatering på nytt etter «Bruk alle forslag»

  useEffect(() => {
    let stop = false;
    let timer: number | undefined;
    const tick = async () => {
      try {
        const list = ids ?? (await api.jobs()).map((j) => j.id);
        const details = await Promise.all(list.map((id) => api.job(id).catch(() => null)));
        if (stop) return;
        const ok = details.filter((d): d is JobDetails => d !== null);
        setJobs(ok);
        setError(null);
        // Kunngjør bare endringer i trinn, ikke hver prosent (unngår overdreven opplesning).
        const msgs: string[] = [];
        for (const d of ok) {
          const key = `${d.job.status}:${d.job.stage}`;
          if (lastStage.current.get(d.job.id) !== key) {
            if (lastStage.current.has(d.job.id) || ids) msgs.push(`${d.job.name}: ${d.job.status === "done" ? "ferdig" : d.job.stage ?? statusText[d.job.status]}`);
            lastStage.current.set(d.job.id, key);
          }
        }
        if (msgs.length) setAnnounce(msgs.join(". "));
        if (ok.some((d) => !isFinished(d.job.status))) timer = window.setTimeout(tick, 1000);
      } catch (e) {
        setError(e instanceof ApiError ? e.message : "Kunne ikke hente status.");
        timer = window.setTimeout(tick, 3000);
      }
    };
    void tick();
    return () => { stop = true; window.clearTimeout(timer); };
  }, [ids, round]);

  const acceptAll = async (id: string, name: string) => {
    try {
      const r = await api.acceptAll(id);
      setAnnounce(`${r.count} forslag brukes i ${name}. Filen lages på nytt.`);
      setRound(round + 1);
    } catch (e) { setError(e instanceof ApiError ? e.message : "Kunne ikke bruke forslagene."); }
  };

  const cancel = async (id: string) => {
    try { await api.cancel(id); } catch (e) { setError(e instanceof ApiError ? e.message : "Kunne ikke avbryte."); }
  };
  const remove = async (id: string) => {
    if (!window.confirm("Slette jobben og alle arbeidsfiler? Originalfilen på maskinen din påvirkes ikke.")) return;
    try { await api.remove(id); setJobs(jobs.filter((j) => j.job.id !== id)); setAnnounce("Jobben er slettet."); }
    catch (e) { setError(e instanceof ApiError ? e.message : "Kunne ikke slette."); }
  };

  return (
    <div>
      <h1>{ids ? "Behandling" : "Mine jobber"}</h1>
      <p className="visually-hidden" role="status" aria-live="polite">{announce}</p>
      {error && <div className="alert error" role="alert">{error}</div>}
      {jobs.length === 0 && !error && <p>Ingen jobber ennå. <a href="#/">Velg filer</a> for å starte.</p>}
      {jobs.map(({ job, summary }) => (
        <article key={job.id} className="card" aria-labelledby={`t-${job.id}`}>
          <h2 id={`t-${job.id}`}>{job.name}</h2>
          <p>
            <span className={`badge ${job.status}`}>
              <span aria-hidden="true">{job.status === "done" ? "✓ " : job.status === "failed" || job.status === "rejected" ? "✕ " : "… "}</span>
              {statusText[job.status]}
            </span>
            {!isFinished(job.status) && job.stage && <> – {job.stage}{job.detail ? ` (${job.detail})` : ""}</>}
          </p>
          {!isFinished(job.status) && (
            <>
              <progress max={100} value={job.percent} aria-label={`Fremdrift for ${job.name}`} aria-valuetext={`${job.percent} prosent, ${job.stage ?? ""}`} />
              <p><button type="button" onClick={() => void cancel(job.id)}>Avbryt <span className="visually-hidden">{job.name}</span></button></p>
            </>
          )}
          {job.error && <div className="alert error"><p>{job.error}</p></div>}
          {job.status === "done" && summary && (
            <>
              {summary.pendingReview > 0 && (
                <div className="callout">
                  <p>
                    <strong>{summary.pendingReview} forslag venter.</strong> Du kan bruke alle med ett klikk, eller gå gjennom dem. Forslag som
                    brukes uten gjennomgang, merkes som ikke kontrollert i rapporten.
                  </p>
                  <div className="actions">
                    <button type="button" className="primary" onClick={() => void acceptAll(job.id, job.name)}>
                      Bruk alle forslag ({summary.pendingReview})<span className="visually-hidden"> i {job.name}</span>
                    </button>
                    <a className="button" href={`#/jobb/${job.id}/gjennomga`}>Gå gjennom forslagene</a>
                  </div>
                </div>
              )}
              <h3>Resultat</h3>
              <ul>{summary.sentences.map((s) => <li key={s}>{s}</li>)}</ul>
              <div className="actions">
                <a className="button primary" href={`/api/jobs/${job.id}/download`} download>
                  Last ned forbedret fil <span className="visually-hidden">{job.name}</span>
                </a>
                {summary.hasVisibleVariant && (
                  <a className="button" href={`/api/jobs/${job.id}/download?variant=visible`} download>Last ned variant med synlige forbedringer</a>
                )}
                <a className="button" href={`/api/jobs/${job.id}/report.html?download=true`} download>Last ned rapport</a>
                <a href={`/api/jobs/${job.id}/report.html`} target="_blank" rel="noopener">Åpne rapporten (ny fane)</a>
                <a href={`/api/jobs/${job.id}/report.json?download=true`} download>Rapport som JSON</a>
              </div>
              <p>
                <a className="button" href={`#/jobb/${job.id}/gjennomga`}>
                  {summary.pendingReview > 0 ? `Gjennomgå ${summary.pendingReview} forslag` : "Se og juster endringene"}
                </a>
              </p>
            </>
          )}
          {isFinished(job.status) && (
            <p><button type="button" className="link" onClick={() => void remove(job.id)}>Slett jobben <span className="visually-hidden">{job.name}</span></button></p>
          )}
        </article>
      ))}
    </div>
  );
}

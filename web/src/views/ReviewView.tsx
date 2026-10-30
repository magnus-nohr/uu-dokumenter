import { useEffect, useMemo, useState } from "react";
import { api, ApiError, type Decision, type ReviewItem } from "../api";
import { navigate } from "../App";

type Choice = { action: Decision["action"]; value?: string };

const labels: Record<string, string> = {
  location: "Sted", slideTitle: "Lysbildetittel", caption: "Bildetekst", context: "Tekst rundt", ocr: "Tekst lest fra bildet",
  role: "Bildets funksjon", evidence: "Grunnlag", provider: "Laget av", text: "Tekst", target: "Lenkemål", from: "Nåværende farge",
  to: "Foreslått farge", bg: "Bakgrunn", level: "Nivå", source: "Kilde", aiError: "KI-feil",
};
const roleText: Record<string, string> = {
  informative: "Informativ illustrasjon", decorative: "Dekorasjon", chart: "Diagram eller graf", screenshot: "Skjermbilde",
  textImage: "Tekstbilde", map: "Kart", formula: "Formel", qrCode: "QR-kode", composite: "Sammensatt figur", unknown: "Usikker",
};

export function ReviewView({ id }: { id: string }) {
  const [items, setItems] = useState<ReviewItem[] | null>(null);
  const [choices, setChoices] = useState<Record<string, Choice>>({});
  const [editing, setEditing] = useState<Record<string, string>>({});
  const [filter, setFilter] = useState<"pending" | "all">("pending");
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.review(id).then((r) => setItems(r.items)).catch((e) => setError(e instanceof ApiError ? e.message : "Kunne ikke hente forslag."));
  }, [id]);

  const shown = useMemo(() => (items ?? []).filter((i) => !i.discarded && (filter === "all" || i.pending)), [items, filter]);
  const groups = useMemo(() => {
    const m = new Map<string, ReviewItem[]>();
    for (const i of shown) m.set(i.group, [...(m.get(i.group) ?? []), i]);
    return [...m.entries()];
  }, [shown]);

  const choose = (item: ReviewItem, c: Choice) => {
    setChoices({ ...choices, [item.id]: c });
    const word = { accept: "godtatt", edit: "redigert", keep: "original beholdes", skip: "hoppet over", undo: "angret" }[c.action];
    setStatus(`${item.kindText}: ${word}.`);
  };

  const acceptGroup = (list: ReviewItem[]) => {
    const next = { ...choices };
    let n = 0;
    for (const i of list) if (i.pending && i.value.length > 0 && !next[i.id]) { next[i.id] = { action: "accept" }; n++; }
    setChoices(next);
    setStatus(`${n} forslag godtatt. Hvert forslag beholder sin egen tekst.`);
  };

  const submit = async () => {
    const decisions = Object.entries(choices).map(([changeId, c]) => ({ changeId, action: c.action, value: c.value }));
    if (decisions.length === 0) { setError("Du har ikke gjort noen valg ennå."); return; }
    setBusy(true);
    setError(null);
    try {
      await api.submitReview(id, decisions);
      navigate(`/jobber/${id}`);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Kunne ikke lagre valgene.");
    } finally {
      setBusy(false);
    }
  };

  if (error && !items) return <><h1>Gjennomgå forslag</h1><div className="alert error" role="alert">{error}</div></>;
  if (!items) return <><h1>Gjennomgå forslag</h1><p>Laster …</p></>;
  const pending = items.filter((i) => i.pending).length;

  return (
    <div>
      <h1>Gjennomgå forslag</h1>
      <p>
        {pending > 0 ? `${pending} forslag venter på deg.` : "Ingen forslag venter."} Du kan godta, redigere, beholde originalen eller hoppe over.
        Når du er ferdig, lagres valgene og filen kontrolleres på nytt.
      </p>
      <fieldset className="inline">
        <legend>Vis</legend>
        <input type="radio" id="f1" name="filter" checked={filter === "pending"} onChange={() => setFilter("pending")} />
        <label htmlFor="f1">Bare forslag som venter ({pending})</label>
        <input type="radio" id="f2" name="filter" checked={filter === "all"} onChange={() => setFilter("all")} />
        <label htmlFor="f2">Alle endringer ({items.filter((i) => !i.discarded).length})</label>
      </fieldset>
      <p className="visually-hidden" role="status" aria-live="polite">{status}</p>
      {error && <div className="alert error" role="alert">{error}</div>}

      {groups.map(([group, list]) => (
        <section key={group} aria-labelledby={`g-${group}`} className="group">
          <h2 id={`g-${group}`}>{group} ({list.length})</h2>
          {list.some((i) => i.pending && i.value) && (
            <p><button type="button" onClick={() => acceptGroup(list)}>Godta alle forslag i «{group}»</button></p>
          )}
          {list.map((item) => {
            const c = choices[item.id];
            const isEditing = editing[item.id] !== undefined;
            const fieldId = `e-${item.id}`;
            const ctx = `${item.kindText}, ${item.display.location ?? item.id}`;
            return (
              <article key={item.id} className="card review" aria-labelledby={`h-${item.id}`}>
                <h3 id={`h-${item.id}`}>{item.kindText}: {item.display.location ?? item.id}</h3>
                <div className="review-grid">
                  {item.image && (
                    <figure>
                      {/* Bildet vises for seende brukere; teksten ved siden av er den tilgjengelige beskrivelsen av forslaget. */}
                      <img src={item.image} alt="" />
                      <figcaption>Bildet slik det vises i dokumentet</figcaption>
                    </figure>
                  )}
                  <div>
                    <dl>
                      {item.oldValue != null && item.oldValue !== "" && (<><dt>Nåværende</dt><dd>{item.oldValue}</dd></>)}
                      <dt>{item.pending ? "Forslag" : "Brukt i filen"}</dt>
                      <dd>{item.value || <em>Ingen forslag – skriv selv</em>}</dd>
                      {Object.entries(item.display).filter(([k]) => labels[k] && k !== "location").map(([k, v]) => (
                        <div key={k}><dt>{labels[k]}</dt><dd>{k === "role" ? roleText[v] ?? v : v}</dd></div>
                      ))}
                      {item.display.longDescription && (<><dt>Utkast til lang beskrivelse</dt><dd>{item.display.longDescription}</dd></>)}
                      {item.uncertainty.length > 0 && (<><dt>Hvorfor usikkert</dt><dd><ul>{item.uncertainty.map((u) => <li key={u}>{u}</li>)}</ul></dd></>)}
                      {item.reason && (<><dt>Begrunnelse</dt><dd>{item.reason}</dd></>)}
                      <dt>Status</dt><dd>{c ? `Ditt valg: ${{ accept: "godta", edit: "redigert", keep: "behold original", skip: "hopp over", undo: "angre" }[c.action]}` : item.status}</dd>
                    </dl>
                    {isEditing && (
                      <div className="edit">
                        <label htmlFor={fieldId}>{item.kind === "SetTextColor" ? "Ny farge (for eksempel #333333)" : "Din tekst"}</label>
                        <textarea id={fieldId} rows={3} value={editing[item.id]} onChange={(e) => setEditing({ ...editing, [item.id]: e.target.value })} />
                        <button type="button" onClick={() => {
                          const v = editing[item.id].trim();
                          if (!v) { setError("Teksten kan ikke være tom."); return; }
                          choose(item, { action: "edit", value: v });
                          const { [item.id]: _, ...rest } = editing; setEditing(rest);
                        }}>Bruk teksten</button>
                        <button type="button" className="link" onClick={() => { const { [item.id]: _, ...rest } = editing; setEditing(rest); }}>Avbryt redigering</button>
                      </div>
                    )}
                    <div className="actions" role="group" aria-label={`Valg for ${item.kindText}`}>
                      {item.pending && item.value && <button type="button" onClick={() => choose(item, { action: "accept" })}>Godta<span className="visually-hidden"> – {ctx}</span></button>}
                      {item.editable && !isEditing && <button type="button" onClick={() => setEditing({ ...editing, [item.id]: c?.value ?? item.value })}>Rediger<span className="visually-hidden"> – {ctx}</span></button>}
                      {item.pending && <button type="button" onClick={() => choose(item, { action: "keep" })}>Behold original<span className="visually-hidden"> – {ctx}</span></button>}
                      {item.pending && <button type="button" onClick={() => choose(item, { action: "skip" })}>Hopp over<span className="visually-hidden"> – {ctx}</span></button>}
                      {item.applied && <button type="button" onClick={() => choose(item, { action: "undo" })}>Angre endringen<span className="visually-hidden"> – {ctx}</span></button>}
                    </div>
                  </div>
                </div>
              </article>
            );
          })}
        </section>
      ))}
      <div className="sticky-actions">
        <button type="button" className="primary" disabled={busy} onClick={() => void submit()}>
          {busy ? "Lagrer …" : `Lagre ${Object.keys(choices).length} valg og lag ny fil`}
        </button>
        <a href={`#/jobber/${id}`}>Tilbake uten å lagre</a>
      </div>
    </div>
  );
}

import { useEffect, useState } from "react";
import { api, ApiError, type Settings } from "../api";

export function SettingsView({ onSaved }: { onSaved: () => void }) {
  const [s, setS] = useState<Settings | null>(null);
  const [key, setKey] = useState("");
  const [consent, setConsent] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [models, setModels] = useState<string[]>([]);

  useEffect(() => {
    api.settings().then((x) => { setS(x); setConsent(x.cloudConsent); }).catch((e) => setError(e instanceof ApiError ? e.message : "Kunne ikke hente innstillinger."));
  }, []);

  const run = async (f: () => Promise<unknown>, ok: string) => {
    setError(null); setMsg(null);
    try { await f(); setMsg(ok); setS(await api.settings()); onSaved(); }
    catch (e) { setError(e instanceof ApiError ? e.message : "Noe gikk galt."); }
  };

  if (!s) return <><h1>Innstillinger</h1>{error ? <div className="alert error" role="alert">{error}</div> : <p>Laster …</p>}</>;

  return (
    <div>
      <h1>Innstillinger</h1>
      <p role="status" aria-live="polite" className="status">{msg}</p>
      {error && <div className="alert error" role="alert">{error}</div>}

      <form onSubmit={(e) => { e.preventDefault(); void run(() => api.saveSettings({ localAiUrl: s.localAiUrl, localAiModel: s.localAiModel ?? "" }), "Lokal KI er lagret."); }}>
        <h2>Lokal KI-modell</h2>
        <p>En lokal modell kjører på denne maskinen (for eksempel med Ollama), og ingenting sendes ut. Modellen må kunne lese bilder.</p>
        <label htmlFor="lurl">Adresse</label>
        <input id="lurl" value={s.localAiUrl} onChange={(e) => setS({ ...s, localAiUrl: e.target.value })} inputMode="url" />
        <label htmlFor="lmodel">Modellnavn</label>
        <input id="lmodel" value={s.localAiModel ?? ""} list="modeller" onChange={(e) => setS({ ...s, localAiModel: e.target.value })} aria-describedby="lmodelh" />
        <datalist id="modeller">{models.map((m) => <option key={m} value={m} />)}</datalist>
        <p className="hint" id="lmodelh">Tomt felt slår av lokal KI. Velg en modell med bildestøtte.</p>
        <div className="actions">
          <button type="submit">Lagre</button>
          <button type="button" onClick={() => void run(async () => {
            const r = await api.testLocal();
            if (!r.ok) throw new ApiError(r.error ?? "Ingen kontakt.");
            setModels(r.models ?? []);
          }, "Kontakt med lokal KI. Modellene vises i forslagslisten.")}>Test tilkoblingen</button>
        </div>
      </form>

      <form onSubmit={(e) => { e.preventDefault(); void run(() => api.saveSettings({ cloudConsent: consent }), consent ? "Skytjenesten er godkjent." : "Godkjenningen er trukket tilbake."); }}>
        <h2>Skytjeneste (valgfritt)</h2>
        <p>{s.consentText}</p>
        <p>Leverandør: Anthropic. Modell: {s.cloudModel}. Bruk av lokal KI faller aldri automatisk tilbake til skytjenesten.</p>
        <div className="choice">
          <input type="checkbox" id="consent" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
          <label htmlFor="consent">Jeg forstår hva som sendes, og godkjenner at skytjenesten kan velges for enkeltjobber.</label>
        </div>
        <button type="submit">Lagre valget</button>
      </form>

      <form onSubmit={(e) => { e.preventDefault(); void run(async () => { await api.saveKey(key); setKey(""); }, "API-nøkkelen er lagret i Windows Legitimasjonsbehandling."); }}>
        <h3>API-nøkkel</h3>
        {!s.secureStorage && <p className="alert error">Sikker nøkkellagring er ikke tilgjengelig på denne maskinen. Skytjenesten kan ikke brukes.</p>}
        <p>{s.cloudKeyStored ? "En nøkkel er lagret." : "Ingen nøkkel er lagret."} Nøkkelen lagres i Windows Legitimasjonsbehandling og vises aldri igjen.</p>
        <label htmlFor="key">Ny nøkkel</label>
        <input id="key" type="password" autoComplete="off" spellCheck={false} value={key} onChange={(e) => setKey(e.target.value)} />
        <div className="actions">
          <button type="submit" disabled={!s.secureStorage || key.length === 0}>Lagre nøkkel</button>
          {s.cloudKeyStored && <button type="button" onClick={() => void run(() => api.deleteKey(), "Nøkkelen er slettet.")}>Slett nøkkel</button>}
        </div>
      </form>

      <form onSubmit={(e) => { e.preventDefault(); void run(() => api.saveSettings({ retentionDays: s.retentionDays, rendering: s.rendering, defaultProfile: s.defaultProfile }), "Innstillingene er lagret."); }}>
        <h2>Behandling og personvern</h2>
        <label htmlFor="ret">Slett jobber automatisk etter (dager)</label>
        <input id="ret" type="number" min={1} max={365} value={s.retentionDays} onChange={(e) => setS({ ...s, retentionDays: Number(e.target.value) })} />
        <div className="choice">
          <input type="checkbox" id="rend" checked={s.rendering} onChange={(e) => setS({ ...s, rendering: e.target.checked })} />
          <label htmlFor="rend">Kontroller utseendet i Word og PowerPoint på denne maskinen (anbefalt)</label>
        </div>
        <label htmlFor="prof">Standardprofil i rapporten</label>
        <select id="prof" value={s.defaultProfile} onChange={(e) => setS({ ...s, defaultProfile: e.target.value })}>
          <option value="no">Norsk regelverksprofil</option>
          <option value="en321">EN 301 549 V3.2.1 – kapittel 10</option>
          <option value="en411">EN 301 549 V4.1.1 – teknisk profil</option>
          <option value="bp">Beste praksis (WCAG 2.2 + WCAG2ICT)</option>
        </select>
        <button type="submit">Lagre</button>
      </form>
    </div>
  );
}

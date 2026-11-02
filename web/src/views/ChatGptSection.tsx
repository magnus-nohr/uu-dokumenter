import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, type ChatGptStatus } from "../api";

/** «Sign in with ChatGPT»: kobler appen til brukerens ChatGPT Plus/Pro-abonnement uten API-nøkkel. */
export function ChatGptSection({ secureStorage, onChanged }: { secureStorage: boolean; onChanged: () => void }) {
  const [st, setSt] = useState<ChatGptStatus | null>(null);
  const [consent, setConsent] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [waiting, setWaiting] = useState(false);
  const timer = useRef<number | undefined>(undefined);

  const load = useCallback(async () => {
    try {
      const x = await api.chatGpt();
      setSt(x);
      setConsent(x.consent);
      return x;
    } catch (e) { setError(e instanceof ApiError ? e.message : "Kunne ikke hente ChatGPT-status."); return null; }
  }, []);

  useEffect(() => { void load(); return () => window.clearTimeout(timer.current); }, [load]);

  // Mens innloggingen pågår i en annen fane: sjekk status jevnlig og kunngjør resultatet.
  const poll = useCallback(async () => {
    const x = await load();
    if (x?.connected) { setWaiting(false); setMsg(`Koblet til ChatGPT${x.account ? " som " + x.account : ""}.`); onChanged(); return; }
    if (x && !x.loginInProgress) { setWaiting(false); if (x.error) setError(x.error); return; }
    timer.current = window.setTimeout(() => void poll(), 2000);
  }, [load, onChanged]);

  const connect = async () => {
    setError(null); setMsg(null);
    // Fanen åpnes med en gang (i klikket), ellers stopper nettleseren den som sprettoppvindu.
    const w = window.open("about:blank", "_blank");
    try {
      if (!st?.consent) await api.chatGptSave({ consent: true });
      const { authorizeUrl } = await api.chatGptLogin();
      if (w) { w.opener = null; w.location.href = authorizeUrl; } else window.location.href = authorizeUrl;
      setWaiting(true);
      setMsg("Logg inn i fanen som ble åpnet. Denne siden oppdateres når du er ferdig.");
      void poll();
    } catch (e) {
      w?.close();
      setError(e instanceof ApiError ? e.message : "Kunne ikke starte innloggingen.");
    }
  };

  if (!st) return <section aria-labelledby="gpt-h"><h2 id="gpt-h">ChatGPT</h2><p>{error ?? "Laster …"}</p></section>;

  return (
    <section aria-labelledby="gpt-h">
      <h2 id="gpt-h">ChatGPT</h2>
      <p role="status" aria-live="polite" className="status">{msg}</p>
      {error && <div className="alert error" role="alert">{error}</div>}
      {!secureStorage && <p className="alert error">Sikker lagring (Windows Legitimasjonsbehandling) er ikke tilgjengelig. ChatGPT kan ikke kobles til.</p>}

      {st.connected ? (
        <>
          <p><strong>Du bruker ChatGPT-abonnementet ditt</strong>{st.account ? ` (${st.account})` : ""}.{" "}
            <a href={st.usageUrl} target="_blank" rel="noopener">Administrer bruk</a></p>
          <p className="hint">Bruken trekkes fra ditt ChatGPT Plus- eller Pro-abonnement. For Plus deles en femtimersgrense med andre apper.</p>
          <label htmlFor="gpt-model">Modell</label>
          <select id="gpt-model" value={st.model ?? ""} onChange={async (e) => {
            await api.chatGptSave({ model: e.target.value }); setMsg("Modellen er lagret."); await load(); onChanged();
          }}>
            <option value="">Velg automatisk</option>
            {st.models.map((m) => <option key={m} value={m}>{m}</option>)}
          </select>
          <div className="actions">
            <button type="button" onClick={async () => { await api.chatGptLogout(); setMsg("ChatGPT er koblet fra."); await load(); onChanged(); }}>
              Koble fra ChatGPT
            </button>
          </div>
        </>
      ) : (
        <>
          <p>{st.consentText}</p>
          <div className="choice">
            <input type="checkbox" id="gpt-consent" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
            <label htmlFor="gpt-consent">Jeg forstår hva som sendes, og vil bruke ChatGPT-kontoen min i appen.</label>
          </div>
          <div className="actions">
            <button type="button" className="primary" disabled={!consent || !secureStorage || waiting} onClick={() => void connect()}>
              {waiting ? "Venter på innlogging …" : "Koble til ChatGPT-kontoen"}
            </button>
          </div>
          <p className="hint">Du logger inn hos OpenAI i en ny fane. Appen får ikke se passordet ditt, og du trenger ingen API-nøkkel.
            Krever ChatGPT Plus eller Pro.</p>
        </>
      )}
    </section>
  );
}
